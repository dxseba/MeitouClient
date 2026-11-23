using System.Globalization;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Ui;

namespace Meitou.LimitedStats;

/// <summary>
/// The original's stats window hooks (<b>Verified</b>, original source <c>printRaceXPBonusLine_hook</c>, <c>RecolorStatsWindow</c>): the selected
/// stat's description shows "&lt;LevelCapLabel&gt;&lt;cap&gt; (&lt;+rate%&gt;)" in place of the race XP bonus line, and with EnableStatColors each
/// stat name takes the colour of its cap kind and rate. Screen overrides on the stats window (<see cref="UiRegistration.AddScreenOverrides"/>): the
/// stat rows' labels (<c>*-label-*</c> with action <c>stat:N</c>) take a colour, and the description body <c>stat-description-body</c> its caption
/// with the window's <c>race-bonus-line</c> replaced, for the window's <c>selected</c> stat and character.
/// </summary>
sealed class StatsWindowOverrides(CapBoard board, GuiConfig gui) : IUiExtension
{
    const string Screen = "stats", Body = "stat-description-body", StatAction = "stat:";

    public string Id => "dxseba.limitedstats.stats";

    public void Register(UiRegistration registration)
    {
        registration.AddScreenOverrides(Screen, "*-label-*", Label);
        registration.AddScreenOverrides(Screen, Body, Description);
    }

    /// <summary>A stat row's label in the colour of its cap kind and rate, where the ini gives one.</summary>
    UiWidgetOverride? Label(UiScreenContext context)
    {
        if (!gui.Colours || Entry(context) is not { IsAnimal: false } entry || context.Widget is not { } widget) return null;
        if (!widget.Action.StartsWith(StatAction, StringComparison.Ordinal)) return null;
        if (!int.TryParse(widget.Action.AsSpan(StatAction.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
        if (Cap(entry, n) is not { } cap) return null;

        int kind = gui.CapKind(cap, entry.OwnCap[n]);
        return gui.NameColour((StatsEnumerated)n, kind, RateKind(entry, n)) is { } colour ? new() { TextColour = new Vector4(colour, 1) } : null;
    }

    /// <summary>The description body with the race XP bonus paragraph replaced by the cap line (added when the window shows none).</summary>
    UiWidgetOverride? Description(UiScreenContext context)
    {
        if (Entry(context) is not { } entry || context.Widget is not { } widget || !context.State.TryGetValue("selected", out var selected))
        {
            return null;
        }

        if (!int.TryParse(selected, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || Cap(entry, n) is not { } cap) return null;

        string text = widget.Caption;
        string race = context.State.GetValueOrDefault("race-bonus-line", "");
        if (race.Length > 0 && text.EndsWith("\n\n" + race, StringComparison.Ordinal)) text = text[..^(race.Length + 2)];

        return new() { Caption = text + "\n\n" + CapLine(entry, n, cap) };
    }

    CapEntry? Entry(UiScreenContext context) =>
        context.Character is { } character && board.Read(character.Id) is var (entry, _) ? entry : null;

    string CapLine(CapEntry entry, int n, float cap)
    {
        float delta = (entry.Rates[n] - 1) * 100;
        int rateKind = RateKind(entry, n);
        string capTag = "", rateTag = "";
        if (gui.Colours)
        {
            var capColour = gui.CapKind(cap, entry.OwnCap[n]) switch { 1 => gui.HighCap, 2 => gui.LowCap, 3 => gui.SpecialCap, _ => null };
            var rateColour = rateKind > 0 ? gui.Buff : rateKind < 0 ? gui.Nerf : null;
            capTag = capColour is { } cc ? GuiConfig.Tag(cc) : "";
            rateTag = rateColour is { } rc ? GuiConfig.Tag(rc) : "";
        }

        string capText = MathF.Round(cap, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);
        string rateText = MathF.Round(delta, MidpointRounding.ToEven).ToString("+0;-0;+0", CultureInfo.InvariantCulture);
        return $"{gui.Label}{capTag}{capText} {rateTag}({rateText}%)";
    }

    static float? Cap(CapEntry entry, int n) => n > 0 && n < entry.Caps.Length && entry.Caps[n] >= 0 ? entry.Caps[n] : null;

    static int RateKind(CapEntry entry, int n) => entry.Rates[n] > 1 ? 1 : entry.Rates[n] < 1 ? -1 : 0;
}
