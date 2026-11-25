using Meitou.Content;
using Meitou.Modding;

namespace Meitou.CheatMenu;

/// <summary>
/// The original's own translations, read at start from the installed original's <c>CheatMenu.dll</c> (Workshop 3833747223) for the game's
/// language; nothing of the original ships with the port. Its texts are one table in <c>.data</c> (README "Facts"): rows of nine string pointers,
/// English first, then the eight languages in the order of the locale table just before it (<c>de_DE</c>, <c>es_ES</c>, <c>fr_FR</c>,
/// <c>ja_JP</c>, <c>ko_KR</c>, <c>pt_BR</c>, <c>ru_RU</c>, <c>zh_CN</c>). The reader finds the row whose English is the window title and walks
/// the rows while all nine are valid UTF-8 strings. The game language's texts are added in memory as the mod's translations.
/// </summary>
static class OriginalTexts
{
    const string OriginalItem = "3833747223";
    const string OriginalName = "CheatMenu";
    const string DllName = "CheatMenu.dll";
    const string FirstRow = "Cheat Menu (F8)";
    const int Columns = 9, Languages = 8, MaxText = 400;

    public static void Add(ModContext context)
    {
        if (context.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return;

        if (Locate(context) is not { } path)
        {
            context.Log($"the original {DllName} (Workshop {OriginalItem}) is not installed: texts stay English");
            return;
        }

        try
        {
            var texts = Read(path, context.Language);
            if (texts is null)
            {
                context.Log($"the original has no {context.Language} texts: texts stay English");
                return;
            }

            context.AddTranslations(context.Language, texts);
            context.Log($"original texts read: {texts.Count} for {context.Language}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context.Log($"original texts not read ({e.Message}): texts stay English");
        }
    }

    /// <summary>English -> the text of <paramref name="language"/>; null when the original has no such language.</summary>
    static Dictionary<string, string>? Read(string path, string language)
    {
        var pe = PeImage.Read(path);
        var data = pe.Section(".data") ?? throw new InvalidDataException("no .data section");
        int table = FindTable(pe, data);
        int column = LanguageColumn(pe, table, language);
        if (column < 0) return null;

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = table; row + Columns * 8 <= data.RawEnd; row += Columns * 8)
        {
            if (Row(pe, row) is not { } strings) break;

            texts.TryAdd(strings[0], strings[column]);
        }

        return texts;
    }

    /// <summary>The file offset of the first row: a pointer to <see cref="FirstRow"/> preceded by the eight locale pointers.</summary>
    static int FindTable(PeImage pe, PeSection data)
    {
        for (int o = data.RawOffset + Languages * 8; o + 8 <= data.RawEnd; o += 8)
        {
            if (pe.Utf8(pe.U64(o), MaxText) != FirstRow || Row(pe, o) is null) continue;

            if (Enumerable.Range(1, Languages).All(k => pe.Utf8(pe.U64(o - 8 * k), 8) is { Length: 5 })) return o;
        }

        throw new InvalidDataException("text table not found");
    }

    /// <summary>The column (1 to 8) of the game's language: its full code, else its language part; -1 when absent.</summary>
    static int LanguageColumn(PeImage pe, int table, string language)
    {
        var codes = Enumerable.Range(0, Languages).Select(k => pe.Utf8(pe.U64(table - 8 * (Languages - k)), 8) ?? "").ToList();
        int exact = codes.FindIndex(c => c.Equals(language, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) return exact + 1;

        string part = language.Split('_', '-')[0];
        int near = codes.FindIndex(c => c.Split('_')[0].Equals(part, StringComparison.OrdinalIgnoreCase));
        return near < 0 ? -1 : near + 1;
    }

    static string[]? Row(PeImage pe, int offset)
    {
        var row = new string[Columns];
        for (int i = 0; i < Columns; i++)
        {
            if (pe.Utf8(pe.U64(offset + 8 * i), MaxText) is not { } text) return null;

            row[i] = text;
        }

        return row;
    }

    static string? Locate(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent && File.Exists(Path.Combine(parent, OriginalItem, DllName)))
            return Path.Combine(parent, OriginalItem, DllName);

        return Installed(context) is { } folder && File.Exists(Path.Combine(folder, DllName)) ? Path.Combine(folder, DllName) : null;
    }

    /// <summary>The installed mod named "CheatMenu" without Meitou code (the original), not the port's own folder.</summary>
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
