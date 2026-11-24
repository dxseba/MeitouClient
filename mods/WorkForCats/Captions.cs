using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meitou.Modding;

namespace Meitou.WorkForCats;

/// <summary>
/// The window's captions in the game's language. The original picks Russian ones when the game's locale id is <c>ru</c>, <c>ru_RU</c> or
/// <c>ru-RU</c> (<b>Verified</b>, its <c>WorkForCats.cpp</c>), else English; it has no catalogue. The port does not carry the Russian texts: it
/// reads them at runtime from the installed original's shipped source (<c>Source/WorkForCats.cpp</c>, its required item), each English caption
/// paired with the Russian one of the same <c>russian ? ... : ...</c> choice, and adds them in memory as the mod's translations only when every
/// caption it shows is found. The button shows through them by itself; the composed rate line through <see cref="Of"/>. Set once in
/// <c>Initialize</c>; read from any thread.
/// </summary>
static partial class Captions
{
    public const string GetJob = "Get a job";
    public const string QuitJob = "Quit job";
    public const string PerHour = " cats/hour";
    public const string OfHour = "/60 min";
    public const string Waiting = " (waiting)";

    static readonly string[] Shown = [GetJob, QuitJob, PerHour, OfHour, Waiting];

    static ModContext? s_context;

    /// <summary>
    /// <paramref name="english"/> in the game's language through the mod's translations (itself when none was read).
    /// </summary>
    public static string Of(string english) => s_context?.Tr(english) ?? english;

    /// <summary>
    /// Reads the Russian captions when the game runs in Russian; logs why when they cannot be used.
    /// </summary>
    public static void Load(ModContext context)
    {
        s_context = context;
        if (!Russian(context.Language)) return;

        if (OriginalFolder.Find(context) is not { } folder) return;

        string file = Path.Combine(folder, "Source", "WorkForCats.cpp");
        try
        {
            var pairs = Pairs(File.ReadAllText(file, Encoding.UTF8));
            if (Shown.All(pairs.ContainsKey)) context.AddTranslations(context.Language, pairs);
            else context.Log($"the Russian captions were not all found in {file}: English is shown");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"the original's source not read ({e.Message}): English is shown");
        }
    }

    /// <summary>
    /// The original's test of the locale id.
    /// </summary>
    static bool Russian(string language) => language is "ru" or "ru_RU" or "ru-RU";

    /// <summary>
    /// English to Russian from each <c>russian ? L"ru" : L"en"</c> and <c>russian ? (hired ? L"ru1" : L"ru2") : (hired ? L"en1" : L"en2")</c>.
    /// </summary>
    static Dictionary<string, string> Pairs(string source)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Simple().Matches(source)) Add(pairs, m.Groups["en"].Value, m.Groups["ru"].Value);

        foreach (Match m in Nested().Matches(source))
        {
            Add(pairs, m.Groups["en1"].Value, m.Groups["ru1"].Value);
            Add(pairs, m.Groups["en2"].Value, m.Groups["ru2"].Value);
        }

        return pairs;
    }

    static void Add(Dictionary<string, string> pairs, string english, string russian)
    {
        if (Unescape(english) is { } en && Unescape(russian) is { Length: > 0 } ru) pairs.TryAdd(en, ru);
    }

    /// <summary>
    /// A C++ wide literal's text: <c>\uXXXX</c>, <c>\\</c> and <c>\"</c>; null for any other escape.
    /// </summary>
    static string? Unescape(string literal)
    {
        var text = new StringBuilder(literal.Length);
        for (int i = 0; i < literal.Length; i++)
        {
            if (literal[i] != '\\')
            {
                text.Append(literal[i]);
                continue;
            }

            if (i + 1 >= literal.Length) return null;

            char next = literal[++i];
            if (next is '\\' or '"') text.Append(next);
            else if (next == 'u' && i + 4 < literal.Length && int.TryParse(literal.AsSpan(i + 1, 4), NumberStyles.HexNumber, null, out int code))
            {
                text.Append((char)code);
                i += 4;
            }
            else return null;
        }

        return text.ToString();
    }

    [GeneratedRegex(@"russian\s*\?\s*L""(?<ru>(?:[^""\\]|\\.)*)""\s*:\s*L""(?<en>(?:[^""\\]|\\.)*)""")]
    private static partial Regex Simple();

    [GeneratedRegex(@"russian\s*\?\s*\(\s*hired\s*\?\s*L""(?<ru1>(?:[^""\\]|\\.)*)""\s*:\s*L""(?<ru2>(?:[^""\\]|\\.)*)""\s*\)\s*:\s*"
        + @"\(\s*hired\s*\?\s*L""(?<en1>(?:[^""\\]|\\.)*)""\s*:\s*L""(?<en2>(?:[^""\\]|\\.)*)""\s*\)")]
    private static partial Regex Nested();
}
