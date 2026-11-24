using System.Globalization;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// The overview's column widths from the installed original's <c>PersistentPrisoners.languages.ini</c> (Workshop 3798215878, the port's required
/// item; its <c>.local.ini</c> beside it wins, as in the original): the <c>[en]</c> section's <c>width.name</c> ... <c>width.status</c>, shares in
/// percent of the list's width (**Observed**, 0.2.8's file: 18, 19, 15, 5, 13, 18, 12, summing to 100). Read at start, never shipped; a missing
/// file or an incomplete or out-of-range set leaves the port's own widths.
/// </summary>
static class OriginalLayout
{
    const string OriginalItem = "3798215878";
    const string OriginalName = "PersistentPrisoners";
    const string FileName = "PersistentPrisoners.languages.ini";
    const string LocalName = "PersistentPrisoners.languages.local.ini";

    /// <summary>The ini's column keys, in the window's column order.</summary>
    static readonly string[] Keys = ["name", "faction", "cage", "nu", "condition", "location", "status"];

    /// <summary>The seven shares (summing to at most 100), or null.</summary>
    public static float[]? Widths(ModContext context)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string folder in Folders(context))
        {
            Read(Path.Combine(folder, FileName), values);
            Read(Path.Combine(folder, LocalName), values);
            if (values.Count > 0) break;
        }

        var widths = Keys.Select(k => values.TryGetValue("width." + k, out string? v) && float.TryParse(v, CultureInfo.InvariantCulture, out float f)
            ? f : float.NaN).ToArray();
        bool valid = widths.All(w => w is >= 1 and <= 100) && widths.Sum() <= 100.5f;
        context.Log(valid ? "column widths from the original's languages.ini" : "original's languages.ini not found or incomplete: own column widths");
        return valid ? widths : null;
    }

    /// <summary>The <c>[en]</c> section's <c>key=value</c> lines of <paramref name="path"/> into <paramref name="values"/> (later files win).</summary>
    static void Read(string path, Dictionary<string, string> values)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 1 << 20) return;

        bool english = false;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim().TrimStart((char)0xFEFF);
            if (line.StartsWith('[')) english = line == "[en]";
            else if (english && line.IndexOf('=') is > 0 and var split) values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }
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
