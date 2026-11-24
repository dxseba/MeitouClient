using System.Text.RegularExpressions;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The plugin's interface texts in its languages, read from the installed original's DLL as bytes (never loaded). The plugin keeps them as
/// tables of rows, one row a language: a pointer to its locale code (<c>en_GB</c>, <c>ja_JP</c>, ...) then one pointer per text, the same count
/// in every row, the rows back to back, English first. The reader finds each pointer to an <c>en_GB</c> string in the data sections, takes the
/// row length from the next pointer to a locale code, and accepts the table only when every row starts with a distinct locale code and every
/// text is valid UTF-8; anything else is skipped. Nothing of the original is shipped.
/// </summary>
static partial class PluginTexts
{
    const int MaxRow = 200, MaxRows = 32, MaxText = 2000;

    /// <summary>
    /// The English text to its <paramref name="language"/> text (the row of that locale code, else of its language part) of every table found;
    /// empty for English or a language the plugin does not have. The first table that gives an English text wins.
    /// </summary>
    public static Dictionary<string, string> For(PeImage pe, string language)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return map;

        foreach (ulong at in pe.FindDataPointers(p => pe.Utf8At(p, 8) == "en_GB"))
        {
            if (Table(pe, at) is not { } rows || Row(rows, language) is not { } row) continue;

            for (int i = 1; i < row.Length; i++)
            {
                if (rows[0][i].Length > 0 && row[i].Length > 0) map.TryAdd(rows[0][i], row[i]);
            }
        }

        return map;
    }

    /// <summary>The table whose first row starts at <paramref name="at"/>, or null when it is not one (or runs past the file).</summary>
    static List<string[]>? Table(PeImage pe, ulong at)
    {
        try
        {
            return Rows(pe, at);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    static List<string[]>? Rows(PeImage pe, ulong at)
    {
        int width = 1;
        while (width < MaxRow && Code(pe, Peek(pe, at + (ulong)(8 * width))) is null) width++;

        var rows = new List<string[]>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (ulong row = at; rows.Count < MaxRows && Code(pe, Peek(pe, row)) is { } code; row += (ulong)(8 * width))
        {
            if (!codes.Add(code) || Texts(pe, row, width) is not { } texts) return null;

            rows.Add(texts);
        }

        return width < MaxRow && rows.Count >= 2 ? rows : null;
    }

    /// <summary>The row's code and texts, or null when one is not valid text.</summary>
    static string[]? Texts(PeImage pe, ulong row, int width)
    {
        var texts = new string[width];
        for (int i = 0; i < width; i++)
        {
            if (pe.Utf8At(pe.UInt64At(row + (ulong)(8 * i)), MaxText) is not { } text) return null;

            texts[i] = text;
        }

        return texts;
    }

    static string[]? Row(List<string[]> rows, string language) =>
        rows.FirstOrDefault(r => string.Equals(r[0], language, StringComparison.OrdinalIgnoreCase))
        ?? rows.FirstOrDefault(r => string.Equals(r[0][..2], language.Split('_')[0], StringComparison.OrdinalIgnoreCase));

    /// <summary>The 64-bit value at <paramref name="va"/>, 0 past the file (the end of a table at a section's end).</summary>
    static ulong Peek(PeImage pe, ulong va)
    {
        try
        {
            return pe.UInt64At(va);
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    static string? Code(PeImage pe, ulong pointer) => pe.Utf8At(pointer, 8) is { } s && Locale().IsMatch(s) ? s : null;

    [GeneratedRegex("^[a-z]{2}_[A-Z]{2}$")]
    private static partial Regex Locale();
}
