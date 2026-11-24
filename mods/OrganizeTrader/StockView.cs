using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Construction;
using Meitou.Simulation.Trading;

namespace Meitou.OrganizeTrader;

/// <summary>
/// The original's sort presets, in its list's order (its log names <c>name</c>, <c>unit_price</c>, <c>stack_value</c>, <c>unit_weight</c>,
/// <c>stack_weight</c>, <c>value_weight</c>; <c>default</c> keeps the trader's own layout).
/// </summary>
enum SortMode
{
    Default,
    Name,
    UnitPrice,
    StackValue,
    UnitWeight,
    StackWeight,
    ValueWeight,
}

/// <summary>
/// How the stock grid is shown: the cell each shown item moves to (null: every item stays where it is), the items left out, and the counter's
/// numbers, and the order the items are moved to (<see cref="StockOrderSystem"/>).
/// </summary>
sealed record StockShown(IReadOnlyDictionary<int, (int FromX, int FromY, int X, int Y)>? Moves, IReadOnlySet<int> Hidden, int Visible, int Total,
    int Quantity, string? Skipped)
{
    /// <summary>
    /// Every stock index in the order the original lays the grid out in (empty: the trader's own layout).
    /// </summary>
    public int[] Order { get; init; } = [];

    public static StockShown Native(int total, int quantity) => new(null, new HashSet<int>(), total, total, quantity, null);
}

/// <summary>
/// The search and sort of the trade window's stock: the items whose name (a blueprint's also with its research's name and what it unlocks)
/// contains the query, <c>b:</c> before it keeping blueprints only, packed from the grid's top-left without gaps in the trader's order or the
/// chosen preset's, the rest left out. Worked out again only when the stock, the query or the sort changes.
/// </summary>
sealed class StockView(GameDatabase data)
{
    /// <summary>
    /// The presets' captions (the original's msgids), in <see cref="SortMode"/> order.
    /// </summary>
    public static readonly string[] Presets = ["Default", "By name", "Unit price", "Stack value", "Unit weight", "Stack weight", "Price / weight"];

    readonly Dictionary<string, string> _blueprintText = new(StringComparer.Ordinal);

    (long Signature, string Typed, SortMode Mode, bool Descending) _key;
    StockShown? _last;

    /// <summary>
    /// The stock as shown for <paramref name="typed"/> and the sort.
    /// </summary>
    public StockShown Of(TradeView trade, string typed, SortMode mode, bool descending)
    {
        var key = (Signature(trade), typed, mode, descending);
        if (_last is not null && key == _key) return _last;

        _key = key;
        _last = Work(trade, typed, mode, descending);
        return _last;
    }

    /// <summary>
    /// A number that changes with the stock's items, places, sizes and amounts.
    /// </summary>
    static long Signature(TradeView trade)
    {
        var hash = new HashCode();
        hash.Add(trade.Trader);
        hash.Add(trade.StockWidth);
        hash.Add(trade.StockHeight);
        foreach (var item in trade.Stock)
        {
            hash.Add(item.Index);
            hash.Add(item.Record);
            hash.Add(item.X);
            hash.Add(item.Y);
            hash.Add(item.Width);
            hash.Add(item.Height);
            hash.Add(item.Quantity);
        }

        return ((long)trade.Stock.Count << 32) ^ (uint)hash.ToHashCode();
    }

    StockShown Work(TradeView trade, string typed, SortMode mode, bool descending)
    {
        var (query, blueprintsOnly) = Query(typed);
        bool filtering = query.Length > 0 || blueprintsOnly;
        int quantity = trade.Stock.Sum(i => i.Quantity);
        if (!filtering && mode == SortMode.Default) return StockShown.Native(trade.Stock.Count, quantity);

        var shown = trade.Stock.Where(i => Matches(i, query, blueprintsOnly)).ToList();
        var kept = shown.Select(i => i.Index).ToHashSet();
        var rest = trade.Stock.Where(i => !kept.Contains(i.Index)).ToList();
        var ordered = Order(shown, mode, descending);
        var moves = Pack(ordered, Math.Max(1, trade.StockWidth), Math.Max(1, trade.StockHeight));
        return new(moves, rest.Select(i => i.Index).ToHashSet(), shown.Count, trade.Stock.Count, shown.Sum(i => i.Quantity),
            moves is null ? "grid_full" : null) { Order = FullOrder(ordered, rest) };
    }

