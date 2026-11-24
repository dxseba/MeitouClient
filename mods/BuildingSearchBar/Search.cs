using System.Numerics;
using Meitou.Modding;

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
    /// The mod's interface actions (the build window's entries and choice); set at start.
    /// </summary>
    static ModInterface? s_ui;

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
    /// Binds the mod's interface actions.
    /// </summary>
    public static void Bind(ModInterface ui) => s_ui = ui;

    /// <summary>
    /// Once a frame on the host's thread: whether the build window is open and where; closing it clears the search.
    /// </summary>
    public static void Frame()
    {
        bool open = s_ui?.IsOpen(ModScreen.Build) ?? false;
        if (!open && BuildOpen) Clear();

        BuildOpen = open;
        BuildOrigin = open && s_ui!.ScreenBounds(ModScreen.Build) is { } bounds ? new Vector2(bounds.X, bounds.Y) : null;
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
        Results = q.Length == 0 || s_ui is null ? [] : [.. s_ui.BuildEntries().Select(e => new SearchEntry(e)).Where(e => e.Matches(q)).Take(MaxResults)];
    }

    /// <summary>
    /// Chooses result <paramref name="index"/> in the build window.
    /// </summary>
    public static void Choose(int index)
    {
        if (s_ui is null || index < 0 || index >= Results.Count) return;

        Chosen = index;
        s_ui.ChooseBuild(Results[index].Entry.Building);
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

/// <summary>
/// One building of the build window (<see cref="UiBuildEntry"/>) as a search result.
/// </summary>
sealed record SearchEntry(UiBuildEntry Entry)
{
    /// <summary>
    /// The result row's text, as the original's: the name, then the build window's category in brackets when it has one.
    /// </summary>
    public string Caption => Entry.Category.Length > 0 ? $"{Entry.Name} [{Entry.Category}]" : Entry.Name;

    /// <summary>
    /// Whether the query is in the name, group, category or string id, ignoring case.
    /// </summary>
    public bool Matches(string query) => Has(Entry.Name, query) || Has(Entry.Group, query) || Has(Entry.Category, query) || Has(Entry.Building, query);

    /// <summary>
    /// Whether <paramref name="text"/> contains the query, ignoring case.
    /// </summary>
    static bool Has(string text, string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
}
