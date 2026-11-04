using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Materializes the pinned standalone/required dead owners through the native character reader, once per full save handle.</summary>
internal static partial class CorpseSaveAdapter
{
    const string TorsoCorpse = "meitou torso corpse", HeadCorpse = "meitou head corpse", Positive = "meitou torso positive", Release = "meitou torso positive release", CarrySide = "meitou carry torso side";
    sealed record Place(BodyFragmentPlacement Placement, bool Release = false);
    static readonly ConditionalWeakTable<CharacterCold, Place> positive = new();
    static readonly Type characters = typeof(SaveLoader).Assembly.GetType("Meitou.Simulation.Saving.SavedCharacters", true)!;
    static readonly Type assembly = typeof(SaveLoader).Assembly.GetType("Meitou.Simulation.CharacterAssembly", true)!;
    static readonly MethodInfo read = AccessTools.Method(characters, "Read"), make = AccessTools.Method(characters, "Make");
    static readonly MethodInfo modEntries = AccessTools.Method(typeof(ModHost).Assembly.GetType("Meitou.Modding.ModSaveFile", true)!, "Read");
    internal static bool TryTakePositive(CharacterCold cold, out BodyFragmentPlacement placement) => TryTakePositive(cold, out placement, out _);
    internal static bool TryTakePositive(CharacterCold cold, out BodyFragmentPlacement placement, out bool release)
    {
        if (positive.TryGetValue(cold, out var saved)) { positive.Remove(cold); placement = saved.Placement; release = saved.Release; return true; }
        placement = default; release = false; return false;
    }
    static IReadOnlyDictionary<string, byte[]> Entries(SaveGame save)
    {
        if (!save.ModData.TryGetValue(LegacyGoreSave.ModId, out var bytes)) return new Dictionary<string, byte[]>();
        try { return (Dictionary<string, byte[]>)modEntries.Invoke(null, [bytes])!; }
        catch (TargetInvocationException) { return new Dictionary<string, byte[]>(); }
    }
    internal static void Materialize(World world, SaveGame save, PopulationData data, SaveLoadOptions? options, LoadedSave loaded)
    {
        if (world.Saved is not { } context || BrutalityRuntime.For(world) is null) return;
        options ??= new();
        bool dllSave = save.ModData.ContainsKey(LegacyGoreSave.ModId);
        var entries = Entries(save);
        IReadOnlyList<BrutalityDurableState> cuts = dllSave ? entries.TryGetValue("gore", out var bytes) ? BrutalityStateCodec.Read(new MemoryStream(bytes, false)) : [] : LegacyGoreSave.Read(save);
        var byKey = cuts.Where(state => state.Head is not null || state.Torso is not null).GroupBy(state => state.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in save.Characters)
        {
            if (source.GameState is { } state && state.Bools.GetValueOrDefault("carrying") && SaveIdentityAdapter.KeyFor(Hand.Read(state, "carrying")) is { } partner) required.Add(partner);
            if (source.Ai is not { } ai) continue;
            foreach (var (key, row) in ai.Ints)
                if (key.StartsWith("tjob", StringComparison.Ordinal) && int.TryParse(key.AsSpan(4), out int index) && index >= 0
                    && BodyFragmentProxySystem.HalfOrder((TaskType)row) && SaveIdentityAdapter.KeyFor(Hand.Read(ai, "tjobT" + index)) is { } target) required.Add(target);
        }
        if (entries.TryGetValue("body-fragments", out var proxies))
        {
            required.UnionWith(BodyFragmentProxySystem.ReadQueuedTargets(proxies));
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(proxies);
                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && document.RootElement.TryGetProperty("Halves", out var halves)
                    && halves.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var half in halves.EnumerateArray())
                        if (half.ValueKind == System.Text.Json.JsonValueKind.Object && half.TryGetProperty("Carrier", out var carrier) && carrier.ValueKind == System.Text.Json.JsonValueKind.String
                            && half.TryGetProperty("Owner", out var owner) && owner.ValueKind == System.Text.Json.JsonValueKind.String && owner.GetString() is { } key) required.Add(key);
            }
            catch (System.Text.Json.JsonException) { }
        }
        var present = new Dictionary<string, CharacterId>(StringComparer.Ordinal);
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
            if (world.Characters.Next[slot].Alive && world.Characters.Cold(slot)?.Save is { } link && SaveIdentityAdapter.KeyFor(link.Source.Handle) is { } key) present[key] = world.Characters.IdOf(slot);
        var races = new Dictionary<string, RaceData?>(StringComparer.Ordinal); var parts = new Dictionary<string, BodyPartTemplate?>(StringComparer.Ordinal);
        var assembler = Activator.CreateInstance(assembly, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [data], null)!;
        foreach (var source in save.Characters)
        {
            if (SaveIdentityAdapter.KeyFor(source.Handle) is not { } key || !byKey.ContainsKey(key) || present.ContainsKey(key)) continue;
            bool standalone = source.Platoon.Record.Bools.GetValueOrDefault(TorsoCorpse) || source.Platoon.Record.Bools.GetValueOrDefault(HeadCorpse)
                || source.GameState?.Bools.GetValueOrDefault(HeadCorpse) == true;
            if (!standalone && !required.Contains(key)) continue;
            var prepared = read.Invoke(null, [source, source.Platoon.Name, data, options.Appearances, races, parts, (Action<string>)loaded.Notes.Add]);
            if (prepared is null || AccessTools.Property(prepared.GetType(), "Medical").GetValue(prepared) is not MedicalState { Dead: true }) continue;
            int faction = data.FactionIndex(source.OwnerFactionId.Length > 0 ? source.OwnerFactionId : source.Platoon.FactionId);
            var squad = new Squad { Id = -1, TemplateId = source.Platoon.SquadTemplate, Faction = faction, Position = source.Position };
            var id = (CharacterId)make.Invoke(null, [world, prepared, source.Platoon.Name, squad, faction, world.Player.Faction, false,
                source.Position, SaveRotation.YawOf(source.Rotation), CharacterTask.Idle, SpeedMode.Free, data, assembler, context, null, loaded.Notes])!;
            present[key] = id; context.Spawned[(source.Handle.C, source.Handle.I)] = id; loaded.Characters.Add(id);
            if (!context.ByName.TryGetValue(source.Platoon.Name, out var platoon))
            {
                platoon = new SavedPlatoon { Source = source.Platoon, Pinned = false, Touched = true, Anchor = new(source.Platoon.Position.X, source.Platoon.Position.Z) };
                context.Platoons.Add(platoon); context.ByName[platoon.Name] = platoon;
            }
            var member = platoon.Members.FirstOrDefault(member => member.Slot == source.Handle.I);
            if (member is null) platoon.Members.Add(member = new SavedMember { Source = source });
            member.Live = id; member.Dead = true; platoon.Touched = true;
        }
        if (!dllSave) LegacyCarries(world, save, byKey, present);
        // The enclosing loader hook imports complete legacy metadata before the first gameplay bind.
    }
    static void LegacyCarries(World world, SaveGame save, IReadOnlyDictionary<string, BrutalityDurableState> cuts, IReadOnlyDictionary<string, CharacterId> present)
    {
        foreach (var source in save.Characters)
        {
            if (SaveIdentityAdapter.KeyFor(source.Handle) is not { } key || !cuts.TryGetValue(key, out var cut) || cut.Torso is null || !present.TryGetValue(key, out var id)) continue;
            var cold = world.Characters.Cold(id.Slot)!;
            var state = source.GameState;
            var place = PlaceOf(state?.Strings.GetValueOrDefault(Positive), 4);
            var release = PlaceOf(state?.Strings.GetValueOrDefault(Release), 3);
            if (release is { } drop)
            { positive.Remove(cold); positive.Add(cold, new(drop with { Yaw = place?.Yaw ?? SaveRotation.YawOf(source.Rotation) }, true)); }
            else if (place is { } saved)
            { positive.Remove(cold); positive.Add(cold, new(saved)); }
        }
        var sources = save.Characters.Where(source => !source.Handle.IsNull).GroupBy(source => SaveIdentityAdapter.KeyFor(source.Handle)!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var carrier in sources.Values)
        {
            if (carrier.GameState is not { } state || !state.Bools.GetValueOrDefault("carrying") || state.Ints.GetValueOrDefault(CarrySide) == 1
                || SaveIdentityAdapter.KeyFor(carrier.Handle) is not { } carrierKey || SaveIdentityAdapter.KeyFor(Hand.Read(state, "carrying")) is not { } ownerKey
                || !cuts.TryGetValue(ownerKey, out var cut) || cut.Torso is null) continue;
            if (!sources.TryGetValue(ownerKey, out var source)) continue;
            BodyFragmentProxySystem.ForWorld(world)?.ImportLegacy(new(ownerKey, 1, carrierKey, source.Position.X, source.Position.Y, source.Position.Z, SaveRotation.YawOf(source.Rotation)));
            if (present.TryGetValue(carrierKey, out var holder) && world.Characters.Cold(holder.Slot)?.Save is { } carrierLink) carrierLink.CarryPartner = Hand.Null;
            if (present.TryGetValue(ownerKey, out var body) && world.Characters.Cold(body.Slot)?.Save is { } bodyLink) bodyLink.CarryPartner = Hand.Null;
        }
        LegacyOrders(world, cuts, present, sources);
    }
    static void LegacyOrders(World world, IReadOnlyDictionary<string, BrutalityDurableState> cuts, IReadOnlyDictionary<string, CharacterId> present,
        IReadOnlyDictionary<string, SaveCharacter> sources)
    {
        if (BodyFragmentProxySystem.ForWorld(world) is not { } proxies) return;
        foreach (var (key, id) in present)
        {
            if (world.Characters.Cold(id.Slot)?.SavedOrders is not { } orders) continue;
            var converted = new List<BodyFragmentProxySystem.SavedHalfOrder>();
            for (int index = 0; index < orders.Count; index++)
            {
                var row = orders[index];
                if (!BodyFragmentProxySystem.HalfOrder(row.Row) || row.Kind is < 0 or > 4
                    || SaveIdentityAdapter.KeyFor(row.Hand) is not { } target || !cuts.TryGetValue(target, out var cut) || cut.Torso is null) continue;
                int side = row.Kind >= 2 ? 0 : 1, kind = row.Kind is 1 or 4 ? 1 : 0;
                converted.Add(new(index, target, side, (int)row.Row, kind, row.Point.X, row.Point.Y, row.Point.Z));
                if (side == 1 && sources.TryGetValue(target, out var body))
                    proxies.ImportLegacy(new(target, 1, null, body.Position.X, body.Position.Y, body.Position.Z, SaveRotation.YawOf(body.Rotation)));
            }
            if (converted.Count > 0) proxies.ImportLegacyQueue(new(key, converted.ToArray()));
        }
    }
    static BodyFragmentPlacement? PlaceOf(string? text, int count)
    {
        if (text is null) return null; var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != count) return null; Span<float> values = stackalloc float[4];
        for (int n = 0; n < count; n++) if (!float.TryParse(fields[n], NumberStyles.Float, CultureInfo.InvariantCulture, out values[n]) || !float.IsFinite(values[n])) return null;
        return new(new(values[0], values[1], values[2]), values[3]);
    }
}
