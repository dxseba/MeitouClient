using System.Collections.Frozen;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;

namespace Meitou.Disarm;

/// <summary>
/// Runs Disarm in the tick: a <see cref="DisarmOrder"/> waits until its actor has walked within <see cref="Reach"/> of the target, then rolls the
/// <see cref="DisarmRules.Chance"/> once. Success drops (or destroys) the target's hip and back weapons and trains Stealth; failure is the engine's
/// own "caught stealing" (the target wakes, a pending Theft, the stole-from-me memory). Also the knockout drop, Kick and AI rearm (partial files).
/// </summary>
sealed partial class DisarmSystem(DisarmSettings settings, EquipmentRefresh refresh, RngPurpose purpose, Action<SimCommand> enqueue) : ITickSystem
{
    /// <summary>World units an actor may stand from its target (the original's hard maximum).</summary>
    public const float Reach = 30;

    /// <summary>Seconds an actor has to reach its target.</summary>
    const float ApproachSeconds = 20;

    /// <summary>World units round a player character within which targets get a published chance.</summary>
    const float ChanceRadius = 80;

    /// <summary>Stealth XP of a success (the original's <c>DISARM_STEALTH_XP_GAIN</c>, an event-based gain).</summary>
    const float StealthXp = 15;

    static readonly string[] WeaponSections = ["hip", "back"];
    static readonly MethodInfo? CaughtRoll = AccessTools.Method(typeof(CrimeSystem), "Roll");

    readonly List<(CharacterId Actor, CharacterId Target, long Tick)> _approaches = [];
    volatile FrozenDictionary<(CharacterId Actor, CharacterId Target), int> _chances = FrozenDictionary<(CharacterId, CharacterId), int>.Empty;

