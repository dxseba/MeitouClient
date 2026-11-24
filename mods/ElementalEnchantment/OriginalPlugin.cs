using Meitou.Content;
using Meitou.Modding;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// Finds the installed original's plugin (Workshop item 3832978749, the port's required item): its Workshop folder beside this one, else the
/// mod named "Elemental Enchantment" without code; then <c>plugin/ElementalEnchantmentTest.dll</c>. Nothing of the original is shipped.
/// </summary>
static class OriginalPlugin
{
    const string OriginalItem = "3832978749";
    const string OriginalName = "Elemental Enchantment";
    const string PluginPath = "plugin/ElementalEnchantmentTest.dll";

    /// <summary>
    /// The plugin's path; null when the original is not installed.
    /// </summary>
    public static string? Find(ModContext context) => Find(context.Folder, context.Log);

    /// <summary>
    /// The plugin's path for the mod in <paramref name="ownFolder"/>; null when the original is not installed.
    /// </summary>
    public static string? Find(string ownFolder, Action<string> log)
    {
        foreach (string folder in Folders(ownFolder, log))
        {
            string path = Path.Combine(folder, PluginPath);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    static IEnumerable<string> Folders(string ownFolder, Action<string> log)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(ownFolder)) is { } parent) yield return Path.Combine(parent, OriginalItem);

        if (Installed(ownFolder, log) is { } found) yield return found;
    }

    static string? Installed(string ownFolder, Action<string> log)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(ownFolder);
            bool IsOriginal(ModInfo m) =>
                m.Name == OriginalName && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase);

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
