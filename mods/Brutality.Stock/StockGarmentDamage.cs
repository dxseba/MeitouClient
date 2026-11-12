using System.Numerics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

internal enum GarmentMaterial { Cloth, Leather, Metal }
internal readonly record struct GarmentDamageMark(BodyWoundEntry Entry, GarmentMaterial Material, float Severity);
internal sealed record GarmentDamageItem(string Record, string Section, int X, int Y, CharacterPart[] Parts, GarmentDamageMark[] Marks,
    int[]? VestSlots = null);
internal sealed record GarmentDamageActor(ulong Revision, GarmentDamageItem[] Items);
internal sealed record GarmentDamageFrame(long Tick, IReadOnlyDictionary<CharacterId, GarmentDamageActor> Actors);

/// <summary>Visual damage belongs to the individual item. The simulation publishes only currently worn damage.</summary>
internal sealed class StockGarmentDamage : ITickSystem
{
    const int MarkLimit = 6, FrameLimit = 32;
    sealed record Damage(long Revision, GarmentDamageMark[] Marks);
    sealed class ItemDamage { public volatile Damage? Current; }
    static readonly ConditionalWeakTable<ItemInstance, ItemDamage> damage = new();
    static readonly ConditionalWeakTable<CharacterCold, object> displayed = new();
    static long revision, itemRevision;
    static bool tracksEquipment;
    long seenRevision = -1, seenSpawns = -1;
    readonly Queue<GarmentDamageFrame> history = new();
    volatile GarmentDamageFrame[] frames = [];

    internal static void Install(ModContext context)
    {
        context.AddSystem("garment-damage", _ => new StockGarmentDamage(), SystemAnchor.AfterPart(StandardParts.Combat));
        try
        {
            var assembly = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterAssembly", throwOnError: true)!;
            context.Harmony.Patch(AccessTools.Method(assembly, "RefreshEquipment"),
                postfix: new HarmonyMethod(typeof(StockGarmentDamage), nameof(EquipmentChanged)));
            tracksEquipment = true;
        }
        catch (Exception error)
        {
            context.LogError($"Brutality garment equipment refresh unavailable: {error.GetBaseException().Message}");
        }
        StockGarmentDamageSave.Install(context);
    }

    internal static StockGarmentDamage? For(World world) => world.Systems.OfType<StockGarmentDamage>().FirstOrDefault();

    internal static GarmentDamageFrame? FrameAt(World world, long tick)
    {
        if (For(world) is not { } state) return null;
        var available = state.frames;
        for (int i = available.Length - 1; i >= 0; i--)
            if (available[i].Tick <= tick) return available[i];
        return null;
    }

    internal bool Deposit(ItemInstance item, in GarmentDamageMark mark)
    {
        if (item.Type != FcsRecordType.Armour || !Valid(mark)) return false;
        var prior = MarksOf(item) ?? [];
        var next = new List<GarmentDamageMark>(prior.Length + 1);
        foreach (var existing in prior)
        {
            if (SameOpening(existing, mark))
            {
                if (existing == mark || existing.Severity > mark.Severity) return false;
                continue;
            }
            next.Add(existing);
        }
        next.Add(mark);
        if (next.Count > MarkLimit) next.RemoveAt(0);
        Set(item, next.ToArray());
        return true;
    }

    static bool SameOpening(in GarmentDamageMark a, in GarmentDamageMark b)
    {
        var x = a.Entry.Mark;
        var y = b.Entry.Mark;
        return a.Material == b.Material && a.Entry.Region == b.Entry.Region && x.Segment == y.Segment
            && x.Shape.Kind == y.Shape.Kind && MathF.Abs(x.T - y.T) < .13f && Vector3.Dot(x.Side, y.Side) > .8f;
    }

    internal static GarmentDamageMark[]? MarksOf(ItemInstance item) =>
        damage.TryGetValue(item, out var row) ? row.Current?.Marks : null;

