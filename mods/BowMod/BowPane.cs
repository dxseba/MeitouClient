using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.BowMod;

/// <summary>
/// The Bows pane's live numbers and the HUD's skill label (README "Facts"). The original fills its pane's Accuracy and Drawing / reload speed rows from
/// the game's own crossbow functions run with the Bows level; here the game's stats-window readouts of Crossbows
/// (<c>Meitou.Simulation.Bodies.HudTips.AddSkillReadouts</c>) are computed once more with the stored Crossbows level set to the effective Bows level,
/// and published under the Bows skill's stat (postfix on <c>Meitou.Simulation.Bodies.HudTips.StatsWindow</c>, run by the worker publishing that
/// character, as the HUD line's swap). The stats window shows a code mod's skill without readouts, so a prefix on
/// <c>Meitou.Game.Ui.StatsWindow.AddModSkillDescription</c> notes the lead and a prefix on <c>Meitou.Game.Ui.StatsWindow.AddDescriptionPanels</c>
/// hands those rows in. The HUD's Current Skill line of a bow holder is labelled Bows (postfix on <c>Meitou.Simulation.World.SkillLine</c>). Each
/// failure logs once and leaves the vanilla pane or line.
/// </summary>
static class BowPane
{
    delegate void SkillReadouts(Dictionary<StatsEnumerated, IReadOnlyList<StatReadout>> result, MedicalState med, CharacterStats stats,
        HudCombat? hc, GameConstants? constants, StatFactors factors, GameDatabase? db);

    static SkillReadouts? s_readouts;
    static ModContext? s_context;
    static CharacterSnapshot? s_lead;
    static bool s_bowsPane;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        PatchReadouts(context);
        PatchWindow(context);

        if (AccessTools.Method("Meitou.Simulation.World:SkillLine") is { } line)
            context.Harmony.Patch(line, postfix: new HarmonyMethod(typeof(BowPane), nameof(Labelled)));
        else
            context.Log("World.SkillLine not found: the HUD labels bows Crossbows");
    }

    static void PatchReadouts(ModContext context)
    {
        const string tips = "Meitou.Simulation.Bodies.HudTips";
        var window = AccessTools.Method($"{tips}:StatsWindow");
        var rows = AccessTools.Method($"{tips}:AddSkillReadouts");
        if (window is null || rows is null)
        {
            context.Log($"{tips}.StatsWindow or AddSkillReadouts not found: the Bows pane has no numbers");
            return;
        }

        s_readouts = AccessTools.MethodDelegate<SkillReadouts>(rows);
        context.Harmony.Patch(window, postfix: new HarmonyMethod(typeof(BowPane), nameof(Published)));
    }

    static void PatchWindow(ModContext context)
    {
        const string stats = "Meitou.Game.Ui.StatsWindow";
        var mod = AccessTools.Method($"{stats}:AddModSkillDescription");
        var panels = AccessTools.Method($"{stats}:AddDescriptionPanels");
        if (mod is null || panels is null)
        {
            context.Log($"{stats}.AddModSkillDescription or AddDescriptionPanels not found: the Bows pane has no numbers");
            return;
        }

        context.Harmony.Patch(mod, prefix: new HarmonyMethod(typeof(BowPane), nameof(Describing)),
            finalizer: new HarmonyMethod(typeof(BowPane), nameof(Described)));
        context.Harmony.Patch(panels, prefix: new HarmonyMethod(typeof(BowPane), nameof(Panels)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Published(CharacterCold cold, MedicalState med, CharacterStats stats, HudCombat? hc, GameConstants? constants, GameDatabase? db,
        Dictionary<StatsEnumerated, IReadOnlyList<StatReadout>> __result)
    {
        try
        {
            if (BowSkill.Skill is { } skill && s_readouts is not null && BowRows(cold, med, stats, hc, constants, db, skill) is { } rows)
                __result[skill.Stat] = rows;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void Describing(CharacterSnapshot? lead, int index)
    {
        s_lead = lead;
        s_bowsPane = BowSkill.Skill is { } skill && index < ModSkills.All.Count && ReferenceEquals(ModSkills.All[index], skill);
    }

    static Exception? Described(Exception? __exception)
    {
        (s_lead, s_bowsPane) = (null, false);
        return __exception;
    }

    static void Panels(ref IReadOnlyList<StatReadout> readouts)
    {
        try
        {
            if (s_bowsPane && readouts.Count == 0 && BowSkill.Skill is { } skill
                && s_lead?.Hud?.StatReadouts?.GetValueOrDefault(skill.Stat) is { Count: > 0 } rows)
                readouts = rows;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void Labelled(CharacterCold cold, ref CurrentSkillView? __result)
    {
        try
        {
            if (__result is { Stat: StatsEnumerated.Crossbows, Label: null } line && BowSkill.Skill is { } skill && BowSkill.HoldsBow(cold))
                __result = line with { Label = skill.Label };
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    /// <summary>
    /// The game's Crossbows readouts (accuracy, rate of fire) at the effective Bows level, labelled with the Bows pane's Affects rows; null when the
    /// game gives none. The stored level is put back however the call ends.
    /// </summary>
    static IReadOnlyList<StatReadout>? BowRows(CharacterCold cold, MedicalState med, CharacterStats stats, HudCombat? hc, GameConstants? constants,
        GameDatabase? db, ModSkill skill)
    {
        float saved = stats.Stored(StatsEnumerated.Crossbows);
        if (float.IsNaN(saved)) return null;

        var rows = new Dictionary<StatsEnumerated, IReadOnlyList<StatReadout>>();
        var factors = (cold.Fighter?.Factors ?? StatFactors.None) with { Stun = med.DustStatFactor };
        stats[StatsEnumerated.Crossbows] = BowSkill.Effective(skill.Level(stats));
        try
        {
            s_readouts!(rows, med, stats, hc, constants, factors, db);
        }
        finally
        {
            stats[StatsEnumerated.Crossbows] = saved;
        }

        if (!rows.TryGetValue(StatsEnumerated.Crossbows, out var game)) return null;

        return [.. game.Select((r, i) => i < skill.Affects.Count ? r with { Label = skill.Affects[i] } : r)];
    }

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"bow pane or HUD line failed ({e.GetType().Name}: {e.Message})");
    }
}
