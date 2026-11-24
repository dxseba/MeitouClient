using System.Globalization;
using System.Text.RegularExpressions;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.PoshDamageNumbers;

/// <summary>One glyph's rectangle in the atlas and how far the next glyph moves back over it (pixels of the atlas).</summary>
readonly record struct Glyph(int X, int Y, int W, int H, int Overlap);

/// <summary>
/// The original's number and word images, read at start from the installed original (Workshop item 3821781631, the port's required item):
/// <c>gui/gfx/&lt;texture&gt;</c> and the rectangles of <c>scripts/dn_glyphs.lua</c> (a generated Lua table, one <c>["key"] = { x, y, w, h,
/// overlap }</c> line a glyph). The reader takes only that shape and checks every glyph the numbers and words need lies inside the stated
/// texture size; anything else leaves the port on its text labels. Nothing of the original is shipped with the port.
/// </summary>
sealed partial class Atlas
{
    const string OriginalItem = "3821781631";
    const string OriginalName = "DamageNumbers";

    /// <summary>The keys the port draws: the digits, the signs and the words in Russian and English.</summary>
    static readonly string[] Needed =
    [
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "+", "-", "KO", "DEAD", "LIMB", "KO_EN", "DEAD_EN", "LIMB_EN",
    ];

    Atlas(string image, IReadOnlyDictionary<string, Glyph> glyphs)
    {
        Image = image;
        Glyphs = glyphs;
        DigitHeight = glyphs["0"].H;
    }

    /// <summary>The atlas picture's full path.</summary>
    public string Image { get; }

    /// <summary>The glyphs by key.</summary>
    public IReadOnlyDictionary<string, Glyph> Glyphs { get; }

    /// <summary>The height a number is scaled to (the original's <c>DIGIT_H</c>, the "0" glyph's height).</summary>
    public int DigitHeight { get; }

    /// <summary>The installed original's atlas, or null (logged) when it is missing or not in the expected shape.</summary>
    public static Atlas? Load(ModContext context)
    {
        if (OriginalFolder(context) is not { } folder)
        {
            context.Log($"the original mod (Workshop {OriginalItem}) is not installed: numbers drawn as text");
            return null;
        }

        try
        {
            return Read(folder, context.Log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"the original's number images not read ({e.Message}): numbers drawn as text");
            return null;
        }
    }

    static Atlas? Read(string folder, Action<string> log)
    {
        string table = Path.Combine(folder, "scripts", "dn_glyphs.lua");
        if (!File.Exists(table))
        {
            log("the original's dn_glyphs.lua is missing: numbers drawn as text");
            return null;
        }

        string text = File.ReadAllText(table);
        if (Header().Match(text) is not { Success: true } header)
        {
            log("the original's dn_glyphs.lua has no texture line: numbers drawn as text");
            return null;
        }

        return Build(folder, header, Rows(text), log);
    }

    static Atlas? Build(string folder, Match header, Dictionary<string, Glyph> glyphs, Action<string> log)
    {
        string name = header.Groups["texture"].Value;
        string image = Path.Combine(folder, "gui", "gfx", name);
        int width = Number(header, "width"), height = Number(header, "height");
        var bad = Needed.Where(k => !glyphs.TryGetValue(k, out var g) || !Inside(g, width, height)).ToList();
        if (bad.Count > 0 || !File.Exists(image) || name.Contains('/') || name.Contains('\\'))
        {
            log($"the original's number images are not as expected ({string.Join(", ", bad.DefaultIfEmpty(name))}): numbers drawn as text");
            return null;
        }

        log($"number images from {image} ({glyphs.Count} glyphs)");
        return new(image, glyphs);
    }

    static Dictionary<string, Glyph> Rows(string text)
    {
        var glyphs = new Dictionary<string, Glyph>(StringComparer.Ordinal);
        foreach (Match row in Row().Matches(text))
        {
            glyphs[row.Groups["key"].Value] = new(Number(row, "x"), Number(row, "y"), Number(row, "w"), Number(row, "h"), Number(row, "o"));
        }

        return glyphs;
    }

    static bool Inside(Glyph g, int width, int height) =>
        g.W > 0 && g.H > 0 && g.Overlap < g.W && g.X + g.W <= width && g.Y + g.H <= height;

    static int Number(Match m, string group) => int.Parse(m.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex("""texture\s*=\s*"(?<texture>[A-Za-z0-9_.\-]+\.png)"\s*,"""
        + """\s*width\s*=\s*(?<width>\d{1,5})\s*,\s*height\s*=\s*(?<height>\d{1,5})""")]
    private static partial Regex Header();

    [GeneratedRegex("""^\s*\["(?<key>[A-Za-z0-9_+\-]{1,16})"\]\s*=\s*\{\s*x\s*=\s*(?<x>\d{1,5})\s*,\s*y\s*=\s*(?<y>\d{1,5})\s*,"""
        + """\s*w\s*=\s*(?<w>\d{1,5})\s*,"""
        + """\s*h\s*=\s*(?<h>\d{1,5})\s*,\s*overlap\s*=\s*(?<o>\d{1,5})\s*\}\s*,?\s*$""", RegexOptions.Multiline)]
    private static partial Regex Row();

    /// <summary>
    /// The original's folder: its Workshop folder beside this mod's, else the code-less mod named "DamageNumbers"; null when absent.
    /// </summary>
    static string? OriginalFolder(ModContext context)
    {
        string? beside = Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent ? Path.Combine(parent, OriginalItem) : null;
        if (beside is not null && Directory.Exists(beside)) return beside;

        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            return ModList.Discover(install).FirstOrDefault(m => m.Name == OriginalName && !m.HasCode)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