    /// <summary>The chance of each player character on each nearby target, rebuilt in the slow phase; safe to read from the interface thread.</summary>
    public FrozenDictionary<(CharacterId Actor, CharacterId Target), int> Chances => _chances;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is DisarmOrder order) Queue(order);
            else if (command is KickOrder kick) QueueKick(kick);
        }
    }

    /// <summary>Runs every tick: the approaches every 5th, the knockout watch and rearm every 15th, the published chances every 10th.</summary>
    public void SlowWorld(World world)
    {
        if (!settings.Enabled) return;

        if (world.Tick % 5 == 0)
        {
            Approach(world);
            ApproachKicks(world);
        }

        if (world.Tick % 15 == 0)
        {
            WatchKnockouts(world);
            Rearm(world);
        }

        if (world.Tick % 10 == 0) PublishChances(world);
    }

    void Queue(DisarmOrder order)
    {
        _approaches.RemoveAll(a => a.Actor == order.Actor);
        _approaches.Add((order.Actor, order.Target, order.Tick));
    }

    void Approach(World world)
    {
        foreach (var approach in _approaches.ToArray())
        {
            if (Step(world, approach)) _approaches.Remove(approach);
        }
    }

    /// <summary>One approach's check: true once it is done (attempted, impossible or out of time).</summary>
    bool Step(World world, (CharacterId Actor, CharacterId Target, long Tick) approach)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(approach.Actor, out int actor) || !table.TryResolveNext(approach.Target, out int target)) return true;
        if (DisarmRules.Chance(world, actor, target, settings) is not int chance)
        {
            world.Post($"{table.Cold(target)?.Name} can no longer be disarmed.");
            return true;
        }

        if (Distance(table.Next[actor].Position, table.Next[target].Position) <= Reach)
        {
            Attempt(world, actor, target, chance);
            return true;
        }

        if (world.Tick - approach.Tick <= ApproachSeconds / world.TickSeconds) return false;

        world.Post($"{table.Cold(actor)?.Name} could not reach {table.Cold(target)?.Name}.");
        return true;
    }

    void Attempt(World world, int actor, int target, int chance)
    {
        var table = world.Characters;
        ulong key = Rng.Hash(world.Seed, Rng.Key(table.IdOf(actor)) ^ Rng.Key(table.IdOf(target)), purpose, (ulong)world.Tick);
        bool success = Rng.Float(key) * 100 < chance;

        if (success) Succeed(world, actor, target, chance);
        else Caught(world, actor, target);
    }

    void Succeed(World world, int actor, int target, int chance)
    {
        var table = world.Characters;
        var cold = table.Cold(target)!;
        var weapons = cold.Inventory?.Items.Where(i => !i.LockedOn && WeaponSections.Contains(i.Section)).ToList() ?? [];
        if (weapons.Count == 0)
        {
            world.Post($"{cold.Name} has no weapon to take.");
            return;
        }

        foreach (var item in weapons) Remove(world, target, item);
        Rearmed(cold);
        if (chance < 100) TrainStealth(world, table.Cold(actor)!);
        if (settings.Rearm) Track(table.IdOf(target), world.Tick);
        world.Post($"{cold.Name} has been disarmed.");
    }

    /// <summary>The engine's caught-stealing reaction on the target, through <c>CrimeSystem</c>'s steal roll with a chance of 0.</summary>
    static void Caught(World world, int actor, int target)
    {
        var table = world.Characters;
        var targetId = table.IdOf(target);
        if (world.Systems.OfType<CrimeSystem>().FirstOrDefault() is { } crime && CaughtRoll is not null)
            CaughtRoll.Invoke(crime, [world, actor, target, 0f, table.Cold(target)!.Faction, targetId, Rng.Key(targetId), null]);
        else
            world.Post($"{table.Cold(actor)?.Name} was caught trying to disarm {table.Cold(target)?.Name}.");
    }

    static void TrainStealth(World world, CharacterCold actor)
    {
        if (actor.Stats is { } stats && actor.Race is { } race)
            world.Systems.OfType<CrimeSystem>().FirstOrDefault()?.Xp?.Continuous(stats, race, Meitou.Data.Gameplay.Bodies.StatsEnumerated.Stealth, 1, StealthXp);
    }

    /// <summary>Takes <paramref name="item"/> out of the character's inventory and drops it beside it, or destroys it.</summary>
    void Remove(World world, int slot, ItemInstance item)
    {
        var table = world.Characters;
        var cold = table.Cold(slot)!;
        cold.Inventory!.Items.Remove(item);
        if (settings.Destroy) return;

        item.Section = "main";
        item.Mark = settings.StolenFlag ? StolenMark.OfFaction(cold.Faction) : StolenMark.None;

        var at = table.Next[slot].Position;
        ulong salt = (ulong)cold.Inventory.Items.Count + 1;
        float angle = Rng.Float(Rng.Hash(world.Seed, Rng.Key(table.IdOf(slot)) ^ salt, purpose, (ulong)world.Tick)) * MathF.Tau;
        var spot = at + new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle)) * 8;
        spot.Y = world.Walkability.GroundHeight(spot.X, spot.Z, at.Y);
        world.GroundItems.Drop(item, spot, angle);
    }

    /// <summary>Clears the drawn weapon and rebuilds the character's fighter and look from what it still carries.</summary>
    void Rearmed(CharacterCold cold)
    {
        cold.DrawnWeapon = Meitou.Data.Gameplay.WeaponKinds.None;
        refresh.Refresh(cold);
    }

    void PublishChances(World world)
    {
        var table = world.Characters;
        var map = new Dictionary<(CharacterId, CharacterId), int>();
        for (int actor = 0; actor < table.HighWater; actor++)
        {
            if (table.Next[actor].Alive && table.Cold(actor) is { IsPlayer: true, Medical.Incapacitated: false }) AddChances(world, actor, map);
        }

        _chances = map.ToFrozenDictionary();
    }

    void AddChances(World world, int actor, Dictionary<(CharacterId, CharacterId), int> map)
    {
        var table = world.Characters;
        var at = table.Next[actor].Position;
        for (int target = 0; target < table.HighWater; target++)
        {
            if (target == actor || !table.Next[target].Alive || Distance(at, table.Next[target].Position) > ChanceRadius) continue;
            if (DisarmRules.Chance(world, actor, target, settings) is int chance) map[(table.IdOf(actor), table.IdOf(target))] = chance;
        }
    }

    static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
}

/// <summary>The engine's private equipment refresh (fighter and look from the inventory), reached by reflection as the captive strip uses it.</summary>
sealed class EquipmentRefresh(object assembly, MethodInfo refresh)
{
    public static EquipmentRefresh Create(PopulationData data)
    {
        var type = AccessTools.TypeByName("Meitou.Simulation.Bodies.CharacterAssembly") ?? throw new TypeLoadException("Meitou.Simulation.Bodies.CharacterAssembly");
        var method = AccessTools.Method(type, "RefreshEquipment") ?? throw new MissingMethodException(type.FullName, "RefreshEquipment");
        return new(Activator.CreateInstance(type, data)!, method);
    }

    public void Refresh(CharacterCold cold) => refresh.Invoke(assembly, [cold]);
}
