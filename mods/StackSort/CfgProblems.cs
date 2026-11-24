using System.Globalization;

namespace Meitou.StackSort;

/// <summary>
/// The original's checks of <c>StackSort.cfg</c>: a section header, a line without <c>=</c>, an unknown key and a value that does not parse or
/// lies outside the key's range are each ignored as one problem (the original logs each and tells the player how many once a game runs).
/// </summary>
static class CfgProblems
{
    /// <summary>
    /// The whole-number keys and their ranges; the search budget keys also take -1 (the packer's compiled default).
    /// </summary>
    static readonly Dictionary<string, (int Min, int Max, bool Default)> Numbers = new(StringComparer.Ordinal)
    {
        ["log_level"] = (0, 2, false), ["num_workers"] = (1, 8, false), ["mutation_debounce_ms"] = (0, 5000, false),
        ["resnapshots_per_frame"] = (1, 16, false), ["top_targets"] = (1, 10, false), ["arrow_button_width"] = (10, 200, false),
        ["dim_button_width"] = (10, 200, false), ["sort_button_width"] = (40, 600, false), ["button_gap"] = (0, 50, false),
        ["body_x_offset"] = (-500, 500, false), ["first_pass_restarts"] = (1, 256, true), ["first_pass_iters"] = (100, 1000000, true),
        ["refine_restarts"] = (1, 256, true), ["refine_iters"] = (100, 1000000, true), ["refine_lahc_history"] = (1, 100000, true),
        ["refine_plateau"] = (1, 1000000, true), ["refine_item_threshold"] = (1, 1000, true), ["sync_restarts"] = (1, 8, false),
        ["sync_iters"] = (100, 2000, false),
    };

    /// <summary>
    /// The problems of the cfg at <paramref name="path"/>, one line each; none when it is not there.
    /// </summary>
    public static List<string> Of(string path)
    {
        var problems = new List<string>();
        if (!File.Exists(path)) return problems;

        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            if (Check(lines[i].Split('#')[0].Trim()) is { } problem) problems.Add($"StackSort.cfg line {i + 1}: {problem}");
        }

        return problems;
    }

    /// <summary>
    /// One line's problem, or null for a blank, a comment or a good setting.
    /// </summary>
    static string? Check(string line)
    {
        if (line.Length == 0) return null;
        if (line.StartsWith('[')) return $"section headers are not used, ignoring '{line}'";

        int at = line.IndexOf('=');
        if (at <= 0) return $"no '=' in line, ignoring '{line}'";

        string key = line[..at].Trim();
        string value = line[(at + 1)..].Trim();
        return key switch
        {
            "default_dim" => value.ToLowerInvariant() is "h" or "w" or "height" or "width" ? null : Bad(key, value),
            "consolidate_stacks" => value.ToLowerInvariant() is "true" or "false" ? null : Bad(key, value),
            _ when Numbers.TryGetValue(key, out var range) => InRange(value, range) ? null : Bad(key, value),
            _ => $"unknown key '{key}', ignoring",
        };
    }

    /// <summary>
    /// Whether a whole number lies in the key's range (or is -1 where that means the default).
    /// </summary>
    static bool InRange(string value, (int Min, int Max, bool Default) range)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return false;

        return n >= range.Min && n <= range.Max || range.Default && n == -1;
    }

    /// <summary>
    /// A value that does not parse or is out of range.
    /// </summary>
    static string Bad(string key, string value) => $"value '{value}' of '{key}' is not valid, ignoring";
}
