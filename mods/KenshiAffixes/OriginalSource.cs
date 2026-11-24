using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Meitou.KenshiAffixes;

/// <summary>
/// The Chinese halves of the original's bilingual texts, read at start from the installed original's source (never shipped): each affix's
/// <c>name</c> and <c>effect</c> in <c>Source/config/affixes.json</c> (its English halves are <c>name_en</c> / <c>effect_en</c>; the original shows
/// "zh / en", <b>Verified</b>, <c>GeneratedAffixes.h</c>), and the tooltip's fixed texts, the <c>UI_*</c> strings of
/// <c>Source/native/GeneratedAffixes.h</c> (UTF-8 bytes written as <c>\x</c> escapes). A text is used only when its English half matches the
/// port's; anything missing leaves the English text.
/// </summary>
sealed class OriginalTexts
{
    readonly Dictionary<string, (string Name, string Effect)> _affixes = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _ui = new(StringComparer.Ordinal);

    public static OriginalTexts None { get; } = new();

    public bool Loaded => _affixes.Count > 0;

    /// <summary>"zh / en" for the affix's name, or its English name.</summary>
    public string Name(AffixKind kind) => _affixes.TryGetValue(kind.Id, out var t) ? t.Name + " / " + kind.Name : kind.Name;

    /// <summary>"zh / en" for the affix's effect, or its English effect.</summary>
    public string Effect(AffixKind kind) => _affixes.TryGetValue(kind.Id, out var t) ? t.Effect + " / " + kind.Effect : kind.Effect;

    /// <summary>The original's <c>UI_</c> text whose English half is <paramref name="english"/>, else <paramref name="english"/>.</summary>
    public string Ui(string key, string english) => _ui.TryGetValue(key, out string? text) && text.EndsWith(english, StringComparison.Ordinal)
        ? text : english;

    public static OriginalTexts Read(string? affixesJson, string? generatedHeader)
    {
        var texts = new OriginalTexts();
        if (affixesJson is not null) texts.ReadAffixes(File.ReadAllText(affixesJson));
        if (generatedHeader is not null) texts.ReadUi(File.ReadAllText(generatedHeader));
        return texts;
    }

    void ReadAffixes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var a in doc.RootElement.GetProperty("affixes").EnumerateArray())
        {
            string id = a.GetProperty("id").GetString() ?? "";
            if (AffixTable.Find(id) is not { } kind) continue;
            if (a.GetProperty("name_en").GetString() != kind.Name || a.GetProperty("effect_en").GetString() != kind.Effect) continue;

            _affixes[id] = (a.GetProperty("name").GetString() ?? kind.Name, a.GetProperty("effect").GetString() ?? kind.Effect);
        }
    }

    void ReadUi(string header)
    {
        foreach (Match m in Regex.Matches(header, @"static const char\* UI_(\w+) = ""((?:\\x[0-9a-fA-F]{2})*)"";"))
        {
            var bytes = Regex.Matches(m.Groups[2].Value, @"\\x([0-9a-fA-F]{2})").Select(b => Convert.ToByte(b.Groups[1].Value, 16)).ToArray();
            _ui[m.Groups[1].Value] = Encoding.UTF8.GetString(bytes);
        }
    }
}

/// <summary>
/// The original's Meitou rule lists, read at start from the installed original's <c>Source/native/MeitouRules.h</c> (never shipped): the six
/// styles a Meitou template it does not list gets (picked by the template id: an FNV-1a hash of its bytes as a xorshift seed, one draw of 0..5,
/// the original's <c>Random</c>), each led by the damage affix (blunt over cut: the second, else the first of its pair) or not, and the fill-in
/// list appended to every Meitou's priorities so a weapon that cannot take one still gets three. Any part not found as expected: none of it
/// is used (listed templates keep their own lists, modded ones roll as ordinary gear).
/// </summary>
sealed class MeitouRules
{
    string[] _fill = [];
    (bool Damage, string[] Ids)[] _styles = [];
    (string Cut, string Blunt) _damage;

    public static MeitouRules None { get; } = new();

    public bool Loaded => _styles.Length == 6;

    /// <summary>A listed template's priorities with the fill-in appended.</summary>
    public IEnumerable<string> Filled(IEnumerable<string> listed) => Loaded ? listed.Concat(_fill) : listed;

    /// <summary>The style of a template the original does not list, with the fill-in; null without the rules.</summary>
    public string[]? Modded(string record, bool blunt)
    {
        if (!Loaded) return null;

        var (damage, ids) = _styles[Style(record)];
        IEnumerable<string> lead = damage ? [blunt ? _damage.Blunt : _damage.Cut] : [];
        return [.. lead, .. ids, .. _fill];
    }

    /// <summary>The original's per-template draw: FNV-1a (32-bit) of the id's bytes, a xorshift (13, 17, 5) step, modulo 6.</summary>
    static int Style(string record)
    {
        uint x = 2166136261u;
        foreach (byte b in Encoding.UTF8.GetBytes(record))
        {
            x ^= b;
            x *= 16777619u;
        }

        if (x == 0) x = 1;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return (int)(x % 6);
    }

    public static MeitouRules Read(string? path)
    {
        if (path is null) return None;

        string text = File.ReadAllText(path);
        var rules = new MeitouRules();
        var damage = Regex.Match(text, @"damage\s*=\s*blunt\s*\?\s*""([a-z]+)\s*""\s*:\s*""([a-z]+)\s*""");
        var fill = Regex.Match(text, @"priorities\s*\+=\s*""([a-z ]+)""");
        if (!damage.Success || !fill.Success || !text.Contains("between(0,5)", StringComparison.Ordinal)) return None;

        rules._damage = (damage.Groups[2].Value, damage.Groups[1].Value);
        rules._fill = Ids(fill.Groups[1].Value);
        rules._styles = Styles(text);
        bool known = rules._styles.All(s => s.Ids.Length > 0) && rules._styles.SelectMany(s => s.Ids).Concat(rules._fill)
            .Append(rules._damage.Cut).Append(rules._damage.Blunt).All(id => AffixTable.Find(id) is not null);
        return rules.Loaded && known ? rules : None;
    }

    /// <summary>Cases 0 to 4 and the default (5), each "damage + ids" or "ids"; fewer than six: none.</summary>
    static (bool, string[])[] Styles(string text)
    {
        var found = new SortedDictionary<int, (bool, string[])>();
        foreach (Match m in Regex.Matches(text, @"(?:case\s+(\d)|default)\s*:\s*priorities\s*=\s*(std::string\(damage\)\s*\+\s*)?""([a-z ]+)"""))
        {
            int index = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 5;
            found[index] = (m.Groups[2].Success, Ids(m.Groups[3].Value));
        }

        return found.Count == 6 && found.Keys.SequenceEqual(Enumerable.Range(0, 6)) ? [.. found.Values] : [];
    }

    static string[] Ids(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