    internal static void Restore(ItemInstance item, GarmentDamageMark[] marks)
    {
        if (item.Type == FcsRecordType.Armour && marks.Length is > 0 and <= MarkLimit && marks.All(Valid)) Set(item, marks);
    }

    static bool Valid(GarmentDamageMark mark) => mark.Material is >= GarmentMaterial.Cloth and <= GarmentMaterial.Metal
        && float.IsFinite(mark.Severity) && mark.Severity is > 0 and <= 1
        && mark.Entry.Mark.Shape.Kind is WoundKind.Slash or WoundKind.Thrust or WoundKind.Crush
        && new BodyWoundState([mark.Entry]).IsValid;

    static void Set(ItemInstance item, GarmentDamageMark[] marks)
    {
        var target = damage.GetOrCreateValue(item);
        target.Current = new(Interlocked.Increment(ref itemRevision), marks);
        Interlocked.Increment(ref revision);
    }

    static void EquipmentChanged(CharacterCold cold)
    {
        if (displayed.TryGetValue(cold, out _) || cold.Inventory?.Items.Any(item => Equipped(item) && MarksOf(item) is not null) == true)
            Interlocked.Increment(ref revision);
    }

    static bool Equipped(ItemInstance item) => item.Type == FcsRecordType.Armour
        && item.Section is "head" or "shirt" or "armour" or "legs" or "boots" or "belt";

    public void SlowWorld(World world)
    {
        long current = Volatile.Read(ref revision);
        var table = world.Characters;
        if (current == 0 || seenRevision == current && seenSpawns == table.Spawns && (tracksEquipment || world.Tick % 15 != 0)) return;
        (seenRevision, seenSpawns) = (current, table.Spawns);
        var previous = frames.LastOrDefault()?.Actors;
        var actors = new Dictionary<CharacterId, GarmentDamageActor>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Inventory: { } inventory, Appearance: { } look } cold) continue;
            var id = table.IdOf(slot);
            if (ActorView(inventory, look, previous?.GetValueOrDefault(id)) is not { } actor)
            {
                displayed.Remove(cold);
                continue;
            }
            displayed.GetValue(cold, static _ => new());
            actors.Add(id, actor);
        }
        if (previous is null ? actors.Count == 0 : previous.Count == actors.Count &&
            actors.All(row => ReferenceEquals(row.Value, previous.GetValueOrDefault(row.Key)))) return;
        history.Enqueue(new(world.Tick + 1, actors));
        while (history.Count > FrameLimit) history.Dequeue();
        frames = history.ToArray();
    }

    static GarmentDamageActor? ActorView(Inventory inventory, CharacterAppearance look, GarmentDamageActor? previous)
    {
        List<GarmentDamageItem>? items = null;
        var hash = new StateHasher();
        foreach (var item in inventory.Items)
        {
            if (!Equipped(item) || !damage.TryGetValue(item, out var cell) || cell.Current is not { } row) continue;
            var parts = look.Parts.Where(part => part.Record.StringId == item.Record).ToArray();
            // The body shader indexes the first three vest layers in appearance order.
            var vestSlots = Enumerable.Range(0, Math.Min(3, look.BodyLayers.Count))
                .Where(slot => look.BodyLayers[slot].Record.StringId == item.Record).ToArray();
            if (parts.Length == 0 && vestSlots.Length == 0) continue;
            (items ??= []).Add(new(item.Record, item.Section, item.X, item.Y, parts, row.Marks,
                vestSlots.Length > 0 ? vestSlots : null));
            hash.Add(row.Revision);
            hash.Add(Rng.StableHash(item.Section));
            hash.Add(item.X);
            hash.Add(item.Y);
            hash.Add(vestSlots.Length);
            foreach (int slot in vestSlots) hash.Add(slot);
        }
        if (items is null) return null;
        var version = hash.Value;
        if (previous?.Revision == version && previous.Items.Length == items.Count && items.SelectMany(item => item.Parts)
                .SequenceEqual(previous.Items.SelectMany(item => item.Parts), ReferenceEqualityComparer.Instance)) return previous;
        return new(version, items.ToArray());
    }
}
