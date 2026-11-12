using Meitou.Game.Host;
using Meitou.Simulation.Player;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality;

/// <summary>Gives the detached half its own native carry target while sharing the original corpse's inventory.</summary>
internal sealed class StockTorsoProxies : ITickSystem
{
    internal sealed class ProxyFrame(Dictionary<CharacterId, CharacterId> proxies, Dictionary<CharacterId, CharacterId> owners)
    {
        readonly IReadOnlyDictionary<CharacterId, CharacterId> byOwner = new ReadOnlyDictionary<CharacterId, CharacterId>(proxies);
        readonly IReadOnlyDictionary<CharacterId, CharacterId> byProxy = new ReadOnlyDictionary<CharacterId, CharacterId>(owners);
        internal bool TryProxy(CharacterId owner, out CharacterId proxy) => byOwner.TryGetValue(owner, out proxy);
        internal bool TryOwner(CharacterId proxy, out CharacterId owner) => byProxy.TryGetValue(proxy, out owner);
    }

    sealed record Half(CharacterId Owner, CharacterId Proxy, CharacterCold Source, CharacterCold Cold);
    sealed record LootAlias(CharacterId Id, CharacterCold Cold);
    internal sealed record PositionOrder(CharacterId Target, Vector3 Position) : OrderCommand;

    static readonly ConditionalWeakTable<World, StockTorsoProxies> worlds = new();
    static readonly ConditionalWeakTable<CharacterCold, LootAlias> lootOwners = new();
    static FieldInfo? uiSession;
    static readonly FieldInfo? nativeRagdolls = AccessTools.Field(typeof(RagdollSystem), "_ragdolls");
    static readonly MethodInfo? shiftNative = AccessTools.Method(typeof(RagdollSystem), "Shift");
    [ThreadStatic] static World? corpseUpdate;

    readonly Dictionary<CharacterId, Half> halves = [];
    readonly Dictionary<CharacterId, Half> byProxy = [];
    readonly ConcurrentDictionary<CharacterId, Vector3> drawn = new();
    volatile ProxyFrame frame = new([], []);
    internal ProxyFrame Frame => frame;

    internal static StockTorsoProxies? For(World world) => worlds.TryGetValue(world, out var proxies) ? proxies : null;

    internal static void PublishDrawn(World world, CharacterId target, Vector3 position)
    {
        if (float.IsFinite(position.LengthSquared()) && For(world) is { } state
            && (state.Frame.TryOwner(target, out _) || state.Frame.TryProxy(target, out _))) state.drawn[target] = position;
    }

    internal static void ForgetDrawn(World world, CharacterId target)
    { if (For(world) is { } state) state.drawn.TryRemove(target, out _); }

    internal static bool IsDrawn(World world, CharacterId target) => For(world)?.drawn.ContainsKey(target) == true;

    internal static void Install(ModContext context)
    {
        context.AddSystem("torso-proxies", _ => new StockTorsoProxies(), SystemAnchor.Before(StandardParts.Carry));
        if (AccessTools.TypeByName("Meitou.Game.PlayerInterface") is { } ui)
        {
            uiSession = ui.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(field => field.FieldType == typeof(WorldSession));
            if (uiSession is not null && AccessTools.Method(ui, "Say", [typeof(SimCommand)]) is { } say)
                context.Harmony.Patch(say, prefix: Hook(nameof(BeforeSay)));
            else context.Log("Brutality torso carry target placement unavailable on this host");
        }

        context.Harmony.Patch(AccessTools.Method(typeof(BodySystem), nameof(BodySystem.SlowWorld)),
            prefix: Hook(nameof(CorpseUpdatePrefix)), finalizer: Hook(nameof(CorpseUpdateFinalizer)));
        context.Harmony.Patch(AccessTools.Method(typeof(CharacterTable), nameof(CharacterTable.Remove)),
            prefix: Hook(nameof(CorpseRemovePrefix)));
        foreach (var name in new[] { nameof(CrimeSystem.LootAllowed), nameof(CrimeSystem.IsLootTheft), nameof(CrimeSystem.MarksTheft) })
            context.Harmony.Patch(AccessTools.Method(typeof(CrimeSystem), name), prefix: Hook(nameof(AliasVictim)));
        context.Harmony.Patch(AccessTools.Method(typeof(CrimeSystem), nameof(CrimeSystem.TakeCode)), prefix: Hook(nameof(AliasOwner)));
        context.Harmony.Patch(AccessTools.Method(typeof(CrimeSystem), nameof(CrimeSystem.Loot)), prefix: Hook(nameof(AliasLootSlot)));
        context.Harmony.Patch(AccessTools.Method(typeof(CrimeSystem), nameof(CrimeSystem.Pickpocket)), prefix: Hook(nameof(AliasOwnerSlot)));
        context.Harmony.Patch(AccessTools.Method(typeof(StolenMark), nameof(StolenMark.Of)), prefix: Hook(nameof(AliasMark)));
        if (typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterAssembly") is { } assembly
            && AccessTools.Method(assembly, "RefreshEquipment", [typeof(CharacterCold)]) is { } refresh)
            context.Harmony.Patch(refresh, prefix: Hook(nameof(AliasEquipment)));
        StockTorsoMarkerFilter.Install(context);
        context.Log("Brutality stock torso carry and shared loot initialized");
    }

