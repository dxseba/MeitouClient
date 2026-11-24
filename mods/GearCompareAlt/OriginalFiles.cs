using Meitou.Content;
using Meitou.Modding;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The installed original (Workshop item 3812150687, the port's required item), whose <c>locale</c> catalogues the port reads: its Workshop folder
/// beside this one, else the mod named "GearCompare" without code. Nothing of the original is shipped with the port.
/// </summary>
static class OriginalFiles
{
    const string OriginalItem = "3812150687";
    const string OriginalName = "GearCompare";

    /// <summary>The original's folder; null when it is not installed.</summary>
    public static string? Folder(ModContext context)
    {
        string? beside = Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent ? Path.Combine(parent, OriginalItem) : null;
        if (beside is not null && Directory.Exists(beside)) return beside;

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
