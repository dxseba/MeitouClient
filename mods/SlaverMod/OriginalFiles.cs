using Meitou.Content;

namespace Meitou.SlaverMod;

/// <summary>
/// The installed original (Workshop item 3828708454, the port's required item): its folder beside this mod's, else the mod named "SlaverMod"
/// without code. Its <c>SlaverMod_races.ini</c>, <c>SlaverMod_lines.ini</c> and <c>SlaverMod.mod</c> are used from there; nothing is shipped.
/// </summary>
static class OriginalFiles
{
    public const string Item = "3828708454";
    const string ModName = "SlaverMod";

    public static string? Folder(string ownFolder)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(ownFolder)) is { } parent && Directory.Exists(Path.Combine(parent, Item)))
            return Path.Combine(parent, Item);

        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(ownFolder);
            bool IsOriginal(ModInfo m) =>
                m.Name == ModName && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase);

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// An ini file as sections of keys (the original's format: <c>[section]</c>, <c>key=value</c>, <c>;</c> comments).
/// </summary>
static class Ini
{
    public static Dictionary<string, Dictionary<string, string>> Read(string? path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (path is null || !File.Exists(path)) return sections;

        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;

            if (line[0] == '[' && line.EndsWith(']'))
            {
                sections[line[1..^1]] = current = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0) current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return sections;
    }
}

/// <summary>
/// How a race takes slavery (<c>SlaverMod_races.ini</c>): start, cap (-1: the setting), gain, loss, escape, punish and price multipliers.
/// </summary>
sealed record RaceTrait(float? Start, float Cap, float Gain, float Loss, float Escape, float Punish, float Value)
{
    public static readonly RaceTrait Default = new(null, -1, 1, 1, 1, 1, 1);

    public static Dictionary<string, RaceTrait> Read(string? folder)
    {
        var table = new Dictionary<string, RaceTrait>(StringComparer.Ordinal);
        foreach (var (race, keys) in Ini.Read(folder is null ? null : Path.Combine(folder, "SlaverMod_races.ini")))
        {
            float? start = keys.ContainsKey("Start") ? F(keys, "Start", 0) : null;
            table[race] = new RaceTrait(start, F(keys, "Cap", -1), F(keys, "Gain", 1), F(keys, "Loss", 1), F(keys, "Escape", 1),
                F(keys, "Punish", 1), F(keys, "Value", 1));
        }

        return table;
    }

    static float F(Dictionary<string, string> keys, string key, float fallback) =>
        keys.TryGetValue(key, out string? s) && float.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
}
