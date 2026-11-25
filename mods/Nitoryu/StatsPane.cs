using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Nitoryu;

/// <summary>What the Dual Wielding pane shows of a character (written by the serial update).</summary>
sealed record PaneInfo(float Effective, float Armour, float Overload, float Path, int Tier, string Next, float Speed, string Pair, bool LeftHand);

/// <summary>
/// The Dual Wielding pane's live lines (README "Where you see it in game"): Dual Wielding in a fight now (with the armour and overload factors),
/// the Path and rank and what the next rank needs, the dual speed bonus, the fight's Dexterity bonus, the pair and what it gives. The stats
/// window shows a code mod's skill without readouts, so (as the BowMod port) a postfix on <c>Meitou.Simulation.Bodies.HudTips.StatsWindow</c>
/// publishes the lines under the skill's stat, a prefix and finalizer on <c>Meitou.Game.Ui.StatsWindow.AddModSkillDescription</c> note the pane,
/// and a prefix on <c>Meitou.Game.Ui.StatsWindow.AddDescriptionPanels</c> hands the lines in. A failure logs once and leaves the plain pane.
/// </summary>
static class StatsPane
{
    public static readonly ConditionalWeakTable<CharacterCold, PaneInfo> Info = [];

    static ModSkill? s_skill;
    static ModContext? s_context;
    static CharacterSnapshot? s_lead;
    static bool s_mine;
    static int s_failed;

    public static void Patch(ModContext context, ModSkill skill)
    {
        (s_context, s_skill) = (context, skill);
        var window = AccessTools.Method("Meitou.Simulation.Bodies.HudTips:StatsWindow");
        var mod = AccessTools.Method("Meitou.Game.Ui.StatsWindow:AddModSkillDescription");
        var panels = AccessTools.Method("Meitou.Game.Ui.StatsWindow:AddDescriptionPanels");
        if (window is null || mod is null || panels is null)
        {
            context.Log("HudTips.StatsWindow or the stats window's description methods not found: the Dual Wielding pane has no live lines");
            return;
        }

        context.Harmony.Patch(window, postfix: new HarmonyMethod(typeof(StatsPane), nameof(Published)));
        context.Harmony.Patch(mod, prefix: new HarmonyMethod(typeof(StatsPane), nameof(Describing)),
            finalizer: new HarmonyMethod(typeof(StatsPane), nameof(Described)));
        context.Harmony.Patch(panels, prefix: new HarmonyMethod(typeof(StatsPane), nameof(Panels)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Published(CharacterCold cold, Dictionary<StatsEnumerated, IReadOnlyList<StatReadout>> __result)
    {
        try
        {
            if (s_skill is { } skill && Info.TryGetValue(cold, out var info)) __result[skill.Stat] = Rows(info);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void Describing(CharacterSnapshot? lead, int index)
    {
        s_lead = lead;
        s_mine = s_skill is { } skill && index < ModSkills.All.Count && ReferenceEquals(ModSkills.All[index], skill);
    }

    static Exception? Described(Exception? __exception)
    {
        (s_lead, s_mine) = (null, false);
        return __exception;
    }

    static void Panels(ref IReadOnlyList<StatReadout> readouts)
    {
        try
        {
            if (s_mine && readouts.Count == 0 && s_skill is { } skill && s_lead?.Hud?.StatReadouts?.GetValueOrDefault(skill.Stat) is { Count: > 0 } rows)
                readouts = rows;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    static string N(float v, string format = "0") => v.ToString(format, CultureInfo.InvariantCulture);

    static IReadOnlyList<StatReadout> Rows(PaneInfo i)
    {
        string fight = N(i.Effective) + (i.Armour < 1 ? $"  -Armour x{N(i.Armour, "0.00")}" : "") + (i.Overload < 1 ? $"  -Overloaded x{N(i.Overload, "0.00")}" : "");
        var rows = new List<StatReadout>
        {
            new("In a fight now", fight, Main: true),
            new("Path", $"{(i.Tier > 0 ? Ranks.Name(i.Tier) : "none")} ({N(i.Path)})"),
        };
        if (i.Next.Length > 0) rows.Add(new("Next rank", i.Next));
        rows.Add(new("Dual speed bonus", "x" + N(i.Speed, "0.00")));
        rows.Add(new("Dexterity in a fight", "+12%"));
        if (i.Pair.Length > 0) rows.Add(new("Pair", i.Pair));
        if (i.LeftHand) rows.Add(new("Left hand", "fighting left-handed"));
        return rows;
    }

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"Dual Wielding pane failed ({e.GetType().Name}: {e.Message})");
    }
}
