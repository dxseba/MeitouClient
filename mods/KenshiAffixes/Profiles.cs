using System.Text.Json;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.KenshiAffixes;

/// <summary>
/// The original's authored rolls, read at start from the installed original mod (a required Workshop item): <c>config/fixed-npcs.json</c> (the
/// unique NPCs whose equipped gear takes a profile's affixes in order) and <c>config/meitou.json</c> (the Meitou weapon templates' three
/// affixes at level 100, whoever holds them). None of their values are shipped; without the original only ordinary rolls are made.
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

    /// <summary>The fixed NPC's profile name and its priorities for armour or melee gear; null for any other character.</summary>
    public (string Name, string[] Priorities)? Npc(string characterRecord, bool armour) =>
        _npcs.TryGetValue(characterRecord, out var name) && _profiles.TryGetValue(name, out var p) ? (name, armour ? p.Armour : p.Melee) : null;

    /// <summary>The Meitou template's affixes in order; null for a template the original lists none for (it rolls as ordinary gear).</summary>
    public string[]? Meitou(string record) => _meitou.GetValueOrDefault(record);

    /// <summary>The tables from the installed original, else none (logged once).</summary>
    public static Profiles Load(ModContext context)
    {
        var profiles = new Profiles();
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

    /// <summary><paramref name="name"/> in the original's config folder: its Workshop folder beside this one, else the mod found by name.</summary>
    static string? Find(ModContext context, string name)
    {
        foreach (string folder in Folders(context))
        {
            foreach (string sub in (string[])[Path.Combine("Source", "config"), "config"])
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
            return GameInstall.Locate() is { } install ? ModList.Discover(install).FirstOrDefault(m => m.Name == OriginalName)?.Folder : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
