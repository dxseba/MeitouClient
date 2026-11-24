using System.Collections.Frozen;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Movement;

namespace Meitou.WorkForCats;

/// <summary>
/// The workplace's hunger while working: the original's employee runs the native operate action, which sets the functionality's <c>hunger
/// rate</c> as the situation factor (docs/game/character-stats.md, <c>currentOperatingMachineHungerRate</c>); Meitou's employee stands on the job's
/// idle task, which the game counts as idle (0.8) or a task (1). The hunger filter swaps that factor for the workplace's rate while the employee
/// stands still at the work clip out of combat.
/// </summary>
internal sealed partial class WorkSystem : INeedRateFilter
{
    /// <summary>
    /// The hunger rate of each employee's workplace while it works, set in the serial sweep, read by the body update.
    /// </summary>
    volatile FrozenDictionary<CharacterId, float> _workHunger = FrozenDictionary<CharacterId, float>.Empty;

    public float Rate(World world, in NeedRateAsk ask)
    {
        if (ask.Kind != NeedKind.Hunger || !_workHunger.TryGetValue(ask.Character, out float rate)) return ask.Rate;

        var table = world.Characters;
        if (!table.TryResolveNext(ask.Character, out int slot)) return ask.Rate;
        if (table.Cold(slot) is not { ActionClip: not null, InCombat: false } cold) return ask.Rate;
        if (cold.Medical is { Incapacitated: true } || table.Next[slot].Velocity.LengthSquared() > 1) return ask.Rate;
        if (table.Next[slot].Task != (byte)CharacterTask.Idle) return ask.Rate;

        float now = cold.AmIdle ? MedicalState.IdleHungerRate : 1;
        return ask.Rate / now * rate;
    }

    /// <summary>
    /// Publishes the workplace rates of the employees found working in a sweep.
    /// </summary>
    void PublishHunger(Dictionary<CharacterId, float> working) => _workHunger = working.ToFrozenDictionary();
}
