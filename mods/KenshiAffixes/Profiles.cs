using System.Text.Json;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.KenshiAffixes;

/// <summary>
/// The original's authored rolls, read at start from the installed original mod (a required Workshop item): <c>config/fixed-npcs.json</c> (the
/// unique NPCs whose equipped gear takes a profile's affixes in order) and <c>config/meitou.json</c> (the Meitou weapon templates' three
/// affixes at level 100, whoever holds them), with the original's ini lists (<see cref="Overrides"/>), its Meitou fill-in and modded-template styles
/// (<see cref="MeitouRules"/>) and the Chinese halves of its texts (<see cref="OriginalTexts"/>). None of their values are shipped; without the
/// original only ordinary rolls are made.
/// </summary>
sealed class Profiles
{
    const string OriginalItem = "3821983877", OriginalName = "KenshiAffixes";

    /// <summary>The vanilla WEAPON_MODEL of the Meitou grade (a game fact).</summary>
    public const string MeitouMaterial = "52293-rebirth.mod";

    public const string MeitouProfile = "meitou";

    readonly Dictionary<string, (string[] Melee, string[] Armour)> _profiles = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _npcs = new(StringComparer.Ordinal);
    readonly Dictionary<string, string[]> _meitou = new(StringComparer.Ordinal);

    public static readonly Profiles None = new();

    public Overrides Ini { get; private set; } = Overrides.None;

    public MeitouRules Rules { get; private set; } = MeitouRules.None;

    public OriginalTexts Texts { get; private set; } = OriginalTexts.None;

    /// <summary>
    /// The fixed NPC's profile name and its priorities for armour or melee gear; null for any other character. The ini's <c>[FixedNPCs]</c> entry
    /// replaces the built-in one ("off" or empty: none), its <c>[FixedProfile_NAME]</c> list the profile's own (the original's <c>fixedRule</c>).
    /// </summary>
    public (string Name, string[] Priorities)? Npc(string characterRecord, bool armour)
    {
        string? name = Ini.FixedNpc(characterRecord) ?? _npcs.GetValueOrDefault(characterRecord);
        if (string.IsNullOrEmpty(name) || name == "off") return null;

        string[] defaults = _profiles.TryGetValue(name, out var p) ? armour ? p.Armour : p.Melee : [];
        string[] priorities = Ini.Profile(name, armour) ?? defaults;
        return priorities.Length > 0 ? (name, priorities) : null;
    }

    /// <summary>
    /// The Meitou template's affixes in order (with the original's fill-in when its rules were read); a template it does not list takes its style
    /// by id; null without either (it rolls as ordinary gear).
    /// </summary>
    public string[]? Meitou(string record, bool blunt) =>
        _meitou.TryGetValue(record, out var listed) ? [.. Rules.Filled(listed)] : Rules.Modded(record, blunt);

    /// <summary>Whether the ini's <c>[ExcludeTemplates]</c> keeps the template from rolling.</summary>
    public bool Excluded(string record) => Ini.Excluded(record);

    /// <summary>The tables from the installed original, else none (logged once).</summary>
    public static Profiles Load(ModContext context)
    {
        var profiles = new Profiles();
        profiles.LoadExtras(context);
        string? npcs = Find(context, "fixed-npcs.json"), meitou = Find(context, "meitou.json");
        try
        {
            if (npcs is not null) profiles.ReadNpcs(File.ReadAllText(npcs));
            if (meitou is not null) profiles.ReadMeitou(File.ReadAllText(meitou));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            context.Log($"the original's profiles not read ({e.Message}): ordinary rolls only");
            return None;
        }

        if (npcs is null || meitou is null)
            context.Log($"the original mod {OriginalName} (Workshop {OriginalItem}) was not found: no fixed NPC or Meitou profiles");
        return profiles;
    }

    /// <summary>The ini lists, the Meitou rules and the Chinese texts; each one missing or unreadable is left out with a log line.</summary>
    void LoadExtras(ModContext context)
    {
        try
        {
            Ini = Overrides.Read(IniFiles(context));
            Rules = MeitouRules.Read(Find(context, "MeitouRules.h", Path.Combine("Source", "native")));
            Texts = OriginalTexts.Read(Find(context, "affixes.json"), Find(context, "GeneratedAffixes.h", Path.Combine("Source", "native")));
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            context.Log($"the original's ini or source not read ({e.Message})");
        }

        string rules = Rules.Loaded ? "read" : "not found", texts = Texts.Loaded ? "read" : "not found";
        context.Log($"ini keys {Ini.Keys}, Meitou rules {rules}, Chinese texts {texts}");
    }

    /// <summary>The original's packaged ini, the player's override in %LOCALAPPDATA%\KenshiAffixes, then this port's own (later wins).</summary>
    static IEnumerable<string> IniFiles(ModContext context)
    {
        foreach (string folder in Folders(context)) yield return Path.Combine(folder, "KenshiAffixes.ini");

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length > 0) yield return Path.Combine(local, "KenshiAffixes", "KenshiAffixes.ini");

        yield return Path.Combine(context.Folder, "KenshiAffixes.ini");
    }

    void ReadNpcs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.GetProperty("profiles").EnumerateObject())
            _profiles[p.Name] = (Ids(p.Value.GetProperty("melee")), Ids(p.Value.GetProperty("armour")));

        foreach (var npc in doc.RootElement.GetProperty("npcs").EnumerateArray())
            _npcs[npc.GetProperty("id").GetString()!] = npc.GetProperty("profile").GetString()!;
    }

    void ReadMeitou(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var weapon in doc.RootElement.GetProperty("weapons").EnumerateArray())
            _meitou[weapon.GetProperty("id").GetString()!] = Ids(weapon.GetProperty("affixes"));
    }

    static string[] Ids(JsonElement list) => [.. list.EnumerateArray().Select(e => e.GetString() ?? "")];

    /// <summary>
    /// <paramref name="name"/> in the original's config folder (or <paramref name="under"/>): its Workshop folder beside this one, else the mod
    /// found by name.
    /// </summary>
    static string? Find(ModContext context, string name, string? under = null)
    {
        foreach (string folder in Folders(context))
        {
            foreach (string sub in under is null ? (string[])[Path.Combine("Source", "config"), "config"] : [under])
            {
                string path = Path.Combine(folder, sub, name);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    static IEnumerable<string> Folders(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent) yield return Path.Combine(parent, OriginalItem);
        if (OriginalFolder(context) is { } found) yield return found;
    }

    static string? OriginalFolder(ModContext context)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            return ModList.Discover(install).FirstOrDefault(m => m.Name == OriginalName && !m.HasCode)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
