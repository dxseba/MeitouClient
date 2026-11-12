using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Saves the stump beside native medical state and the visible limb beside its native ground item.</summary>
internal static class StockLimbSave
{
    const string ActorKey = "dxseba brutality limbs", GroundKey = "dxseba brutality limb";
    const int Version = 1, ActorLimit = 4_000, GroundLimit = 160_000;
    static readonly JsonSerializerOptions json = new() { IncludeFields = true, IgnoreReadOnlyProperties = true, MaxDepth = 12 };
    static readonly ConditionalWeakTable<World, State> worlds = new();
    static readonly ConditionalWeakTable<FcsRecord, ReadGround> readGround = new();
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed record Cut(LimbCutState Shape, Vector2 Travel, ulong Seed, string Key);
    sealed record Actor(int Version, Cut[] Cuts);
    sealed record Ground(int Version, string Key, Pose? Frame);
    sealed record ReadGround(Ground? Value);
    internal sealed record Pose(Matrix4x4 World, Matrix4x4[] Birth, SavedFragmentPiece Fragment);
    internal readonly record struct Item(int Id, Pose Frame);

    sealed class State
    {
        internal readonly ConcurrentDictionary<int, Ground> Drawn = new();
        internal long LastSpawn = -1, GroundRevision = -1;
        internal volatile IReadOnlyDictionary<string, Item> Loaded = new Dictionary<string, Item>(StringComparer.Ordinal);
    }

    sealed record Mark(Ground Data, ZoneCoordinate Zone, string? RecordId);
    sealed class Capture
    {
        internal readonly Dictionary<ItemInstance, Mark> Items = new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<SaveItem, Mark> Written = new();
    }
    [ThreadStatic] static Capture? capturing;

    internal static void Install(ModContext context)
    {
        try
        {
            var writer = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name == "WriteCharacter" && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
            var ground = AccessTools.Method(typeof(GroundItemSave), nameof(GroundItemSave.Write));
            var items = AccessTools.Method(typeof(SavedKit), "FromItem", [typeof(ItemInstance), typeof(string)]);
            context.Harmony.Patch(writer, postfix: Patch(nameof(WrittenActor)));
            context.Harmony.Patch(ground, prefix: Patch(nameof(BeforeGround)), postfix: Patch(nameof(WrittenGround)), finalizer: Patch(nameof(AfterGround)));
            context.Harmony.Patch(items, postfix: Patch(nameof(WrittenItem)));
            context.Harmony.Patch(AccessTools.Method(typeof(SaveLoader), nameof(SaveLoader.Load)), postfix: Patch(nameof(Loaded)));
        }
        catch (Exception error)
        {
            context.LogError($"Brutality stock limb persistence unavailable: {error.GetBaseException().Message}");
        }
    }

    static HarmonyMethod Patch(string name) => new(typeof(StockLimbSave), name);

    static string Key(World world, in StockLimbRules.Request cut) => cut.ItemKey ?? string.Create(CultureInfo.InvariantCulture,
        $"{world.Seed:x16}-{Rng.Key(cut.Owner):x16}-{cut.Tick:x16}-{cut.Shape.LimbSlot}");

    internal static Item? For(World world, in StockLimbRules.Request cut) =>
        worlds.TryGetValue(world, out var state) && state.Loaded.TryGetValue(Key(world, cut), out var item) ? item : null;

    internal static bool IsCustomItem(World world, int id) => worlds.TryGetValue(world, out var state)
        && (state.Drawn.ContainsKey(id) || state.Loaded.Values.Any(item => item.Id == id));

    internal static void Publish(World world, int item, in StockLimbRules.Request cut, Pose pose) =>
        worlds.GetOrCreateValue(world).Drawn[item] = new(Version, Key(world, cut), pose);

    internal static void Remove(World world, int item)
    {
        if (worlds.TryGetValue(world, out var state)) state.Drawn.TryRemove(item, out _);
    }

