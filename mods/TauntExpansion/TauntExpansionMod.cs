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
        context.AddSystem("taunt", _ => new TauntSystem(settings), SystemAnchor.Before(StandardParts.Combat));
        context.Log("Taunt Expansion initialized");
    }
}

/// <summary>
/// The target score filter: copies the settings in its serial step and answers every taunting candidate's score with the scaled bonus. Asked on
/// any thread, so it reads only the copies.
/// </summary>
sealed class TauntSystem(TauntSettings settings) : ITargetScoreFilter
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
    }

    /// <summary>
    /// Effective bonus: 2 x Multiplier, or 2 x Multiplier x RetargetMultiplier while the chooser already has a target (the original ini). The
    /// original raises it the second time only in AI::chooseBestAttackTarget calls given a valid old target: the engine's ChooserHasTarget.
    /// </summary>
    public float Score(World world, in TargetScoreAsk ask)
    {
        if (!_enabled || !ask.Candidate.Taunt) return ask.Score;

        float bonus = VanillaBonus * _multiplier;
        if (ask.Candidate.ChooserHasTarget) bonus *= _retarget;

        return ask.Score + bonus - VanillaBonus;
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
}
