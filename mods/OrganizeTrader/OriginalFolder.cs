using Meitou.Content;
using Meitou.Modding;

namespace Meitou.OrganizeTrader;

/// <summary>
/// The installed original's folder (Workshop item 3813852189, the MCM fork, the port's required item): its Workshop folder beside this mod's,
/// else the mod named "Organize-the-Trader" without code. Its translation catalogues are read from there, never shipped with the port.
/// </summary>
static class OriginalFolder
{
    const string OriginalItem = "3813852189";
    const string OriginalName = "Organize-the-Trader";

    /// <summary>
    /// The original's folder; null (logged) when it is not installed.
    /// </summary>
    public static string? Find(ModContext context)
    {
        string own = Path.GetFullPath(context.Folder);
        string? found = Sibling(own) ?? Installed(context, own);
        if (found is null) context.Log($"the original mod (Workshop {OriginalItem}) is not installed: the texts stay English");

        return found;
    }

    static string? Sibling(string own) =>
        Path.GetDirectoryName(own) is { } parent && Path.Combine(parent, OriginalItem) is var path && Directory.Exists(path) ? path : null;

    static string? Installed(ModContext context, string own)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            bool IsOriginal(ModInfo m) =>
                m.Name == OriginalName && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(Path.Combine(m.Folder, "locale"));

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
