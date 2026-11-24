using System.Globalization;
using System.Text.RegularExpressions;

namespace Meitou.KenshiAffixes;

/// <summary>
/// The original's ini sections that are lists, not single options (<b>Verified</b>, its <c>KenshiAffixes.ini</c> and <c>RuntimePaths.h</c>): read
/// from its packaged <c>KenshiAffixes.ini</c> (the installed original's folder), then the player's override
/// <c>%LOCALAPPDATA%\KenshiAffixes\KenshiAffixes.ini</c>, then a <c>KenshiAffixes.ini</c> in this port's folder, a later file's key winning.
/// <c>[ExcludeTemplates]</c> <c>ID=1</c> keeps an item template from ever rolling; <c>[FixedNPCs]</c> <c>CHARACTER_ID=profile</c> (or <c>off</c>)
/// names or disables a fixed NPC's profile; <c>[FixedProfile_NAME]</c> <c>Melee=</c> / <c>Armour=</c> give a profile's affix ids, space separated.
/// <c>[LootSiteLevels]</c> <c>TOWN_ID=level</c> sets a site's loot strength. The single options are on the Mod settings page.
/// </summary>
sealed class Overrides
{
    readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public static Overrides None { get; } = new();

    public static Overrides Read(IEnumerable<string> files)
    {
        var overrides = new Overrides();
        foreach (string file in files.Where(File.Exists)) overrides.ReadFile(file);
        return overrides;
    }

    /// <summary>The keys read, for the log.</summary>
    public int Keys => _sections.Values.Sum(s => s.Count);

    /// <summary><c>[ExcludeTemplates]</c>: the template's key is a whole number other than 0 (as <c>GetPrivateProfileInt</c> reads it).</summary>
    public bool Excluded(string record) => Value("ExcludeTemplates", record) is { } v && LeadingInt(v) != 0;

    /// <summary><c>[FixedNPCs]</c>: the profile named for the character template, "off", or null when the ini says nothing.</summary>
    public string? FixedNpc(string character) => Value("FixedNPCs", character);

    /// <summary><c>[FixedProfile_NAME]</c>'s <c>Melee</c> or <c>Armour</c> ids; null when the ini says nothing.</summary>
    public string[]? Profile(string name, bool armour) =>
        Value("FixedProfile_" + name, armour ? "Armour" : "Melee")?.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary><c>[LootSiteLevels]</c>: the TOWN template's level as <c>GetPrivateProfileInt</c> reads it (0 for text), or null without one.</summary>
    public int? SiteLevel(string town) => Value("LootSiteLevels", town) is { } v ? LeadingInt(v) : null;

    string? Value(string section, string key) => _sections.TryGetValue(section, out var s) && s.TryGetValue(key, out string? v) ? v : null;

    void ReadFile(string path)
    {
        if (new FileInfo(path).Length > 1 << 20) return;

        Dictionary<string, string>? section = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim().TrimStart((char)0xFEFF);
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line[0] == '[' && line.EndsWith(']'))
            {
                string name = line[1..^1].Trim();
                if (!_sections.TryGetValue(name, out section)) _sections[name] = section = new(StringComparer.OrdinalIgnoreCase);
            }
            else if (section is not null && line.IndexOf('=') is > 0 and var split)
            {
                section[line[..split].Trim()] = line[(split + 1)..].Trim();
            }
        }
    }

    static int LeadingInt(string text)
    {
        var match = Regex.Match(text, @"^[+-]?\d+");
        return match.Success && int.TryParse(match.Value, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }
}