    /// <summary>
    /// The normalized query and whether it starts with <c>b:</c> (blueprints only).
    /// </summary>
    static (string Query, bool BlueprintsOnly) Query(string typed)
    {
        string raw = typed.TrimStart();
        bool blueprints = raw.StartsWith("b:", StringComparison.OrdinalIgnoreCase);
        return (SearchText.Normalize(blueprints ? raw[2..] : raw), blueprints);
    }

    bool Matches(TradeItemView item, string query, bool blueprintsOnly)
    {
        string? blueprint = BlueprintText(item);
        if (blueprintsOnly && blueprint is null) return false;
        if (query.Length == 0) return true;

        return SearchText.Normalize(item.Name).Contains(query, StringComparison.Ordinal)
            || blueprint is not null && blueprint.Contains(query, StringComparison.Ordinal);
    }

    /// <summary>
    /// A blueprint's searchable text (its research's name and the names of what it unlocks, normalized); null for any other item.
    /// </summary>
    string? BlueprintText(TradeItemView item)
    {
        if (item.Company.Length == 0) return null;
        if (_blueprintText.TryGetValue(item.Company, out var known)) return known.Length == 0 ? null : known;

        string text = "";
        if (data.Find(item.Company) is { Type: FcsRecordType.Research } research)
        {
            var names = ResearchData.UnlockFields.SelectMany(research.GetReferences).Select(r => data.Find(r.TargetStringId)?.Name ?? "");
            text = SearchText.Normalize(string.Join(' ', names.Prepend(research.Name)));
            if (text.Length == 0) text = " ";
        }

        _blueprintText[item.Company] = text;
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The shown items in the trader's order (row by row) or the preset's, ties in the trader's order.
    /// </summary>
    static List<TradeItemView> Order(List<TradeItemView> items, SortMode mode, bool descending)
    {
        var byPlace = items.OrderBy(i => i.Y).ThenBy(i => i.X).ThenBy(i => i.Index).ToList();
        if (mode == SortMode.Default) return byPlace;

        if (mode == SortMode.Name)
        {
            var named = descending ? byPlace.OrderByDescending(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                : byPlace.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
            return [.. named];
        }

        return [.. descending ? byPlace.OrderByDescending(i => Metric(i, mode)) : byPlace.OrderBy(i => Metric(i, mode))];
    }

    /// <summary>
    /// The preset's number for an item: the unit or stack price the shop asks, the unit or stack weight, or the price per kilogram (an item
    /// weighing nothing counts as the dearest).
    /// </summary>
    static double Metric(TradeItemView item, SortMode mode)
    {
        int units = Math.Max(1, item.Quantity);
        return mode switch
        {
            SortMode.UnitPrice => item.UnitPrice,
            SortMode.StackValue => item.Price,
            SortMode.UnitWeight => item.Weight / units,
            SortMode.StackWeight => item.Weight,
            SortMode.ValueWeight => item.Weight > 0 ? item.Price / (double)item.Weight : double.MaxValue,
            _ => 0,
        };
    }

    /// <summary>
    /// Each item, in order, at the first free cell row by row where its footprint fits; null when one does not fit in the grid.
    /// </summary>
    static Dictionary<int, (int FromX, int FromY, int X, int Y)>? Pack(List<TradeItemView> items, int columns, int rows)
    {
        var grid = new Footprints(columns, rows);
        var moves = new Dictionary<int, (int, int, int, int)>(items.Count);
        foreach (var item in items)
        {
            if (grid.FirstFit(item.Width, item.Height) is not { } at) return null;

            grid.Take(at.X, at.Y, item.Width, item.Height);
            moves[item.Index] = (item.X, item.Y, at.X, at.Y);
        }

        return moves;
    }

    /// <summary>
    /// The stock's order for the original's physical layout: the shown items in their order, then the hidden ones largest first (ties in the
    /// trader's order).
    /// </summary>
    static int[] FullOrder(List<TradeItemView> shown, IEnumerable<TradeItemView> hidden) =>
    [
        .. shown.Select(i => i.Index),
        .. hidden.OrderByDescending(i => i.Width * i.Height).ThenBy(i => i.Y).ThenBy(i => i.X).ThenBy(i => i.Index).Select(i => i.Index),
    ];
}
