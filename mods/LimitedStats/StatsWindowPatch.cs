using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LimitedStats;

/// <summary>
/// The original's stats window hooks (<b>Verified</b>, original source <c>printRaceXPBonusLine_hook</c>, <c>RecolorStatsWindow</c>): the selected
/// stat's description shows "&lt;LevelCapLabel&gt;&lt;cap&gt; (&lt;+rate%&gt;)" in place of the race XP bonus line, and with EnableStatColors each
/// stat name takes the colour of its cap kind and rate. A postfix on the game's internal <c>StatsWindow.AddStatsContent</c> (Harmony; there is no
/// mod slot in the window): a game that renamed it leaves the window as it is, with a log line.
/// </summary>
static class StatsWindowPatch
{
    const int RaceBonusLine = 451;

    static CapBoard s_board = new();
    static GuiConfig s_gui = new();
    static Action<string> s_log = _ => { };
    static FieldInfo? s_player, s_selectedStat, s_help;
    static MethodInfo? s_selected;
    static bool s_failed;

    public static void Install(ModContext context, CapBoard board, GuiConfig gui)
    {
        (s_board, s_gui, s_log) = (board, gui, context.Log);
        var window = AccessTools.TypeByName("Meitou.Game.Ui.StatsWindow");
        var target = window is null ? null : AccessTools.Method(window, "AddStatsContent");
        s_player = window?.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType.Name == "PlayerInterface");
        s_selected = s_player is null ? null : AccessTools.Method(s_player.FieldType, "Selected");
        s_selectedStat = window is null ? null : AccessTools.Field(window, "SelectedStat");
        s_help = window is null ? null : AccessTools.Field(window, "StatHelp");
        if (target is null || s_selected is null || s_selectedStat is null || s_help is null)
        {
            context.Log("stats window members not found; the Level Cap line and stat colours are off");
            return;
        }

        context.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(StatsWindowPatch), nameof(AfterStatsContent)));
    }

#pragma warning disable IDE1006 // Harmony binds __instance by name
    static void AfterStatsContent(object __instance, List<UiControl> controls)
#pragma warning restore IDE1006
    {
        if (s_failed) return;

        try
        {
            if (Lead(__instance) is not { } lead || s_board.Read(lead) is not var (entry, _)) return;

            if (s_gui.Colours && !entry.IsAnimal) Recolour(controls, entry);
            if (int.TryParse((string?)s_selectedStat!.GetValue(__instance), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                ShowCap(__instance, controls, entry, (StatsEnumerated)n);
        }
        catch (Exception e)
        {
            s_failed = true;
            s_log($"stats window hook failed, off for the session: {e.Message}");
        }
    }

    static CharacterId? Lead(object window) =>
        s_player!.GetValue(window) is { } player && s_selected!.Invoke(player, null) is IReadOnlyList<CharacterSnapshot> { Count: > 0 } selected
            ? selected[0].Id
            : null;

    /// <summary>Each stat row's label in the colour of its cap kind and rate, where the ini gives one.</summary>
    static void Recolour(List<UiControl> controls, CapEntry entry)
    {
        for (int i = 0; i < controls.Count; i++)
        {
            var c = controls[i];
            if (!c.Name.Contains("-label-", StringComparison.Ordinal) || !c.Action.StartsWith("stat:", StringComparison.Ordinal)) continue;
            if (!int.TryParse(c.Action.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || Cap(entry, n) is not { } cap) continue;

            var stat = (StatsEnumerated)n;
            int kind = s_gui.CapKind(cap, entry.OwnCap[n]);
            if (s_gui.NameColour(stat, kind, RateKind(entry, n)) is not { } colour) continue;

            controls[i] = c with { Properties = new Dictionary<string, string>(c.Properties) { ["TextColour"] = GuiConfig.Property(colour) } };
        }
    }

    /// <summary>The description body with the race XP bonus paragraph replaced by the cap line.</summary>
    static void ShowCap(object window, List<UiControl> controls, CapEntry entry, StatsEnumerated stat)
    {
        int index = controls.FindIndex(c => c.Name == "stat-description-body");
        if (index < 0 || Cap(entry, (int)stat) is not { } cap) return;

        var body = controls[index];
        string text = body.Caption;
        string race = s_help!.GetValue(window) is StatHelpCatalog help ? help.TextAt(RaceBonusLine) : "";
        int cut = race.Length > 0 ? text.LastIndexOf("\n\n" + race + " ", StringComparison.Ordinal) : -1;
        if (cut >= 0) text = text[..cut];

        controls[index] = body with { Caption = text + "\n\n" + CapLine(entry, (int)stat, cap) };
    }

    static string CapLine(CapEntry entry, int n, float cap)
    {
        float delta = (entry.Rates[n] - 1) * 100;
        int rateKind = RateKind(entry, n);
        string capTag = "", rateTag = "";
        if (s_gui.Colours)
        {
            var capColour = s_gui.CapKind(cap, entry.OwnCap[n]) switch { 1 => s_gui.HighCap, 2 => s_gui.LowCap, 3 => s_gui.SpecialCap, _ => null };
            var rateColour = rateKind > 0 ? s_gui.Buff : rateKind < 0 ? s_gui.Nerf : null;
            capTag = capColour is { } cc ? GuiConfig.Tag(cc) : "";
            rateTag = rateColour is { } rc ? GuiConfig.Tag(rc) : "";
        }

        string capText = MathF.Round(cap, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);
        string rateText = MathF.Round(delta, MidpointRounding.ToEven).ToString("+0;-0;+0", CultureInfo.InvariantCulture);
        return $"{s_gui.Label}{capTag}{capText} {rateTag}({rateText}%)";
    }

    static float? Cap(CapEntry entry, int n) => n > 0 && n < entry.Caps.Length && entry.Caps[n] >= 0 ? entry.Caps[n] : null;

    static int RateKind(CapEntry entry, int n) => entry.Rates[n] > 1 ? 1 : entry.Rates[n] < 1 ? -1 : 0;
}
