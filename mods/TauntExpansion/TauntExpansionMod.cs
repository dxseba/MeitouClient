using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.TauntExpansion;

/// <summary>
/// Scales the vanilla Taunt term of the fight target score, and scales it again while the chooser already has a target. The vanilla target
/// choice, AI and pathing are kept: only <see cref="TargetScore.Score"/> of a taunting candidate changes.
/// </summary>
public sealed class TauntExpansionMod : IMod
{
    /// <summary>
    /// The vanilla taunt bonus in <see cref="TargetScore.Score"/> (the original's ini: "Kenshi's confirmed vanilla Taunt bonus is +2.0").
    /// </summary>
    const float VanillaBonus = 2;

    /// <summary>
    /// Copied from the settings in a serial step; read by the parallel phases that score targets.
    /// </summary>
    static bool s_enabled = true;

    /// <summary>
    /// The Taunt and re-targeting multipliers, copied with <see cref="s_enabled"/>.
    /// </summary>
    static float s_multiplier = 5, s_retarget = 2;

    public void Initialize(ModContext context)
    {
        var settings = new TauntSettings();
        context.AddSettings(settings);
        context.AddSystem("settings", _ => new SettingsCopy(settings), SystemAnchor.Before(StandardParts.Combat));

        PatchScore(context.Harmony);
        context.Log("Taunt Expansion initialized");
    }

    /// <summary>
    /// <see cref="TargetScore.Score"/> is public and has no filter hook; the documented route is a Harmony postfix on it.
    /// </summary>
    static void PatchScore(Harmony harmony)
    {
        var score = AccessTools.Method(typeof(TargetScore), nameof(TargetScore.Score))
            ?? throw new MissingMethodException(nameof(TargetScore), nameof(TargetScore.Score));
        harmony.Patch(score, postfix: new HarmonyMethod(typeof(TauntExpansionMod), nameof(Scored)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result by name
    static void Scored(in TargetCandidate c, ref float? __result)
    #pragma warning restore IDE1006
    {
        if (!s_enabled || !c.Taunt || __result is not { } s) return;

        // Effective bonus: 2 x Multiplier, or 2 x Multiplier x RetargetMultiplier while the chooser already has a target (the original ini).
        // The original raises it the second time only in AI::chooseBestAttackTarget calls given a valid old target: the engine's
        // ChooserHasTarget (a fighter's own choice while it fights someone).
        float bonus = VanillaBonus * s_multiplier;
        if (c.ChooserHasTarget) bonus *= s_retarget;

        __result = s + bonus - VanillaBonus;
    }

    sealed class SettingsCopy(TauntSettings settings) : ITickSystem
    {
        public void Inputs(World world, IReadOnlyList<SimCommand> commands)
        {
            s_enabled = settings.Enabled;
            s_multiplier = settings.Multiplier;
            s_retarget = settings.RetargetMultiplier;
        }
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
