using System.Collections.Frozen;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>
/// What the window needs from the world that the snapshot lacks: the player characters' skills and the job rows whose target is gone.
/// </summary>
public sealed record JobBoard(FrozenDictionary<CharacterId, float[]> Skills, FrozenSet<(CharacterId, PermaJob)> Invalid, long Tick)
{
    public static JobBoard Empty { get; } = new(FrozenDictionary<CharacterId, float[]>.Empty, FrozenSet<(CharacterId, PermaJob)>.Empty, -1);

    /// <summary>A character's value of <paramref name="stat"/>, or null when it is not known yet.</summary>
    public float? Skill(CharacterId who, StatsEnumerated stat) =>
        stat != StatsEnumerated.None && Skills.TryGetValue(who, out var values) ? values[(int)stat] : null;
}

/// <summary>
/// Publishes a <see cref="JobBoard"/> about once a second of game time (a serial step; nothing while paused, when skills and sites cannot change).
/// </summary>
sealed class JobBoardSystem : ITickSystem
{
    volatile JobBoard _board = JobBoard.Empty;

    public JobBoard Board => _board;

    public void SlowWorld(World world)
    {
        if (world.Tick % 30 != 0 && _board.Tick >= 0) return;

        var skills = new Dictionary<CharacterId, float[]>();
        var invalid = new HashSet<(CharacterId, PermaJob)>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold) continue;

            var id = new CharacterId(slot, table.Next[slot].Generation);
            skills[id] = SkillsOf(cold);
            foreach (var job in cold.Jobs)
            {
                if (TargetGone(world, job)) invalid.Add((id, job));
            }
        }

        _board = new(skills.ToFrozenDictionary(), invalid.ToFrozenSet(), world.Tick);
    }

    /// <summary>Every stat's value (with modifiers), by <see cref="StatsEnumerated"/>.</summary>
    static float[] SkillsOf(CharacterCold cold)
    {
        var values = new float[StatsInfo.Count];
        for (int stat = 1; stat < values.Length; stat++)
            values[stat] = cold.Stats?[(StatsEnumerated)stat] ?? 0;
        return values;
    }

    /// <summary>
    /// A fixed-target row whose target no longer exists: a player building, fixture or seat site found nowhere, or a followed character gone from
    /// the world. Rocks, town work sites and unmatched handles are never judged (the zone may not be placed yet).
    /// </summary>
    static bool TargetGone(World world, PermaJob job)
    {
        if (JobRules.IsProtected(job.Type)) return false;

        if (job.Site is { } site)
        {
            if (ProductionSystem.IsNatural(site) || ProductionSystem.IsTownWork(site)) return false;
            return world.Buildings.Find(site) is null && world.Fixtures.Find(site) is null && world.Fixtures.FindSeat(site) is null;
        }

        return PermaJobs.Follows(job.Type) && !job.Character.IsNone && !world.Characters.IsAlive(job.Character);
    }
}
