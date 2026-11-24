using Meitou.Content;
using Meitou.Modding;

namespace Meitou.JobManagement;

/// <summary>
/// Files looked up in this mod's folder, else in the installed original's (Workshop item 3818460323, the port's required item): its Workshop
/// folder beside this one, else the mod named "KenshiJobManagement" without code (the port keeps the original's <c>.mod</c> name). Nothing of
/// the original is shipped with the port.
/// </summary>
sealed class OriginalFiles
{
    const string OriginalItem = "3818460323";
    const string OriginalName = "KenshiJobManagement";

    readonly IReadOnlyList<string> _folders;

    public OriginalFiles(ModContext context)
    {
        _folders = Folders(context).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();
        if (_folders.Count < 2) context.Log($"the original mod (Workshop {OriginalItem}) is not installed: no station icons");
    }

    /// <summary>The installed original's folder (the first found after this mod's own); null when it is not installed.</summary>
    public string? Original => _folders.Count > 1 ? _folders[1] : null;

    /// <summary>
    /// The first of <paramref name="relative"/>'s spellings found in the folders, in order; null when none is.
    /// </summary>
    public string? Find(params string[] relative)
    {
        foreach (string folder in _folders)
        {
            foreach (string name in relative)
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }
        }

        return null;
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