    static HarmonyMethod Hook(string name) => new(typeof(StockTorsoProxies), name);

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        worlds.GetValue(world, _ => this);
        bool changed = false;
        var table = world.Characters;
        foreach (var owner in StockTorsoRules.CutOwners(world))
        {
            if (!table.TryResolveNext(owner, out int slot) || table.Cold(slot) is not { Medical.Dead: true, Race: { } race } source
                || halves.ContainsKey(owner) || StockTorsoSave.For(world, owner) is null) continue;

            var stats = new CharacterStats { Scale = source.Stats?.Scale ?? 1 };
            if (source.Stats is { } original)
                foreach (var stat in StatsInfo.RecordStats) stats[stat] = original[stat];
            var medical = MedicalState.Create(race, stats.Strength);
            medical.Butcher();
            medical.CollapseOnset = false;
            var cold = new CharacterCold
            {
                Name = source.Name + " — Lower body", Faction = source.Faction, Race = race,
                Stats = stats, Medical = medical, Fighter = new Fighter(null), Inventory = source.Inventory,
                DiedTick = world.Tick, NpcClass = source.NpcClass, SlaveState = source.SlaveState,
            };
            // Appearance stays null. The stock renderer and ragdoll system must not draw or animate a second full body.
            var body = table.Next[slot];
            var proxy = table.Spawn(new CharacterHot { Position = body.Position, Yaw = body.Yaw, Health = 0 }, cold, world.Tick);
            var half = new Half(owner, proxy, source, cold);
            halves.Add(owner, half);
            byProxy.Add(proxy, half);
            lootOwners.Add(cold, new(owner, source));
            changed = true;
        }
        if (changed) Publish();
        StockTorsoProxySave.AfterProxies(world);
        StockTorsoProxySave.Preview(world, drawn);

