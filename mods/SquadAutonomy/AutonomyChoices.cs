using System.Collections.Frozen;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Locks;

namespace Meitou.SquadAutonomy;

/// <summary>
/// The original's bed and labour finders as answers to the engine's bed search (<see cref="IBedFilter"/>) and job choice
/// (<see cref="IJobScoreFilter"/>) for the members of an autonomous squad with a plan. Pure reads of the published member table, on any thread.
/// </summary>
sealed class AutonomyChoices(Func<FrozenDictionary<CharacterId, MemberRule>?> members, WorkplaceStats stats)
{
    /// <summary>
    /// The bed system, for a bed's cost (set in the serial steps).
    /// </summary>
    public BedSystem? Beds { get; set; }

    /// <summary>
    /// The original's <c>FindOptimalBed</c>: a bed of the member's own faction first (the game's search weighs those above any other), then any
    /// bed it may use for free (cost 0: no faction, an ally's, a bandit's, a rented one), and with Allow Using Paid Beds also one it pays for
    /// (cost above 0, an inn's); never one it may not use (cost -1). Anyone else, and a bed the game takes, keeps the game's answer.
    /// </summary>
    public bool Allows(World world, in BedAsk ask)
    {
        if (ask.Allowed || members() is not { } table || !table.TryGetValue(ask.Sleeper, out var rule)) return ask.Allowed;
        if (Beds is not { } beds || world.Fixtures.Find(ask.Bed) is not { } bed) return false;
        if (world.Characters.Cold(ask.Sleeper.Slot) is not { } sleeper) return false;

        int cost = beds.Cost(world, bed, sleeper.Faction);
        return cost == 0 || cost > 0 && rule.Rest.PaidBeds;
    }

    /// <summary>
    /// The original's labour priorities (<c>getLabourPriority</c>: a bigger number wins, 0 forbids): the game's score times the priority of the
    /// workplace's stat. The original's tie-breaks (fewer operators, then the nearer, with the distance from home counted twice) are left to the
    /// game's draw among equals.
    /// </summary>
    public float Score(in JobScoreAsk ask)
    {
        if (ask.Kind != JobCandidateKind.Workplace || members() is not { } table || !table.TryGetValue(ask.Character, out var rule))
            return ask.Score;

        int priority = rule.Labour.Of(LabourPriorities.KindOf(stats.Of(ask.Record)));
        return priority <= 0 ? 0 : ask.Score * priority;
    }
}