    internal static void Step(World world)
    {
        var state = worlds.GetOrCreateValue(world);
        var table = world.Characters;
        if (state.LastSpawn == table.Spawns && state.GroundRevision == world.GroundItems.Revision) return;
        bool newActors = state.LastSpawn != table.Spawns;
        state.LastSpawn = table.Spawns;
        if (state.GroundRevision != world.GroundItems.Revision)
        {
            var items = new Dictionary<string, Item>(StringComparer.Ordinal);
            var duplicate = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ground in world.GroundItems.All)
                if (Read(ground.Item.Saved) is { Frame: { } pose } data && !items.TryAdd(data.Key, new(ground.Id, pose)))
                    duplicate.Add(data.Key);
            foreach (string key in duplicate) items.Remove(key);
            state.Loaded = items;
            state.GroundRevision = world.GroundItems.Revision;
        }
        if (!newActors) return;
        for (int slot = 0; slot < table.HighWater; slot++)
            if (table.Next[slot].Alive && table.Cold(slot) is { Medical: { } medical, Race: { } race, Save.Source.GameState: { } source }
                && Parse<Actor>(source, ActorKey, ActorLimit) is { Version: Version, Cuts.Length: > 0 and <= 4 } actor)
            {
                var id = table.IdOf(slot);
                foreach (var cut in actor.Cuts)
                {
                    if (cut is not { Shape.IsValid: true } || !ValidKey(cut.Key) || cut.Key[^1] != '0' + cut.Shape.LimbSlot
                        || !float.IsFinite(cut.Travel.LengthSquared())
                        || medical.LimbPart(cut.Shape.LimbSlot) is not { Limb: LimbState.Stump } part) continue;
                    string[] records = race.SeveredLimbs.Where(limb => limb.Slot == cut.Shape.LimbSlot).Select(limb => limb.TargetStringId).ToArray();
                    if (StockLimbRules.Restore(world, new(id, world.Tick - 1, cut.Shape, cut.Travel, cut.Seed, part.Template.BoneName2,
                            records, race.IsRobot, Restoring: true, ItemKey: cut.Key)) && trace)
                        Console.WriteLine($"Brutality stock restored limb stump owner={id} slot={cut.Shape.LimbSlot}");
                }
            }
    }

    static void Loaded(World world)
    {
        if (StockLimbRules.Enabled) Step(world);
    }

    internal static void Migrate(FcsRecord state, IReadOnlyList<LimbCutState> shapes, ulong seed)
    {
        if (state.Strings.ContainsKey(ActorKey)) return;
        var cuts = shapes.Select(shape => new Cut(shape, default, Rng.Mix(seed ^ (uint)shape.LimbSlot),
            string.Create(CultureInfo.InvariantCulture, $"{seed:x16}-{0UL:x16}-{0UL:x16}-{shape.LimbSlot}"))).ToArray();
        state.Strings[ActorKey] = JsonSerializer.Serialize(new Actor(Version, cuts), json);
    }

    static void WrittenActor(World world, CharacterCold cold, CharacterId id, SaveCharacter __result)
    {
        if (id.IsNone || __result.GameState is not { } state || cold.Medical is not { } medical) return;
        state.Strings.Remove(ActorKey);
        if (StockLimbRules.For(world, id) is not { } cuts) return;
        var valid = cuts.Where(cut => medical.LimbPart(cut.Shape.LimbSlot)?.Limb == LimbState.Stump)
            .Select(cut => new Cut(cut.Shape, cut.Travel, cut.Seed, Key(world, cut))).ToArray();
        if (valid.Length > 0) state.Strings[ActorKey] = JsonSerializer.Serialize(new Actor(Version, valid), json);
    }

    static void BeforeGround(World world, out Capture? __state)
    {
        __state = capturing;
        capturing = null;
        if (!worlds.TryGetValue(world, out var state)) return;
        foreach (var ground in world.GroundItems.All)
        {
            if (!state.Drawn.TryGetValue(ground.Id, out var data) && Read(ground.Item.Saved) is null)
            {
                if (ground.Fall is not { } fall || StockLimbRules.For(world, fall.Character) is not { } cuts) continue;
                var cut = cuts.FirstOrDefault(request => request.Bone == fall.Bone && fall.Tick >= request.Tick
                    && fall.Tick - request.Tick <= 2 && request.Records.Contains(ground.Item.Record));
                if (!cut.Shape.IsValid) continue;
                data = new(Version, Key(world, cut), null);
            }
            else data ??= Read(ground.Item.Saved);
            if (data is null) continue;
            (capturing ??= new()).Items[ground.Item] = new(data, WorldLayout.ZoneOf(ground.Position.X, ground.Position.Z), ground.Item.Saved?.StringId);
        }
    }

    static void WrittenItem(ItemInstance i, SaveItem __result)
    {
        if (capturing is { } capture && capture.Items.TryGetValue(i, out var mark)) capture.Written[__result] = mark;
    }

    static void WrittenGround(SaveGame game)
    {
        if (capturing is not { } capture) return;
        var updated = new HashSet<(ZoneCoordinate Zone, string Id)>();
        foreach (var (item, mark) in capture.Written)
            if (item.Written is { } record)
            {
                Write(record, mark.Data);
                updated.Add((mark.Zone, record.StringId));
                if (mark.RecordId is { } source) updated.Add((mark.Zone, source));
            }
        // Stock leaves an unchanged zone untouched. Refresh the same FCS record by zone and its native id in that case.
        foreach (var mark in capture.Items.Values)
            if (mark.RecordId is { } id && !updated.Contains((mark.Zone, id)) && game.Zones.TryGetValue(mark.Zone, out var zone)
                && zone.File.Records.Find(record => record.StringId == id && record.RecordType == FcsRecordType.Item) is { } record)
                Write(record, mark.Data);
    }

    static void Write(FcsRecord record, Ground data)
    {
        var valid = data.Frame is { } pose && Valid(pose) ? data : data with { Frame = null };
        string text = JsonSerializer.Serialize(valid, json);
        if (text.Length > GroundLimit) text = JsonSerializer.Serialize(valid with { Frame = null }, json);
        record.Strings[GroundKey] = text;
    }

    static Exception? AfterGround(Exception? __exception, Capture? __state) { capturing = __state; return __exception; }

    static Ground? Read(FcsRecord? record) => record is null ? null : readGround.GetValue(record, static source =>
    {
        var data = Parse<Ground>(source, GroundKey, GroundLimit);
        if (data is not { Version: Version } || !ValidKey(data.Key)) return new(null);
        return new(data.Frame is { } frame && !Valid(frame) ? data with { Frame = null } : data);
    }).Value;

    static T? Parse<T>(FcsRecord record, string key, int maximum) where T : class
    {
        if (record.Strings.GetValueOrDefault(key) is not { Length: > 0 } text || text.Length > maximum) return null;
        try { return JsonSerializer.Deserialize<T>(text, json); }
        catch (JsonException) { return null; }
    }

    static bool ValidKey(string? key) => key is { Length: 52 } && key[16] == '-' && key[33] == '-' && key[50] == '-'
        && key[^1] is >= '0' and <= '3' && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or '-');

    static bool Valid(Pose pose) => pose.Birth is { Length: > 0 and <= 1024 } && pose.Birth.All(MatrixValid)
        && MatrixValid(pose.World) && Matrix4x4.Invert(pose.World, out _) && AdvancedFragmentScene.Valid(pose.Fragment)
        && pose.Fragment.Key is >= 10 and <= 13;

    static bool MatrixValid(Matrix4x4 matrix) => float.IsFinite(matrix.GetDeterminant())
        && float.IsFinite(matrix.Translation.LengthSquared()) && matrix.Translation.LengthSquared() < 1e14f;
}
