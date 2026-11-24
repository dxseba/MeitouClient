using System.Globalization;
using Meitou.Localisation;
using Meitou.StackSort.Search;

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

    OriginalFiles(Dictionary<string, string> config, Dictionary<string, string> captions, List<string> problems) =>
        (_config, _captions, Problems) = (config, captions, problems);

    /// <summary>
    /// The cfg's ignored lines (<see cref="CfgProblems"/>), one text each.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// Reads the original's files beside <paramref name="folder"/>.
    /// </summary>
    public static OriginalFiles Load(string folder)
    {
        string? root = Path.GetDirectoryName(Path.GetFullPath(folder)) is { } parent
            ? Folders.Select(name => Path.Combine(parent, name)).FirstOrDefault(Directory.Exists)
            : null;
        if (root is null) return new([], new(English), []);

        string cfg = Path.Combine(root, "StackSort.cfg");
        var captions = new Dictionary<string, string>(English);
        foreach (var (key, value) in Pairs(Path.Combine(root, "lang", LanguageFile() + ".txt"))) captions[key] = value;
        return new(Pairs(cfg), captions, CfgProblems.Of(cfg));
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
    /// <c>log_level</c>, 0-2 (default 1): 0 errors only, 1 normal, 2 also each candidate layout.
    /// </summary>
    public int LogLevel => Number("log_level", 0, 2, 1);

    /// <summary>
    /// The player's notice of <paramref name="count"/> ignored cfg problems: the language file's <c>WARN_CONFIG</c> with <c>{n}</c> replaced.
    /// </summary>
    public string ConfigWarning(int count)
    {
        string n = count.ToString(CultureInfo.InvariantCulture);
        return _captions.TryGetValue("WARN_CONFIG", out var text)
            ? text.Replace("{n}", n, StringComparison.Ordinal)
            : $"StackSort: {n} line(s) of StackSort.cfg ignored (see the mod log)";
    }

    /// <summary>
    /// The search budgets (StackSort.md "Budgets"): <c>first_pass_restarts</c> / <c>first_pass_iters</c> (compiled 16 / 4000),
    /// <c>refine_restarts</c> / <c>refine_iters</c> / <c>refine_lahc_history</c> / <c>refine_plateau</c> (24 / 8000 / 200 / 2000) and
    /// <c>refine_item_threshold</c> (20); a key left out or at -1 keeps the compiled value. <c>num_workers</c> (4) threads refine the targets.
    /// </summary>
    public SweepBudget Budget
    {
        get
        {
            var first = SearchParams.FirstPass with
            {
                Restarts = Number("first_pass_restarts", 1, 256, 16), Iterations = Number("first_pass_iters", 100, 1_000_000, 4000),
            };
            var refine = first with
            {
                Restarts = Number("refine_restarts", 1, 256, 24), Iterations = Number("refine_iters", 100, 1_000_000, 8000),
                History = Number("refine_lahc_history", 1, 100_000, 200), Plateau = Number("refine_plateau", 1, 1_000_000, 2000),
            };
            return new(first, refine, Number("refine_item_threshold", 1, 1000, 20), Workers);
        }
    }

    /// <summary>
    /// The synchronous budget (<c>sync_restarts</c> 4, <c>sync_iters</c> 1000): every arrange other than the strip's, which has no precomputed
    /// result to wait for, is searched with it and no refinement.
    /// </summary>
    public SweepBudget SyncBudget =>
        new(SearchParams.FirstPass with { Restarts = Number("sync_restarts", 1, 8, 4), Iterations = Number("sync_iters", 100, 2000, 1000) },
            null, Workers: Workers);

    /// <summary>
    /// <c>num_workers</c>, 1-8 (default 4): background sweeps at once, and threads per refinement.
    /// </summary>
    public int Workers => Number("num_workers", 1, 8, 4);

    /// <summary>
    /// <c>mutation_debounce_ms</c>, 0-5000 (default 150): how long an open window's grid must stay unchanged before its precompute starts.
    /// </summary>
    public int DebounceMs => Number("mutation_debounce_ms", 0, 5000, 150);

    /// <summary>
    /// <c>resnapshots_per_frame</c>, 1-16 (default 3): precomputes started per simulation step at most.
    /// </summary>
    public int ResnapshotsPerStep => Number("resnapshots_per_frame", 1, 16, 3);

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
