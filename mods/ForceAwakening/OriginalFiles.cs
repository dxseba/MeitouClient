using Meitou.Content;

namespace Meitou.ForceAwakening;

/// <summary>
/// Files looked up in this mod's folder, else in the installed original's (Workshop item 3836973645, the port's required item): its Workshop
/// folder beside this one, else the mod named "TheForceAwakening" without code. Nothing of the original (sounds, configs, texts) is shipped
/// with the port; it is all read at runtime from there.
/// </summary>
static class OriginalFiles
{
    public const string OriginalItem = "3836973645";
    const string OriginalName = "TheForceAwakening";

    /// <summary>The first of the folders holding <paramref name="relative"/>, as a full path; null when none does.</summary>
    public static string? Find(string ownFolder, string relative, Action<string> log)
    {
        foreach (string folder in Folders(ownFolder, log))
        {
            string path = Path.Combine(folder, relative);
            if (File.Exists(path)) return Path.GetFullPath(path);
        }

        return null;
    }

    /// <summary>The installed original's folder (its Workshop folder beside this one, else the code-less mod by name); null when absent.</summary>
    public static string? Original(string ownFolder, Action<string> log) =>
        Folders(ownFolder, log).Skip(1).FirstOrDefault(f => File.Exists(Path.Combine(f, "TheForceAwakening.cfg")));

    static IEnumerable<string> Folders(string own, Action<string> log)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in Candidates(own, log))
        {
            if (Directory.Exists(folder) && seen.Add(Path.GetFullPath(folder))) yield return folder;
        }
    }

    static IEnumerable<string> Candidates(string own, Action<string> log)
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
