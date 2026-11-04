using System.Collections.ObjectModel;
using System.Numerics;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public readonly record struct BodyFragmentPlacement(Vector3 Position, float Yaw);
public sealed record BodyFragmentProxyView(CharacterId Proxy, CharacterId Owner, int StableKey, BodyFragmentPlacement Placement);
public sealed record BodyFragmentProxyFrame(long Tick, IReadOnlyDictionary<CharacterId, BodyFragmentProxyView> Proxies);

/// <summary>A native carry/loot target for the negative torso half. Only inventory is shared; medical state and carry links are independent.</summary>
public sealed partial class BodyFragmentProxySystem(Action<CharacterCold>? refreshEquipment = null) : ITickSystem, IModSaveData, IStateHashed
{
    sealed class Half
    {
        public required CharacterId Owner, Proxy;
        public required CharacterCold Source, Cold;
        public BodyFragmentPlacement? RestoredPlacement;
        public BodyFragmentPlacement LastPlacement;
        public string? LastCarrier;
        public ItemStamp[] Items = [];
    }
    readonly record struct ItemStamp(Meitou.Simulation.Items.ItemInstance Item, string Section, int X, int Y, int Level, float Quality, string Material, string Maker, string Colour);
    readonly Dictionary<CharacterId, Half> halves = [];
    readonly Queue<BodyFragmentProxyFrame> history = new();
    volatile BodyFragmentProxyFrame[] frames = [];
    World? world;
    public static BodyFragmentProxySystem? ForWorld(World world) => world.Systems.OfType<BodyFragmentProxySystem>().FirstOrDefault();
    public static void Install(ModContext context)
    {
        InstallOrderPersistence(context.Harmony);
        InstallRetention(context.Harmony);
        var assemblyType = typeof(World).Assembly.GetType("Meitou.Simulation.CharacterAssembly", throwOnError: true)!;
        var assembly = Activator.CreateInstance(assemblyType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, [context.Population], null)!;
        var refresh = AccessTools.Method(assemblyType, "RefreshEquipment");
        context.AddSystem("body-fragments", _ => new BodyFragmentProxySystem(cold => refresh.Invoke(assembly, [cold])), SystemAnchor.End);
    }
    public bool TryProxy(CharacterId owner, int stableKey, out CharacterId proxy)
    {
        if (TryProxy(owner, stableKey, out proxy, out _)) return true;
        proxy = CharacterId.None; return false;
    }
    public bool TryProxy(CharacterId owner, int stableKey, out CharacterId proxy, out CharacterCold cold)
    {
        if (stableKey == 1 && halves.TryGetValue(owner, out var half) && world?.Characters.TryResolveNext(half.Proxy, out _) == true)
        { proxy = half.Proxy; cold = half.Cold; return true; }
        proxy = CharacterId.None; cold = null!; return false;
    }
    public bool TryTakeRestoredPlacement(CharacterId owner, int stableKey, out BodyFragmentPlacement placement)
    {
        if (stableKey == 1 && halves.TryGetValue(owner, out var half) && half.RestoredPlacement is { } saved)
        { placement = saved; half.RestoredPlacement = null; return true; }
        placement = default; return false;
    }
    public bool HasPendingProxy(CharacterId owner, int stableKey) => stableKey == 1 && world is { } value && BrutalityRuntime.For(value) is { } gameplay
        && value.Characters.TryResolveNext(owner, out int slot) && value.Characters.Cold(slot) is { } cold && gameplay.SaveKeyOf(cold) is { } key && pending.ContainsKey(key);
    public BodyFragmentProxyFrame? FrameAt(long tick)
    {
        var available = frames;
        for (int i = available.Length - 1; i >= 0; i--) if (available[i].Tick <= tick) return available[i];
        return null;
    }
    void Bind(World value)
    {
        if (world is not null && !ReferenceEquals(world, value)) throw new InvalidOperationException("A fragment proxy system belongs to one world.");
        world = value;
    }
    public void Inputs(World value, IReadOnlyList<SimCommand> commands)
    {
        Bind(value);
        if (BrutalityRuntime.For(value) is not { } gameplay) return;
        gameplay.BindWorld(value);
        Restore(value, gameplay);
        RestoreQueues(value, gameplay);
        // Spawning before Schedule allows every native system to size its slot buffers for the new carry target.
        foreach (var actor in gameplay.ActorViews.OrderBy(actor => Rng.Key(actor.Character)))
            if (actor.Torso is { IsValid: true } && value.Characters.TryResolveNext(actor.Character, out int slot)
                && value.Characters.Cold(slot) is { Medical.Dead: true, Race: not null } source)
                Ensure(value, actor.Character, source, null);
    }
    Half? Ensure(World value, CharacterId owner, CharacterCold source, BodyFragmentPlacement? placement)
    {
        if (halves.TryGetValue(owner, out var old) && value.Characters.TryResolveNext(old.Proxy, out _)) return old;
        if (!value.Characters.TryResolveNext(owner, out int slot) || source.Race is not { } race) return null;
        var stats = new CharacterStats { Scale = source.Stats?.Scale ?? 1 };
        foreach (var stat in StatsInfo.RecordStats) if (source.Stats is { } original) stats[stat] = original[stat];
        var medical = MedicalState.Create(race, stats.Strength); medical.Butcher(); medical.CollapseOnset = false;
        var cold = new CharacterCold
        {
            Name = source.Name + " — Lower body", Faction = source.Faction, Race = race, Stats = stats, Medical = medical,
            Fighter = new Fighter(null), Inventory = source.Inventory, Appearance = source.Appearance, IsPlayer = false, SquadId = -1,
            DiedTick = value.Tick, Orders = CharacterOrders.None,
        };
        RegisterLootOwner(cold, source);
        BrutalityRuntime.ControlPhysics(cold, true);
        ref readonly var originalHot = ref value.Characters.Next[slot];
        var at = placement ?? new BodyFragmentPlacement(originalHot.Position, originalHot.Yaw);
        var proxy = value.Characters.Spawn(new() { Position = at.Position, Yaw = at.Yaw, Health = 0 }, cold, value.Tick);
        var half = new Half { Owner = owner, Proxy = proxy, Source = source, Cold = cold, RestoredPlacement = placement, LastPlacement = at, Items = InventoryStamp(source) };
        halves[owner] = half; return half;
    }
    static ItemStamp[] InventoryStamp(CharacterCold cold) => cold.Inventory?.Items.Select(item => new ItemStamp(item, item.Section, item.X, item.Y,
        item.Level, item.Quality, item.MaterialId, item.CompanyId, item.ColourId)).ToArray() ?? [];
    public void SlowWorld(World value)
    {
        Bind(value); RememberQueues(value); var physics = value.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault()?.FrameAt(value.Tick + 1);
        var shown = new Dictionary<CharacterId, BodyFragmentProxyView>();
        foreach (var (owner, half) in halves.OrderBy(row => Rng.Key(row.Key)).ToArray())
        {
            bool ownerPresent = value.Characters.TryResolveNext(owner, out _);
            if (!value.Characters.TryResolveNext(half.Proxy, out int proxySlot)) { Remember(half); halves.Remove(owner); continue; }
            if (!ownerPresent && half.Cold.CarriedBy.IsNone)
            {
                Remember(half); BrutalityRuntime.ControlPhysics(half.Cold, false); value.Characters.Remove(half.Proxy); halves.Remove(owner); continue;
            }
            half.Cold.DiedTick = value.Tick; // The owner controls the shared corpse's decay; this proxy must not expire independently.
            half.Cold.Inventory = half.Source.Inventory;
            var stamp = InventoryStamp(half.Source);
            if (!half.Items.SequenceEqual(stamp)) { refreshEquipment?.Invoke(half.Source); half.Items = stamp; }
            half.Cold.Appearance = half.Source.Appearance;
            half.Cold.Fighter = new Fighter(null);
            ref var hot = ref value.Characters.Next[proxySlot];
            if (half.Cold.CarriedBy.IsNone && half.RestoredPlacement is null && physics?.Characters.GetValueOrDefault(owner)?.Detached.FirstOrDefault(piece => piece.StableKey == 1) is { } piece)
            { hot.Position = piece.Position; hot.Velocity = Vector3.Zero; hot.Goal = new(hot.Position.X, hot.Position.Z); }
            half.LastPlacement = new(hot.Position, hot.Yaw);
            if (value.Characters.TryResolveNext(half.Cold.CarriedBy, out int holderSlot) && value.Characters.Cold(holderSlot) is { } holder)
                half.LastCarrier = BrutalityRuntime.For(value)?.SaveKeyOf(holder);
            else half.LastCarrier = null;
            shown[half.Proxy] = new(half.Proxy, owner, 1, half.LastPlacement);
        }
        history.Enqueue(new(value.Tick + 1, new ReadOnlyDictionary<CharacterId, BodyFragmentProxyView>(shown)));
        while (history.Count > 64) history.Dequeue(); frames = history.ToArray();
    }
}
