using System.Numerics;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// The search state, kept on the interface thread: the query, its results, the one chosen last (Next steps through them) and whether the window
/// is folded.
/// </summary>
static class Search
{
    /// <summary>
    /// The longest query the box takes.
    /// </summary>
    public const int MaxLength = 40;

    /// <summary>
    /// The most results listed (the original's list stops at 80).
    /// </summary>
    public const int MaxResults = 80;

    /// <summary>
    /// The interface seen in the last frame.
    /// </summary>
    static object? s_player;

    /// <summary>
    /// The query as typed.
    /// </summary>
    public static string Query { get; private set; } = "";

    /// <summary>
    /// The listed matches of the query in the build window's order.
    /// </summary>
    public static IReadOnlyList<SearchEntry> Results { get; private set; } = [];

    /// <summary>
    /// The index in <see cref="Results"/> chosen last; -1 none.
    /// </summary>
    public static int Chosen { get; private set; } = -1;

    /// <summary>
    /// Whether the build window is open.
    /// </summary>
    public static bool BuildOpen { get; private set; }

    /// <summary>
    /// Whether the window is folded to its SEARCH button.
    /// </summary>
    public static bool Collapsed { get; set; }

    /// <summary>
    /// The build window's top-left in the last frame, when read.
    /// </summary>
    public static Vector2? BuildOrigin { get; private set; }

    /// <summary>
    /// The frame's interface and build window state; closing the window clears the search.
    /// </summary>
    public static void Frame(object player, bool open)
    {
        s_player = player;
        if (!open && BuildOpen) Clear();

        BuildOpen = open;
        BuildOrigin = open ? BuildAccess.BuildWindowOrigin(player) : null;
    }

    /// <summary>
    /// A new query: the results again, none chosen.
    /// </summary>
    public static void Set(string query)
    {
        Query = query.Length > MaxLength ? query[..MaxLength] : query;
        Chosen = -1;
        Refresh();
    }

    /// <summary>
    /// The clear button: no query, no results.
    /// </summary>
    public static void Clear()
    {
        Query = "";
        Results = [];
        Chosen = -1;
    }

    /// <summary>
    /// The results of the query against the build window's lists as they are now.
    /// </summary>
    static void Refresh()
    {
        string q = Query.Trim();
        Results = q.Length == 0 || s_player is null ? [] : [.. BuildAccess.Entries(s_player).Where(e => e.Matches(q)).Take(MaxResults)];
    }

    /// <summary>
    /// Chooses result <paramref name="index"/> in the build window.
    /// </summary>
    public static void Choose(int index)
    {
        if (s_player is null || index < 0 || index >= Results.Count) return;

        Chosen = index;
        BuildAccess.Choose(s_player, Results[index]);
    }

    /// <summary>
    /// Next: the result after the one chosen last, wrapping round.
    /// </summary>
    public static void Next()
    {
        if (Results.Count > 0) Choose((Chosen + 1) % Results.Count);
    }

    /// <summary>
    /// The status line: "chosen/count" once one is chosen, else the count; empty without a query.
    /// </summary>
    public static string Status => Query.Trim().Length == 0 ? "" : Chosen >= 0 ? $"{Chosen + 1}/{Results.Count}" : $"{Results.Count}";
}
