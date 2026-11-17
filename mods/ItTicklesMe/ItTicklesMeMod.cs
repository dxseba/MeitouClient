using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.ItTicklesMe;

/// <summary>
/// Drops the light hit reaction of a melee blow whose stagger damage is below a share of the victim's stumble threshold, so weak hits no longer stun-lock
/// a tough fighter. Heavy staggers and hits at or above the share are unchanged.
/// </summary>
public sealed class ItTicklesMeMod : IMod
{
    internal static readonly TicklesSettings Settings = new();
    [ThreadStatic] static bool s_skipNext;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var apply = AccessTools.Method(typeof(CombatSystem), "BlowReaction") ?? throw new MissingMethodException(nameof(CombatSystem), "BlowReaction");
        var stagger = AccessTools.Method(typeof(CombatSystem), "Stagger") ?? throw new MissingMethodException(nameof(CombatSystem), "Stagger");
        context.Harmony.Patch(apply, transpiler: new HarmonyMethod(typeof(ItTicklesMeMod), nameof(Transpile)));
        context.Harmony.Patch(stagger, prefix: new HarmonyMethod(typeof(ItTicklesMeMod), nameof(BeforeStagger)));
        context.Log("It. Tickles. Me initialized");
    }

    /// <summary>Replaces <c>damage &gt; StumbleThreshold(...)</c> in the melee hit with <see cref="Heavy"/>, which also notes a hit too weak to react to.</summary>
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var threshold = AccessTools.Method(typeof(DamageFormulas), nameof(DamageFormulas.StumbleThreshold));
        var list = instructions.ToList();
        int replaced = 0;
        for (int i = 0; i + 1 < list.Count; i++)
        {
            if (!list[i].Calls(threshold) || list[i + 1].opcode != OpCodes.Cgt) continue;
            list[i + 1] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ItTicklesMeMod), nameof(Heavy))).MoveLabelsFrom(list[i + 1]);
            replaced++;
        }
        if (replaced != 1) throw new InvalidOperationException($"expected one stumble comparison in CombatSystem.BlowReaction, found {replaced}");
        return list;
    }

    static bool Heavy(float damage, float threshold)
    {
        s_skipNext = Settings.Enabled && damage < threshold * Settings.Ratio;
        return damage > threshold;
    }

    static bool BeforeStagger(bool heavy)
    {
        bool skip = s_skipNext && !heavy;
        s_skipNext = false;
        return !skip;
    }
}

public sealed class TicklesSettings
{
    [Setting("Filter weak staggers", Hint = "Weak melee hits no longer interrupt with a light stumble.")]
    public bool Enabled { get; set; } = true;

    [Setting("Stagger threshold share", Hint = "A light stagger needs at least this share of the victim's stumble threshold.", Min = 0, Max = 1, Step = 0.01)]
    public float Ratio { get; set; } = 0.33f;
}
