using Meitou.Modding;

namespace Meitou.ForceAwakening;

/// <summary>
/// The original's texts (<c>translations.txt</c> and <c>engine_texts.txt</c> of the installed original; nothing is shipped): <c>@KEY|en|pt|ja|ru|zh</c>
/// for the interface and <c>=&lt;record&gt;|&lt;field&gt;|en|pt|ja|ru|zh</c> for its records, whose own names in its <c>.mod</c> are Portuguese. The
/// game's language picks the column (English for any other).
/// </summary>
static class ForceTexts
{
    static readonly Dictionary<string, string> Ui = new(StringComparer.Ordinal);

    /// <summary>The column of <paramref name="language"/> (a game locale code such as en_GB, pt_BR, ja_JP, ru_RU, zh_CN).</summary>
    public static int Column(string language) => (language.Length >= 2 ? language[..2].ToLowerInvariant() : "en") switch
    {
        "pt" => 2,
        "ja" => 3,
        "ru" => 4,
        "zh" => 5,
        _ => 1,
    };

    /// <summary>Loads the interface texts in the game's language.</summary>
    public static void Load(ModContext context)
    {
        int column = Column(context.Language);
        foreach (var f in Rows(context.Folder, context.Log))
        {
            if (f[0].StartsWith('@')) Ui[f[0][1..]] = Pick(f, column);
        }
    }

    /// <summary>The record texts (<c>=id|field|...</c>) in the game's language, for the data phase.</summary>
    public static IEnumerable<(string Record, string Field, string Text)> Records(string folder, string language, Action<string> log)
    {
        int column = Column(language);
        foreach (var f in Rows(folder, log))
        {
            if (f[0].StartsWith('=') && f.Length >= 3) yield return (f[0][1..], f[1], Pick(f, column + 1));
        }
    }

    /// <summary>The text of <paramref name="key"/>, its <c>{0}</c>.. filled; the key itself when the original's file is missing.</summary>
    public static string T(string key, params object[] args)
    {
        string text = Ui.TryGetValue(key, out var found) ? found : key;
        text = text.Replace("\\n", "\n", StringComparison.Ordinal);
        for (int i = 0; i < args.Length; i++) text = text.Replace("{" + i + "}", Convert.ToString(args[i], System.Globalization.CultureInfo.InvariantCulture));
        return text;
    }

    public static bool Has(string key) => Ui.ContainsKey(key);

    /// <summary>A spell's name: its <c>SPELL_&lt;N&gt;</c> text, else the cfg's own (Portuguese) name.</summary>
    public static string SpellName(Spell spell) => Has("SPELL_" + spell.Number) ? T("SPELL_" + spell.Number) : spell.Name;

    /// <summary>A spell's short bar caption: <c>SHORT_&lt;kind&gt;</c>, else its name.</summary>
    public static string Short(Spell spell) => Has("SHORT_" + spell.Kind) ? T("SHORT_" + spell.Kind) : SpellName(spell);

    public static string PathName(ForcePath path) => T("CLASS_" + ForceConfig.KeyOf(path));

    static string Pick(string[] f, int column) => column < f.Length && f[column].Length > 0 ? f[column] : f.Length > 1 ? f[1] : "";

    static IEnumerable<string[]> Rows(string folder, Action<string> log)
    {
        foreach (string name in new[] { "engine_texts.txt", "translations.txt" })
        {
            if (OriginalFiles.Find(folder, name, log) is not { } path) continue;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log($"{name} not read: {e.Message}");
                continue;
            }

            foreach (string line in lines)
            {
                if (line.Length > 1 && (line[0] == '@' || line[0] == '=')) yield return line.Split('|');
            }
        }
    }
}
