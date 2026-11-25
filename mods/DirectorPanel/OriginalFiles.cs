using System.Text;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.DirectorPanel;

/// <summary>
/// The installed original (Workshop 3805040918, the port's required item): its folder beside this one, else the code-less mod named "Lammy's
/// Director Panel". Its Russian texts and its saved points are read from there at runtime; nothing of the original ships with the port.
/// </summary>
internal static class OriginalFiles
{
    internal const string OriginalItem = "3805040918";
    const string OriginalName = "Lammy's Director Panel";
    const string DllName = "DirectorPanel.dll";
    internal const string PointsName = "DirectorPanel_points.txt";

    /// <summary>The original's folder; null when it is not installed.</summary>
    public static string? Folder(ModContext context)
    {
        string? beside = Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent ? Path.Combine(parent, OriginalItem) : null;
        if (beside is not null && File.Exists(Path.Combine(beside, DllName))) return beside;

        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(context.Folder);
            return ModList.Discover(install)
                .FirstOrDefault(m => m.Name == OriginalName && !m.HasCode
                    && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase))?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The original's Russian texts by their English text. Each panel text is an English string followed in the DLL's read-only data by its
    /// Russian one (README "Facts"), so every NUL-terminated ASCII string whose next string holds Cyrillic letters makes a pair. Empty without
    /// the original.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RussianTexts(ModContext context)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Folder(context) is not { } folder) return texts;

        try
        {
            Pair(Strings(File.ReadAllBytes(Path.Combine(folder, DllName))), texts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"original texts not read ({e.Message}): the panel stays English");
        }

        return texts;
    }

    static void Pair(List<string> strings, Dictionary<string, string> texts)
    {
        for (int i = 0; i + 1 < strings.Count; i++)
        {
            string en = strings[i], ru = strings[i + 1];
            if (en.Length < 2 || !en.All(c => c is >= ' ' and <= '~') || !ru.Any(c => c is >= 'Ѐ' and <= 'ӿ')) continue;

            texts.TryAdd(en, ru);
        }
    }

    /// <summary>The file's NUL-terminated runs of valid UTF-8 text (no control characters), in file order.</summary>
    static List<string> Strings(byte[] bytes)
    {
        var strings = new List<string>();
        var utf8 = new UTF8Encoding(false, true);
        int start = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0) continue;

            if (i - start >= 2 && Text(utf8, bytes, start, i - start) is { } s) strings.Add(s);
            start = i + 1;
        }

        return strings;
    }

    static string? Text(UTF8Encoding utf8, byte[] bytes, int start, int length)
    {
        for (int k = start; k < start + length; k++)
        {
            if (bytes[k] < 0x20) return null;
        }

        try
        {
            return utf8.GetString(bytes, start, length);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
