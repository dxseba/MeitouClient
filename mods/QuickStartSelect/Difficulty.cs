using System.Numerics;

namespace Meitou.QuickStartSelect;

/// <summary>
/// A start's difficulty as a rank (10 very easy to 70 hardest) read from its free-text <c>difficulty</c> by words, English and Russian, and its
/// colour (the original's word table and colour steps, docs in the README's Facts).
/// </summary>
static class Difficulty
{
    /// <summary>The rank of a difficulty no word matches.</summary>
    public const int Unknown = 30;

    /// <summary>The rank of each custom level (very easy to very hard).</summary>
    static readonly int[] LevelRanks = [10, 20, 30, 40, 50, 60];

    /// <summary>The words in the original's order: the first one found in the text gives the rank.</summary>
    static readonly (string Word, int Rank)[] Words =
    [
        ("default", 30), ("обычн", 30), ("very easy", 10), ("очень лег", 10), ("easy/hard", 38), ("normal/hard", 38), ("смешан", 38),
        ("двое", 38), ("hardest", 70), ("very hard", 60), ("очень тяж", 60), ("выше средн", 40), ("harder", 40), ("посложнее", 40),
        ("сложнее", 40), ("dodgy", 45), ("щекотл", 45), ("slightly", 35), ("kinda", 35), ("easier", 15), ("полегче", 15), ("easy", 20),
        ("лег", 20), ("hard", 50), ("тяж", 50), ("жестк", 50), ("normal", 30), ("doable", 30), ("умерен", 30), ("средн", 30), ("нормал", 30),
    ];

    /// <summary>
    /// The rank of <paramref name="difficulty"/>: a custom level whose list holds a part of it first, then the word table, else
    /// <see cref="Unknown"/>.
    /// </summary>
    public static int Rank(string difficulty, string[] customLevels)
    {
        string text = Normalise(difficulty);
        if (text.Length == 0) return Unknown;

        if (CustomRank(text, customLevels) is { } custom) return custom;

        foreach (var (word, rank) in Words)
        {
            if (text.Contains(word, StringComparison.Ordinal)) return rank;
        }

        return Unknown;
    }

    /// <summary>The rank of the first custom level one of whose comma-separated words the text holds; null for none.</summary>
    static int? CustomRank(string text, string[] customLevels)
    {
        for (int level = 0; level < customLevels.Length && level < LevelRanks.Length; level++)
        {
            foreach (string part in customLevels[level].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (text.Contains(Normalise(part), StringComparison.Ordinal)) return LevelRanks[level];
            }
        }

        return null;
    }

    /// <summary>Lower case, ё as е (the original's search and word match ignore both).</summary>
    public static string Normalise(string text) => text.Trim().ToLowerInvariant().Replace('ё', 'е');

    /// <summary>
    /// The rank's colour: six steps from green to red; on dark text (the vanilla parchment) each channel is scaled by 0.55, as the original does.
    /// </summary>
    public static Vector4 Colour(int rank, bool darkText)
    {
        var c = rank switch
        {
            < 16 => new Vector3(.55f, .74f, .45f),
            < 26 => new Vector3(.68f, .76f, .47f),
            < 33 => new Vector3(.80f, .74f, .50f),
            < 46 => new Vector3(.85f, .62f, .40f),
            < 56 => new Vector3(.85f, .48f, .36f),
            _ => new Vector3(.86f, .34f, .32f),
        };
        if (darkText) c *= .55f;
        return new(c, 1);
    }
}
