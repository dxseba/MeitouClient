using Meitou.Content;

namespace Meitou.BowMod;

/// <summary>
/// Files looked up in this mod's folder, else in the installed original's (Workshop item 3838294517, the port's required item): its Workshop
/// folder beside this one, else the mod named "BowMod" without code. Nothing of the original is shipped with the port.
/// </summary>
static class OriginalFiles
{
    const string OriginalItem = "3838294517";
    const string OriginalName = "BowMod";

    /// <summary>The first of the folders holding <paramref name="relative"/>, as a full path; null when none does.</summary>
    public static string? Find(string ownFolder, string relative, Action<string> log)
    {
        foreach (string folder in Folders(ownFolder, log).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(folder, relative);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    static IEnumerable<string> Folders(string own, Action<string> log)
    {
        yield return own;

        if (Path.GetDirectoryName(Path.GetFullPath(own)) is { } parent) yield return Path.Combine(parent, OriginalItem);

        if (Installed(own, log) is { } found) yield return found;
    }

    static string? Installed(string own, Action<string> log)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string full = Path.GetFullPath(own);
            bool IsOriginal(ModInfo m) =>
                m.Name == OriginalName && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), full, StringComparison.OrdinalIgnoreCase);

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
