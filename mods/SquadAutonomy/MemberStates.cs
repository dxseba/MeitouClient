using System.Numerics;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;

namespace Meitou.SquadAutonomy;

/// <summary>
/// Reads what the original's score hooks read of a member, in the serial slow step (<see cref="MemberState"/>), and keeps the game hour each
/// member was last seen lying in a bed (the original reads the game's <c>lastSlept</c>; Meitou keeps none, so this is the port's own record,
/// saved with the plans by the members' lasting ids; a member never seen asleep counts as long awake).
/// </summary>
sealed class MemberStates
{
    /// <summary>The reach of a melee attacker that counts as attacking right now (the game's immediate threat, 40 units).</summary>
    const float MeleeReach = 40;

    /// <summary>Hours since sleeping for a member never seen asleep: long enough for the original's top score.</summary>
    const double LongAwake = 24;

    readonly Dictionary<LastingId, double> _sleptAt = [];
    readonly HashSet<CharacterId> _attacked = [];

    /// <summary>Notes once per schedule who is under melee attack (the stand-in, <see cref="Threats"/>).</summary>
    public void Begin(World world) => Threats(world);

    /// <summary>The member's state now; null when it does not resolve to a living character.</summary>
    public MemberState? Of(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || !table.Next[slot].Alive) return null;
        if (table.Cold(slot) is not { Medical: { } medical } cold) return null;

        bool asleep = cold.InSomething == HeldState.Bed;
        if (asleep) _sleptAt[world.LastingIds.GetOrGive(id)] = world.GameHours;

        var race = cold.Race;
        var (aid, kit) = race is null ? (0f, 0f) : Needs(world, cold, medical, race);
        double since = !cold.LastingId.IsNone && _sleptAt.TryGetValue(cold.LastingId, out double at) ? world.GameHours - at : LongAwake;
        float rested = race is null ? 1 : medical.RestedLevel(race);
        return new(cold.OrderQueue.Count > 0, world.Sight.Inside(slot), asleep, _attacked.Contains(id), cold.Ai?.Goal?.Task.Type,
            race is { IsRobot: false }, rested, aid, kit, since);
    }

    /// <summary>The current goal of <paramref name="id"/>, null without one.</summary>
    public static TaskType? GoalOf(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) ? world.Characters.Cold(slot)?.Ai?.Goal?.Task.Type : null;

    /// <summary>Forgets the members no longer kept by the game.</summary>
    public void Forget(World world)
    {
        if (world.Tick % (30 * 600) != 0) return;

        var kept = world.LastingIds.KeptIds().ToHashSet();
        foreach (var id in _sleptAt.Keys.Where(id => !kept.Contains(id)).ToList()) _sleptAt.Remove(id);
    }

    /// <summary>Writes the sleep record: count, then lasting id and game hour.</summary>
    public void Write(BinaryWriter writer)
    {
        writer.Write(_sleptAt.Count);
        foreach (var (id, hours) in _sleptAt.OrderBy(e => e.Key.Value))
        {
            writer.Write(id.Value);
            writer.Write(hours);
        }
    }

    /// <summary>Reads what <see cref="Write"/> wrote.</summary>
    public void Read(BinaryReader reader)
    {
        _sleptAt.Clear();
        for (int n = reader.ReadInt32(); n > 0; n--) _sleptAt[new LastingId(reader.ReadUInt64())] = reader.ReadDouble();
    }

    /// <summary>The organic first aid need and the need for the race's own kit (<c>scoreFirstAidNeed(false)</c> and <c>(race robot)</c>).</summary>
    static (float Organic, float Kit) Needs(World world, CharacterCold cold, MedicalState medical, RaceData race)
    {
        float strength = cold.Stats?.Strength ?? 50;
        float capacity = MathF.Max(MedicalState.BloodCapacity(race, strength, medical.HasStatsObject, medical.ScaleK, medical.ScaleZ), 1);
        float extraLoss = world.Systems.OfType<BodySystem>().FirstOrDefault()?.Constants.ExtraBloodLossFromBodyparts ?? 1;
        var (organic, robot) = medical.FirstAidNeeds(race, capacity, extraLoss);
        return (organic, race.IsRobot ? robot : organic);
    }

    /// <summary>
    /// The stand-in for <c>isLiterallyUnderMeleeAttackRightNowForSure</c>: a living character whose running attack targets the member from within
    /// 40 units. The game's own flag (its immediate threat) is not open to mods.
    /// </summary>
    void Threats(World world)
    {
        _attacked.Clear();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot)?.Running is not { Task: CharacterTask.Attack } running || running.Target.IsNone) continue;
            if (!table.TryResolveNext(running.Target, out int target)) continue;

            float d2 = Vector3.DistanceSquared(table.Next[slot].Position, table.Next[target].Position);
            if (d2 < MeleeReach * MeleeReach) _attacked.Add(running.Target);
        }
    }
}
