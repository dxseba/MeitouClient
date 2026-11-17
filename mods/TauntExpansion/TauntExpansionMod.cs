using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.TauntExpansion;

/// <summary>
/// Strengthens the vanilla Taunt term of the fight target score and makes enemies already fighting someone else readier to switch to a taunter.
/// The vanilla target choice, AI and pathing are kept: only <see cref="TargetScore.Score"/> of a taunting candidate changes.
/// </summary>
public sealed class TauntExpansionMod : IMod
{
    /// <summary>The vanilla taunt bonus in <see cref="TargetScore.Score"/>.</summary>
    const float VanillaBonus = 2;

    // Copied from the settings in a serial step; read by the parallel phases that score targets.
    static float s_bonusMultiplier = 1.5f, s_retarget = 0.5f;

    public void Initialize(ModContext context)
    {
        var settings = new TauntSettings();
        context.AddSettings(settings);
        context.AddSystem("settings", _ => new SettingsCopy(settings), SystemAnchor.Before(StandardParts.Combat));

        var score = AccessTools.Method(typeof(TargetScore), nameof(TargetScore.Score)) ?? throw new MissingMethodException(nameof(TargetScore), "Score");
        context.Harmony.Patch(score, postfix: new HarmonyMethod(typeof(TauntExpansionMod), nameof(Scored)));
        context.Log("Taunt Expansion initialized");
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Scored(in TargetCandidate c, bool prone, ref float? __result)
    #pragma warning restore IDE1006
    {
        if (!c.Taunt || __result is not { } s) return;

        s += VanillaBonus * (s_bonusMultiplier - 1);
        // A chooser on another target keeps it by the stubbornness term; a taunter not yet its target gets part of that term back.
        if (!c.Current) s += TargetScore.Stubbornness * (prone ? 4 : 1) * s_retarget;
        __result = s;
    }

    sealed class SettingsCopy(TauntSettings settings) : ITickSystem
    {
        public void Inputs(World world, IReadOnlyList<SimCommand> commands)
        {
            s_bonusMultiplier = settings.Multiplier;
            s_retarget = settings.Retarget;
        }
    }
}

public sealed class TauntSettings
{
    [Setting("Taunt strength", Hint = "Times the vanilla taunt bonus in enemies' target choice (1 = vanilla).", Min = 1, Max = 5, Step = 0.1)]
    public float Multiplier { get; set; } = 1.5f;

    [Setting("Re-targeting", Hint = "Share of an enemy's stubbornness for its current target that a taunter offsets (0 = vanilla).", Min = 0, Max = 1, Step = 0.05)]
    public float Retarget { get; set; } = 0.5f;
}
