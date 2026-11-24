using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.ColoredStats;

/// <summary>
/// Port of ColoredStats (workshop 3805088430, <c>StatsColor.dll</c>): every number shown on the stats window takes a colour by its value, a red
/// to green-cyan scale in steps of 10, as an inline colour tag before it.
/// </summary>
public sealed class ColoredStatsMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddUi(new StatColours());
        context.Log("ColoredStats initialized");
    }
}

/// <summary>
/// A screen override on every widget of the stats window: a caption that is only a number (after any colour tag it starts with) becomes
/// "#RRGGBB number", the colour of its value's step.
/// </summary>
sealed class StatColours : IUiExtension
{
    /// <summary>
    /// The colour of a value below 0 (the DLL's).
    /// </summary>
    const string Negative = "#A83232";

    /// <summary>
    /// The colour of each step of 10, from 0-9 up to 100 and above (the DLL's eleven).
    /// </summary>
    static readonly string[] Steps =
        ["#FF5252", "#FF7A4D", "#FFA64D", "#FFD24D", "#FFFF4D", "#D9FF4D", "#A6FF4D", "#73FF4D", "#4DFF4D", "#4DFF80", "#4DFFB3"];

    public string Id => "dxseba.coloredstats.stats";

    public void Register(UiRegistration registration) => registration.AddScreenOverrides("stats", "*", Recolour);

    static UiWidgetOverride? Recolour(UiScreenContext context)
    {
        if (context.Widget is not { } widget || Number(widget.Caption) is not { } number) return null;
        if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return null;

        string caption = ColourOf(value) + " " + number;
        return caption == widget.Caption ? null : new UiWidgetOverride { Caption = caption };
    }

    /// <summary>
    /// The number a caption shows, or null: a leading colour tag and one space after it dropped, the rest trimmed of spaces, tabs and line breaks,
    /// then only digits, a minus first and one point (whitespace inside is dropped), with at least one digit.
    /// </summary>
    internal static string? Number(string caption)
    {
        string text = caption;
        if (text.Length > 6 && text[0] == '#')
        {
            text = text[7..];
            if (text.StartsWith(' ')) text = text[1..];
        }

        return Digits(text.Trim(' ', '\t', '\n', '\r'));
    }

    static string? Digits(string text)
    {
        var kept = new System.Text.StringBuilder(text.Length);
        bool digit = false, point = false;
        foreach (char c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                kept.Append(c);
                digit = true;
            }
            else if (c == '-' && kept.Length == 0) kept.Append(c);
            else if (c == '.' && !point)
            {
                kept.Append(c);
                point = true;
            }
            else if (c is not (' ' or '\t' or '\r' or '\n')) return null;
        }

        return digit ? kept.ToString() : null;
    }

    /// <summary>
    /// The colour of <paramref name="value"/>: below 0 the negative colour, else step value / 10 (truncated), at most the last.
    /// </summary>
    internal static string ColourOf(float value) => value < 0 ? Negative : Steps[Math.Clamp((int)(value / 10), 0, Steps.Length - 1)];
}
