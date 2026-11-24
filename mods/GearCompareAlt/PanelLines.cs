using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The panel's rows as tooltip lines (the game's tooltip look): the title, the column heads, a row per difference ("worn → new" and the change in
/// the better or worse colour), then the axes, the optional total and the verdict.
/// </summary>
static class PanelLines
{
    public static IReadOnlyList<TooltipLine> Build(string title, Comparison c, GearSettings settings, Func<string, string> tr)
    {
        var lines = new List<TooltipLine>
        {
            new(title, "", TooltipTone.Title),
            new("", tr("worn") + " → " + tr("new"), TooltipTone.Main, TooltipTone.Main, "   " + tr("change"), TooltipTone.Main),
        };

        foreach (var row in c.Rows) lines.Add(Coloured(row.Label, row.Worn + " → " + row.New, "   " + row.Change, row.Side, settings));
        if (c.Rows.Count == 0) lines.Add(new(tr("No difference"), "", TooltipTone.Ink));

        lines.Add(new("----------------", "", TooltipTone.Grey));
        AddAxes(lines, c, settings, tr);
        return lines;
    }

    static void AddAxes(List<TooltipLine> lines, Comparison c, GearSettings settings, Func<string, string> tr)
    {
        int main = (int)MathF.Round(c.MainPercent);
        lines.Add(Coloured(tr(c.MainLabel), Signed(main) + "%", "", Math.Sign(main), settings));
        lines.Add(new(tr("Properties"), $"{c.Better} {tr("better")} / {c.Worse} {tr("worse")}", TooltipTone.Ink, TooltipTone.Ink));

        int weightSide = c.WeightChange > 0.05f ? -1 : c.WeightChange < -0.05f ? 1 : 0;
        lines.Add(Coloured(tr("Weight, kg"), c.WeightChange.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture), "", weightSide, settings));

        if (settings.ShowTotal)
        {
            float total = c.Total(settings.TotalPerProperty, settings.TotalPerKg);
            int side = total > 2 ? 1 : total < -2 ? -1 : 0;
            lines.Add(Coloured(tr("Total"), Signed((int)MathF.Round(total)) + "%", "", side, settings));
        }

        var (word, verdictSide) = c.Verdict switch
        {
            1 => ("better", 1),
            -1 => ("worse", -1),
            2 => ("mixed", 0),
            _ => ("equal", 0),
        };
        lines.Add(Coloured(tr("Verdict"), tr(word), "", verdictSide, settings));
    }

    static string Signed(int v) => v.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    /// <summary>A row whose value and change take the better or worse colour: the game's Good / Bad tones for <c>auto</c>, else the setting's
    /// colour.</summary>
    static TooltipLine Coloured(string key, string value, string suffix, int side, GearSettings settings)
    {
        var tone = side > 0 ? TooltipTone.Good : side < 0 ? TooltipTone.Bad : TooltipTone.Ink;
        string? custom = side > 0 ? Rgb(settings.ColourBetter) : side < 0 ? Rgb(settings.ColourWorse) : null;
        return new TooltipLine(key, value, TooltipTone.Ink, tone, suffix, tone) { ValueColour = custom };
    }

    /// <summary>A <c>#RRGGBB</c> setting as MyGUI's "r g b" text; null for <c>auto</c> or anything else.</summary>
    static string? Rgb(string setting)
    {
        string s = setting.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return null;

        string Channel(int shift) => (((rgb >> shift) & 0xff) / 255f).ToString("0.###", CultureInfo.InvariantCulture);
        return $"{Channel(16)} {Channel(8)} {Channel(0)}";
    }
}
