using System.Collections.Frozen;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Meitou.SleepFix;

/// <summary>
/// What the desire filter knows of one living player character, as of the system's last pass: whether the original's rescaling applies to it
/// (not carried, not fighting with <c>skipCombat</c>), whether it is healed, whether it has an unbandaged cut (<c>waitForTreatment</c>), whether a
/// free bed of its own faction is about (<c>ownBedsOnly</c>) and whether it lies in a bed.
/// </summary>
readonly record struct BedState(bool Eligible, bool Healed, bool Unbandaged, bool OwnBed, bool InBed);

/// <summary>
/// The settings the filter reads, taken in the serial pass so every thread of a tick sees the same values.
/// </summary>
readonly record struct BedRules(float BedMult, float ForceBed, float GetUpMult, bool StayInBed, bool OwnBedsOnly);

/// <summary>
/// The published table: the rules and each watched character's state.
/// </summary>
sealed record BedTable(BedRules Rules, FrozenDictionary<CharacterId, BedState> States);

/// <summary>
/// The original's score hooks (SleepFix.dll, decompiled) as a chooser desire filter (<c>IGoalDesireFilter</c>):
/// <list type="bullet">
/// <item><c>AI::scoreGoToBed</c>, the bed goals (<c>GoHomeAndGoToBed</c>, <c>GoHomeAndGoToBedSecure</c>) of a wounded, eligible player character:
/// the desire times <c>bedMult</c>; under <c>forceBed</c> it is raised to <c>forceBed</c> when no cut waits for a bandage (else left as the game
/// computed it) and the bed is one of its faction's or <c>ownBedsOnly</c> is off (else left).</item>
/// <item><c>AI::scoreGetOutOfBedOnceFullyHealed</c>, the plan step <c>GetOutOfBedOnceHealed</c>: 0 for an eligible character in bed that is not
/// healed with <c>stayInBed</c>, else a positive desire times <c>getUpMult</c>.</item>
/// </list>
/// The engine asks a bed goal only when its own bed search found a bed (docs/modding.md "Desire filters"), so the original's "no bed found"
/// branch (the multiplied desire) never comes up here. A pure read of the published table, on any thread.
/// </summary>
sealed class BedDesire(Func<BedTable?> table) : IGoalDesireFilter
{
    public float Desire(World world, in GoalDesireAsk ask)
    {
        if (table() is not { } published || !published.States.TryGetValue(ask.Character, out var state)) return ask.Desire;

        var rules = published.Rules;
        return ask switch
        {
            { Kind: GoalDesireKind.Goal, Type: TaskType.GoHomeAndGoToBed or TaskType.GoHomeAndGoToBedSecure } => GoToBed(rules, state, ask.Desire),
            { Kind: GoalDesireKind.PlanStep, Type: TaskType.GetOutOfBedOnceHealed } => GetUp(rules, state, ask.Desire),
            _ => ask.Desire,
        };
    }

    /// <summary>
    /// <c>scoreGoToBed</c>'s rescaling of <paramref name="desire"/>.
    /// </summary>
    static float GoToBed(BedRules rules, BedState state, float desire)
    {
        if (state.Healed || !state.Eligible) return desire;

        float scaled = rules.BedMult * desire;
        if (rules.ForceBed <= 0 || scaled >= rules.ForceBed) return scaled;
        if (state.Unbandaged) return desire;

        return !rules.OwnBedsOnly || state.OwnBed ? rules.ForceBed : desire;
    }

    /// <summary>
    /// <c>scoreGetOutOfBedOnceFullyHealed</c>'s rescaling of <paramref name="desire"/>.
    /// </summary>
    static float GetUp(BedRules rules, BedState state, float desire)
    {
        if (!state.InBed && desire <= 0) return desire;
        if (state.InBed && rules.StayInBed && !state.Healed && state.Eligible && desire > 0) return 0;
        if (desire <= 0 || !state.Eligible) return desire;

        return rules.GetUpMult * desire;
    }
}
