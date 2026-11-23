using System.Globalization;
using Meitou.Localisation;

namespace Meitou.StackSort;

/// <summary>
/// The installed original's own files (a required item, a sibling of this mod's folder by Workshop id or mod name): StackSort.cfg, whose
/// <c>default_dim</c>, <c>consolidate_stacks</c> and <c>top_targets</c> become the settings' defaults, and its <c>lang\&lt;code&gt;.txt</c>
/// captions. Missing files keep the original's built-in values.
/// </summary>
sealed class OriginalFiles
{
    /// <summary>
    /// The original's Workshop id and folder name.
    /// </summary>
    static readonly string[] Folders = ["3813810734", "StackSort"];

    /// <summary>
    /// The original's built-in English captions.
    /// </summary>
    static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["SORT_BUTTON"] = "StackSort [{n}]", ["DIM_H"] = "H", ["DIM_W"] = "W",
    };

    /// <summary>
    /// The cfg's values by key (inline <c>#</c> comments cut).
    /// </summary>
    readonly Dictionary<string, string> _config;

    /// <summary>
    /// The captions by key.
    /// </summary>
    readonly Dictionary<string, string> _captions;

    OriginalFiles(Dictionary<string, string> config, Dictionary<string, string> captions) => (_config, _captions) = (config, captions);

    /// <summary>
    /// Reads the original's files beside <paramref name="folder"/>.
    /// </summary>
    public static OriginalFiles Load(string folder)
    {
        string? root = Path.GetDirectoryName(Path.GetFullPath(folder)) is { } parent
            ? Folders.Select(name => Path.Combine(parent, name)).FirstOrDefault(Directory.Exists)
            : null;
        if (root is null) return new([], new(English));

        var config = Pairs(Path.Combine(root, "StackSort.cfg"));
        var captions = new Dictionary<string, string>(English);
        foreach (var (key, value) in Pairs(Path.Combine(root, "lang", LanguageFile() + ".txt"))) captions[key] = value;
        return new(config, captions);
    }

    /// <summary>
    /// The original's language file for the game's language: zh_CN / zh_TW (zh_SG with CN, zh_HK with TW), else the code's language part.
    /// </summary>
    static string LanguageFile()
    {
        string code = Loc.Language.Code;
        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return code.EndsWith("TW", StringComparison.OrdinalIgnoreCase) || code.EndsWith("HK", StringComparison.OrdinalIgnoreCase) ? "zh_TW" : "zh_CN";

        return code.Split('_', '-')[0].ToLowerInvariant();
    }

    /// <summary>
    /// A file's <c>key=value</c> lines (comments and blanks skipped); empty when it is not there.
    /// </summary>
    static Dictionary<string, string> Pairs(string path)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return pairs;

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Split('#')[0].Trim();
            int at = line.IndexOf('=');
            if (at > 0) pairs[line[..at].Trim()] = line[(at + 1)..].Trim();
        }

        return pairs;
    }

    /// <summary>
    /// <c>default_dim</c>: W or width starts on the Width goal (default H).
    /// </summary>
    public bool WidthGoal => _config.GetValueOrDefault("default_dim")?.ToLowerInvariant() is "w" or "width";

    /// <summary>
    /// <c>consolidate_stacks</c> (default true).
    /// </summary>
    public bool Consolidate => !bool.TryParse(_config.GetValueOrDefault("consolidate_stacks"), out bool on) || on;

    /// <summary>
    /// <c>top_targets</c>, 1-10 (default 5).
    /// </summary>
    public int TopTargets => Number("top_targets", 1, 10, 5);

    /// <summary>
    /// The button strip's geometry in pixels: <c>arrow_button_width</c> (30), <c>dim_button_width</c> (40), <c>sort_button_width</c> (180),
    /// <c>button_gap</c> (2) and <c>body_x_offset</c> (-40), each in the cfg's range.
    /// </summary>
    public StripGeometry Strip => new(Number("arrow_button_width", 10, 200, 30), Number("dim_button_width", 10, 200, 40),
        Number("sort_button_width", 40, 600, 180), Number("button_gap", 0, 50, 2), Number("body_x_offset", -500, 500, -40));

    /// <summary>
    /// A whole number of the cfg in [<paramref name="min"/>, <paramref name="max"/>]; <paramref name="fallback"/> when absent or out of range
    /// (the original ignores such a value).
    /// </summary>
    int Number(string key, int min, int max, int fallback) =>
        int.TryParse(_config.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max
            ? n
            : fallback;

    /// <summary>
    /// The sort button's caption for layout <paramref name="n"/> (1-based).
    /// </summary>
    public string SortCaption(int n) => _captions["SORT_BUTTON"].Replace("{n}", n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// The goal button's caption.
    /// </summary>
    public string GoalCaption(Goal goal) => _captions[goal == Goal.Height ? "DIM_H" : "DIM_W"];

    /// <summary>
    /// The sort button's caption for layout <paramref name="n"/> (1-based) and the goal letter.
    /// </summary>
    public string Caption(int n, Goal goal) => $"{SortCaption(n)} {GoalCaption(goal)}";
}

/// <summary>
/// The original's button strip geometry (<see cref="OriginalFiles.Strip"/>), in pixels.
/// </summary>
readonly record struct StripGeometry(int Arrow, int Dim, int Sort, int Gap, int BodyOffset)
{
    /// <summary>
    /// The strip's width: two arrows, the sort and goal buttons and three gaps.
    /// </summary>
    public int Width => 2 * Arrow + Sort + Dim + 3 * Gap;
}
