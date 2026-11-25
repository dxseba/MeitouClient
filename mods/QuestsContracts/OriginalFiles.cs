using Meitou.Content;

namespace Meitou.QuestsContracts;

/// <summary>
/// The installed original (Workshop item 3797459646, the port's required item): its Workshop folder beside this mod's, else the mod named
/// "QuestsAndContracts" without code. Its lists (<c>ledger-*.txt</c>) are read from there at runtime; nothing of the original is shipped.
/// </summary>
static class OriginalFiles
{
    public const string Item = "3797459646";
    const string ModName = "QuestsAndContracts";

    /// <summary>
    /// The original's folder (null when it is not installed). <paramref name="ownFolder"/> is this mod's folder; a list file in it wins.
    /// </summary>
    public static string? Folder(string ownFolder)
    {
        if (File.Exists(Path.Combine(ownFolder, "ledger-vars.txt"))) return ownFolder;

        if (Path.GetDirectoryName(Path.GetFullPath(ownFolder)) is { } parent && Directory.Exists(Path.Combine(parent, Item)))
            return Path.Combine(parent, Item);

        return Installed(ownFolder);
    }

    static string? Installed(string ownFolder)
    {
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
