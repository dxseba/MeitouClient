using System.Globalization;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// The window's texts, column weights and font height from the installed original's <c>PersistentPrisoners.languages.ini</c> (Workshop 3798215878,
/// the port's required item; its <c>.local.ini</c> beside it wins, as in the original), for the game's language: the section named by the locale
/// code ignoring case, else the first of the same language part (<c>de</c> for <c>de_DE</c>, <c>en</c> for <c>en_GB</c>), over <c>[en]</c>, over
/// the port's English. Read at start, never shipped. <c>{name}</c> placeholders are filled by <see cref="Get"/>.
/// </summary>
sealed class OriginalTexts
{
    const string OriginalItem = "3798215878";
    const string OriginalName = "PersistentPrisoners";
    const string FileName = "PersistentPrisoners.languages.ini";
    const string LocalName = "PersistentPrisoners.languages.local.ini";

    /// <summary>The ini's column keys, in the window's column order.</summary>
    static readonly string[] Columns = ["name", "faction", "cage", "nu", "condition", "location", "status"];

    readonly Dictionary<string, string> _shown;
    readonly Dictionary<string, string> _keyOfEnglish;

    OriginalTexts(Dictionary<string, string> shown, Dictionary<string, string> english)
    {
        _shown = shown;
        _keyOfEnglish = english.GroupBy(e => e.Value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
    }

    /// <summary>The texts for <paramref name="context"/>'s language (none, so the port's English, when the file is not found).</summary>
    public static OriginalTexts Load(ModContext context)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (string folder in Folders(context))
        {
            Read(Path.Combine(folder, FileName), sections, order);
            Read(Path.Combine(folder, LocalName), sections, order);
            if (sections.Count > 0) break;
        }

        var english = sections.GetValueOrDefault("en") ?? [];
        var shown = new Dictionary<string, string>(english, StringComparer.Ordinal);
        string? section = Section(order, context.Language);
        if (section is not null)
        {
            foreach (var (key, value) in sections[section]) shown[key] = value;
        }

        context.Log(sections.Count == 0
            ? "original's languages.ini not found: English texts and own column widths"
            : $"texts from the original's languages.ini, section [{section ?? "en"}] for {context.Language}");
        return new(shown, english);
    }

    /// <summary>The text of <paramref name="key"/> (<paramref name="english"/> when missing), its <c>{name}</c> placeholders filled.</summary>
    public string Get(string key, string english, params (string Name, object Value)[] args)
    {
        string text = _shown.TryGetValue(key, out string? found) && found.Length > 0 ? found : english;
        foreach (var (name, value) in args)
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return text;
    }

    /// <summary>A text the system produced in English (a condition, a status, "Unknown"), in the shown language when <c>[en]</c> has it.</summary>
    public string Of(string english) => _keyOfEnglish.TryGetValue(english, out string? key) ? Get(key, english) : english;

    /// <summary>The seven column weights (1 to 1000 each), or null when one is missing or out of range.</summary>
    public float[]? Widths()
    {
        var widths = Columns.Select(k => Number("width." + k)).ToArray();
        return widths.All(w => w is >= 1 and <= 1000) ? widths : null;
    }

    /// <summary><c>font.height</c>: 10 to 32 sets the text height; 0, missing or out of range keeps the game's font size (0).</summary>
    public int FontHeight() => Number("font.height") is var h && h is >= 10 and <= 32 ? (int)h : 0;

    float Number(string key) =>
        _shown.TryGetValue(key, out string? v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN;

    /// <summary>The section for <paramref name="language"/>: its code ignoring case, else the first with the same language part.</summary>
    static string? Section(List<string> order, string language)
    {
        string part = language.Split('_', '-')[0];
        return order.FirstOrDefault(s => string.Equals(s, language, StringComparison.OrdinalIgnoreCase))
            ?? order.FirstOrDefault(s => string.Equals(s.Split('_', '-')[0], part, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The <c>key=value</c> lines of <paramref name="path"/> by section (later files win).</summary>
    static void Read(string path, Dictionary<string, Dictionary<string, string>> sections, List<string> order)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 1 << 20) return;

        Dictionary<string, string>? current = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim().TrimStart((char)0xFEFF);
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line.StartsWith('[') && line.EndsWith(']')) current = SectionOf(line[1..^1].Trim(), sections, order);
            else if (current is not null && line.IndexOf('=') is > 0 and var split) current[line[..split].Trim()] = line[(split + 1)..].Trim();
        }
    }

    static Dictionary<string, string> SectionOf(string name, Dictionary<string, Dictionary<string, string>> sections, List<string> order)
    {
        if (sections.TryGetValue(name, out var found)) return found;

        order.Add(name);
        return sections[name] = new(StringComparer.Ordinal);
    }

    static IEnumerable<string> Folders(ModContext context)
    {
        yield return context.Folder;

        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent) yield return Path.Combine(parent, OriginalItem);

        if (Installed(context) is { } found) yield return found;
    }

    static string? Installed(ModContext context)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(context.Folder);
            bool IsOriginal(ModInfo m) =>
                m.Name == OriginalName && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase);

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
