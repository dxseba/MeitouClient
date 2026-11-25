using Meitou.Content;
using Meitou.Modding;

namespace Meitou.CityBuilder;

/// <summary>
/// The installed original's folder (Workshop item 3833745044, the port's required item, mod name "City"): its Workshop folder beside this mod's,
/// else the installed mod of that name without code. City.ini, layouts.txt, zh_CN.txt and City.dll's Russian texts are read from there, never
/// shipped with the port.
/// </summary>
static class OriginalFolder
{
    const string OriginalItem = "3833745044";
    const string OriginalName = "City";

    /// <summary>
    /// The original's folder; null (logged) when it is not installed.
    /// </summary>
    public static string? Find(ModContext context)
    {
        string own = Path.GetFullPath(context.Folder);
        string? found = Sibling(own) ?? Installed(context, own);
        if (found is null) context.Log($"the original mod (Workshop {OriginalItem}) is not installed: shipped defaults, no furniture, English only");

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
