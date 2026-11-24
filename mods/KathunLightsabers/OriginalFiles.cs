using Meitou.Content;
using Meitou.Modding;

namespace Meitou.KathunLightsabers;

/// <summary>
/// Files looked up in this mod's folder, else in the installed original's (Workshop item 3833105321, the port's required item): its Workshop
/// folder beside this one, else the mod named "KathunGlowingLightsabers" without code. Nothing of the original is shipped with the port.
/// </summary>
sealed class OriginalFiles
{
    const string OriginalItem = "3833105321";
    const string OriginalName = "KathunGlowingLightsabers";

    readonly IReadOnlyList<string> _folders;

    public OriginalFiles(ModContext context)
    {
        _folders = Folders(context).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();
        if (_folders.Count < 2) context.Log($"the original mod (Workshop {OriginalItem}) is not installed: only this folder is read");
    }

    /// <summary>
    /// The first folder holding <paramref name="relative"/>, as a full path; null when none does.
    /// </summary>
    public string? Find(string relative)
    {
        foreach (string folder in _folders)
        {
            string path = Path.Combine(folder, relative);
            if (File.Exists(path)) return path;
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
