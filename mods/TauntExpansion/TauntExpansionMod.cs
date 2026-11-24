using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.TauntExpansion;

/// <summary>
/// Scales the vanilla Taunt term of the fight target score, and scales it again while the chooser already has a target. The vanilla target
/// choice, AI and pathing are kept: only the score of a taunting candidate changes, through the engine's target score filter.
/// </summary>
public sealed class TauntExpansionMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new TauntSettings();
        context.AddSettings(settings);
        var log = new TauntLog(settings, context.Log);
        context.AddSystem("taunt", _ => new TauntSystem(settings, log), SystemAnchor.Before(StandardParts.Combat));
        context.Log("Taunt Expansion initialized");
    }
}

/// <summary>
/// The target score filter: copies the settings in its serial step and answers every taunting candidate's score with the scaled bonus. Asked on
/// any thread, so it reads only the copies. Its serial step also writes the diagnostics of the choices scored since the last (<see cref="TauntLog"/>).
/// </summary>
sealed class TauntSystem(TauntSettings settings, TauntLog log) : ITargetScoreFilter
{
    /// <summary>
    /// The vanilla taunt bonus in the game's target score (the original's ini: "Kenshi's confirmed vanilla Taunt bonus is +2.0").
    /// </summary>
    const float VanillaBonus = 2;

    volatile bool _enabled = true;

    float _multiplier = 5, _retarget = 2;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _multiplier = settings.Multiplier;
        _retarget = settings.RetargetMultiplier;
        _enabled = settings.Enabled;
        log.Step(world);
    }

    /// <summary>
    /// Effective bonus: 2 x Multiplier, or 2 x Multiplier x RetargetMultiplier while the chooser already has a target (the original ini). The
    /// original raises it the second time only in AI::chooseBestAttackTarget calls given a valid old target: the engine's ChooserHasTarget.
    /// </summary>
    public float Score(World world, in TargetScoreAsk ask)
    {
        if (!_enabled) return ask.Score;

        if (!ask.Candidate.Taunt)
        {
            log.Record(ask, ask.Score);
            return ask.Score;
        }

        float bonus = VanillaBonus * _multiplier;
        if (ask.Candidate.ChooserHasTarget) bonus *= _retarget;

        float score = ask.Score + bonus - VanillaBonus;
        log.Record(ask, score);
        return score;
    }
}

/// <summary>
/// The original's <c>TauntExpansion.ini</c> keys, defaults and ranges.
/// </summary>
public sealed class TauntSettings
{
    /// <summary>
    /// <c>[General] Enabled</c>.
    /// </summary>
    [Setting("Enabled", Hint = "Off leaves the vanilla Taunt bonus.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>[Taunt] Multiplier</c>: effective bonus = +2 x Multiplier.
    /// </summary>
    [Setting("Taunt multiplier", Hint = "Effective Taunt bonus = +2 x this (1 = vanilla +2, 5 = +10).", Min = 0, Max = 100, Step = 0.5)]
    public float Multiplier { get; set; } = 5;

    /// <summary>
    /// <c>[Taunt] RetargetMultiplier</c>: when the enemy already has a target the bonus is +2 x Multiplier x this.
    /// </summary>
    [Setting("Re-target multiplier", Hint = "When the enemy already has a target the bonus is +2 x Taunt multiplier x this (1 = no extra).",
        Min = 0, Max = 10, Step = 0.1)]
    public float RetargetMultiplier { get; set; } = 2;

    /// <summary>
    /// The port's switch for the original's <c>[Logging]</c> lines (the original writes them to RE_Kenshi's log whenever it is enabled); off
    /// for normal play, as the original's ini advises.
    /// </summary>
    [Setting("Log target choices", Hint = "Writes each fighter's target choice to the mod log. Leave off for normal play.", SubGroup = "Logging")]
    public bool LogChoices { get; set; }

    /// <summary>
    /// <c>[Logging] LogCandidates</c>: the line lists the candidates.
    /// </summary>
    [Setting("Log candidates", Hint = "Lists every candidate with its Taunt and Selected flags.", SubGroup = "Logging",
        VisibleWhen = nameof(LogChoices))]
    public bool LogCandidates { get; set; }

    /// <summary>
    /// <c>[Logging] LogOnlyWhenTauntPresent</c>: only choices with a taunting candidate.
    /// </summary>
    [Setting("Only when a taunt is present", Hint = "Logs only choices where a candidate taunts.", SubGroup = "Logging",
        VisibleWhen = nameof(LogChoices))]
    public bool LogOnlyWhenTauntPresent { get; set; } = true;

    /// <summary>
    /// <c>[Logging] LogOnlyChangedSelection</c>: only when the chooser's selection differs from its last logged one.
    /// </summary>
    [Setting("Only changed selections", Hint = "Logs a fighter again only when it selects someone else.", SubGroup = "Logging",
        VisibleWhen = nameof(LogChoices))]
    public bool LogOnlyChangedSelection { get; set; } = true;

    /// <summary>
    /// <c>[Logging] MinimumLogIntervalMs</c> (0 to 60000): the least time between two lines of one chooser, in game time.
    /// </summary>
    [Setting("Minimum interval (ms)", Hint = "Least game time between two lines of one fighter (0 = no limit).", SubGroup = "Logging",
        VisibleWhen = nameof(LogChoices), Min = 0, Max = 60000, Step = 100)]
    public int MinimumLogIntervalMs { get; set; } = 500;

    /// <summary>
    /// <c>[Logging] MaximumCandidatesPerEntry</c> (1 to 512): the most candidates one line lists.
    /// </summary>
    [Setting("Maximum candidates per line", Hint = "The most candidates one line lists; the rest are counted.", SubGroup = "Logging",
        VisibleWhen = nameof(LogChoices), Min = 1, Max = 512)]
    public int MaximumCandidatesPerEntry { get; set; } = 32;
}
