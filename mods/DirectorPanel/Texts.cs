using System.Globalization;
using System.Text.RegularExpressions;

namespace Meitou.DirectorPanel;

/// <summary>A status line: the original's English format text (printf placeholders) and its values.</summary>
internal sealed record StatusLine(string Format, object[] Args);

/// <summary>
/// The panel's two languages (the original's RU/EN toggle, ini <c>Language</c>, default RU): an English text, or its Russian one read from the
/// installed original (<see cref="OriginalFiles.RussianTexts"/>); a text without one stays English. Its printf placeholders (<c>%d</c>,
/// <c>%.0f</c>, <c>%s</c>, <c>%ld</c>) are filled in order.
/// </summary>
internal sealed partial class Texts(IReadOnlyDictionary<string, string> russian)
{
    public bool Russian { get; set; }

    public bool HasRussian => russian.Count > 0;

    /// <summary>
    /// The original's tab names, which its DLL keeps apart from their English (a list of their own), so no pair is read for them: the only
    /// Russian texts the port carries.
    /// </summary>
    static readonly Dictionary<string, string> TabNames = new(StringComparer.Ordinal)
    {
        ["Items"] = "Предметы", ["Stats"] = "Параметры", ["NPC Spawn"] = "Спавн npc", ["Relations"] = "Отношения", ["Research"] = "Технологии",
        ["Behaviour"] = "Поведение", ["Barbershop"] = "Барбершоп", ["Body"] = "Тело", ["Effects"] = "Спецэффекты", ["Teleport"] = "Телепорт",
        ["World"] = "Мир",
    };

    public string this[string english] =>
        !Russian ? english
        : HasRussian && TabNames.TryGetValue(english, out string? tab) ? tab
        : russian.TryGetValue(english, out string? ru) ? ru : english;

    public string Format(StatusLine line) => Fill(this[line.Format], line.Args);

    public string Format(string english, params object[] args) => Fill(this[english], args);

    static string Fill(string format, object[] args)
    {
        int next = 0;
        return Placeholder().Replace(format, m => next < args.Length ? Value(m.Value, args[next++]) : m.Value);
    }

    static string Value(string spec, object arg)
    {
        if (arg is float or double && spec.Contains('.'))
        {
            int digits = spec[spec.IndexOf('.') + 1] - '0';
            return Convert.ToDouble(arg, CultureInfo.InvariantCulture).ToString("F" + Math.Clamp(digits, 0, 6), CultureInfo.InvariantCulture);
        }

        string text = Convert.ToString(arg, CultureInfo.InvariantCulture) ?? "";
        return spec.StartsWith("%0", StringComparison.Ordinal) ? text.PadLeft(spec[2] - '0', '0') : text;
    }

    [GeneratedRegex(@"%(0\d)?(\.\d)?l?[dsf]")]
    private static partial Regex Placeholder();
}