        foreach (var command in commands)
        {
            if (command is not PositionOrder placement || !table.TryResolveNext(placement.Target, out int slot)
                || table.Cold(slot) is not { Medical.Dead: true, CarriedBy.IsNone: true, InSomething: HeldState.None } cold
                || !float.IsFinite(placement.Position.LengthSquared())) continue;
            bool lower = byProxy.TryGetValue(placement.Target, out var proxy) && ReferenceEquals(proxy.Cold, cold);
            bool upper = halves.TryGetValue(placement.Target, out var owner) && ReferenceEquals(owner.Source, cold);
            if (!lower && !upper) continue;
            var position = placement.Position;
            if (lower)
            {
                float ground = world.Walkability.GroundHeight(position.X, position.Z, position.Y);
                if (float.IsFinite(ground)) position.Y = ground;
            }
            else if (position - table.Next[slot].Position is { } delta && delta.LengthSquared() > 1e-6f
                && world.Systems.OfType<RagdollSystem>().FirstOrDefault() is { } system
                && nativeRagdolls?.GetValue(system) is IDictionary native && native[placement.Target] is { } ragdoll)
            {
                // Keep the stock root aligned with the selected visible half; its next physics step would otherwise
                // return the target to the original unsplit body's position while the character walks over to it.
                shiftNative?.Invoke(system, [ragdoll, delta]);
            }
            table.Place(slot, position, table.Next[slot].Yaw);
        }
    }

    public void SlowWorld(World world)
    {
        var table = world.Characters;
        if (halves.Count == 0) return;
        bool changed = false;
        foreach (var (owner, half) in halves)
        {
            bool alive = table.TryResolveNext(half.Proxy, out int slot) && ReferenceEquals(table.Cold(slot), half.Cold);
            if (!alive || !table.TryResolveNext(owner, out _) && !Held(table, half))
            {
                if (alive) table.Remove(half.Proxy);
                halves.Remove(owner);
                byProxy.Remove(half.Proxy);
                drawn.TryRemove(owner, out _);
                drawn.TryRemove(half.Proxy, out _);
                lootOwners.Remove(half.Cold);
                changed = true;
                continue;
            }
            // Native changes to the owner may replace the inventory object. Both halves always alias the current inventory.
            half.Cold.Inventory = half.Source.Inventory;
            half.Cold.DiedTick = world.Tick;
        }
        if (changed) Publish();
    }

    void Publish() => frame = new(halves.ToDictionary(pair => pair.Key, pair => pair.Value.Proxy),
        byProxy.ToDictionary(pair => pair.Key, pair => pair.Value.Owner));

    static bool Held(CharacterTable table, Half half) =>
        table.TryResolveNext(half.Cold.CarriedBy, out int slot) && table.Cold(slot)?.Carrying == half.Proxy;

    static void BeforeSay(object __instance, SimCommand command)
    {
        var target = command switch
        {
            PickUpOrder order => order.Target,
            OpenLootOrder order => order.Target,
            LootOrder order => order.Target,
            _ => CharacterId.None,
        };
        if (target.IsNone || uiSession?.GetValue(__instance) is not WorldSession session
            || For(session.World) is not { } proxies || !(proxies.Frame.TryOwner(target, out _) || proxies.Frame.TryProxy(target, out _))
            || !proxies.drawn.TryGetValue(target, out var position)) return;
        session.World.Commands.Enqueue(new PositionOrder(target, position) { Tick = command.Tick });
    }

    static void CorpseUpdatePrefix(World world, out World? __state)
    { __state = corpseUpdate; corpseUpdate = For(world) is null ? null : world; }
    static Exception? CorpseUpdateFinalizer(Exception? __exception, World? __state)
    { corpseUpdate = __state; return __exception; }

    static bool CorpseRemovePrefix(CharacterTable __instance, CharacterId id, ref bool __result)
    {
        if (corpseUpdate is not { } world || !ReferenceEquals(world.Characters, __instance) || For(world) is not { } proxies
            || !__instance.TryResolveNext(id, out int slot) || __instance.Cold(slot)?.Burst is not null) return true;
        if (!proxies.byProxy.ContainsKey(id) && (!proxies.halves.TryGetValue(id, out var half) || !Held(__instance, half))) return true;
        __result = false;
        return false;
    }

    static void AliasVictim(ref CharacterCold victim) => victim = Canonical(victim);
    static void AliasOwner(ref CharacterCold owner) => owner = Canonical(owner);
    static void AliasEquipment(ref CharacterCold cold) => cold = Canonical(cold);
    static CharacterCold Canonical(CharacterCold cold) => lootOwners.TryGetValue(cold, out var original) ? original.Cold : cold;

    static void AliasMark(ref CharacterCold victim, ref CharacterId id)
    {
        if (lootOwners.TryGetValue(victim, out var original)) (victim, id) = (original.Cold, original.Id);
    }

    static void AliasLootSlot(World world, ref int victim) => victim = CanonicalSlot(world, victim);
    static void AliasOwnerSlot(World world, ref int owner) => owner = CanonicalSlot(world, owner);
    static int CanonicalSlot(World world, int slot) => world.Characters.Cold(slot) is { } cold
        && lootOwners.TryGetValue(cold, out var original) && world.Characters.TryResolveNext(original.Id, out int live)
        && ReferenceEquals(world.Characters.Cold(live), original.Cold) ? live : slot;
}
