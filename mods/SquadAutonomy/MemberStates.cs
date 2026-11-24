using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;

namespace Meitou.SquadAutonomy;

/// <summary>
/// Reads what the original's score hooks read of a member, in the serial slow step (<see cref="MemberState"/>). The last sleep is the game's
/// sleep clock (<c>CharacterCold.BedUsedHours</c>, stamped by the bed task: the original's <c>lastSlept</c>); the engine does not save it, so
/// the port keeps the latest it saw per lasting id, saved with the plans, for after a load. A member never seen asleep counts as long awake.
/// </summary>
sealed class MemberStates
{
    /// <summary>Hours since sleeping for a member never seen asleep: long enough for the original's top score.</summary>
    const double LongAwake = 24;

    readonly Dictionary<LastingId, double> _sleptAt = [];

    /// <summary>The member's state now; null when it does not resolve to a living character.</summary>
    public MemberState? Of(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || !table.Next[slot].Alive) return null;
        if (table.Cold(slot) is not { Medical: { } medical } cold) return null;

        bool asleep = cold.InSomething == HeldState.Bed;
        var race = cold.Race;
        var (aid, kit) = race is null ? (0f, 0f) : Needs(world, cold, medical, race);
        float rested = race is null ? 1 : medical.RestedLevel(race);
        bool attacked = CharacterFacts.UnderMeleeAttack(world, slot);
        return new(cold.OrderQueue.Count > 0, world.Sight.Inside(slot), asleep, attacked, cold.Ai?.Goal?.Task.Type, race is { IsRobot: false },
            rested, aid, kit, HoursSinceSlept(world, id, cold));
    }

    /// <summary>
    /// Game hours since the member last slept: the sleep clock, else (after a load, before it sleeps again) the hour kept from before.
    /// </summary>
    double HoursSinceSlept(World world, CharacterId id, CharacterCold cold)
    {
        if (cold.BedUsedHours > 0)
        {
            var lasting = world.LastingIds.GetOrGive(id);
            _sleptAt[lasting] = Math.Max(_sleptAt.GetValueOrDefault(lasting), cold.BedUsedHours);
        }

        return !cold.LastingId.IsNone && _sleptAt.TryGetValue(cold.LastingId, out double at) ? world.GameHours - at : LongAwake;
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
}
