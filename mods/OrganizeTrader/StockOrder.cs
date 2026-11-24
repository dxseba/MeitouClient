using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.OrganizeTrader;

/// <summary>
/// The trade window's controls ask for the stock to be laid out in <paramref name="Order"/> (stock indices as <c>TradeItemView.Index</c> numbers
/// them: the shown items in their order, then the hidden ones largest first). Sent just before the game's own <c>ArrangeStock</c>; a paused
/// command, as the game's ARRANGE is.
/// </summary>
public sealed record OrganizeStock(int[] Order) : SimCommand, IPausedCommand;

/// <summary>
/// Moves the stock's items as the original does: placed before the trade system (<c>SystemAnchor.Before(StandardParts.Trade)</c>), its inputs
/// keep the step's <see cref="OrganizeStock"/>, and the arrange the trade system then runs for <c>ArrangeStock</c> (the stock's ARRANGE,
/// <see cref="InventoryArrangeReason.Button"/>) is answered with the items packed first-fit, row by row, in that order. An order that no
/// longer fits the grid keeps every item where it lies. Everything it decides comes from the command and the world, so runs repeat; the kept
/// order lives only until the next inputs step and is not hashed.
/// </summary>
sealed class StockOrderSystem : IPausedCommandHandler, IInventoryArrangeFilter
{
    int[]? _order;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _order = null;
        foreach (var command in commands)
            if (command is OrganizeStock organize) _order = organize.Order;
    }

    public InventoryArrangement? Arrange(World world, in InventoryArrangeAsk ask)
    {
        if (_order is not { } order || ask.Owner.Holder != ItemHolder.Stock || ask.Reason != InventoryArrangeReason.Button) return null;

        _order = null;
        var items = Indexed(ask.Root);
        var grids = ask.Grids.ToList();
        var placements = new List<ItemPlacement>();
        foreach (var grid in grids)
        {
            var own = grid.Items.ToList();
            placements.AddRange(Pack(Ordered(own, order, items), grid.Grid.Width, grid.Grid.Height) ?? Kept(own));
        }

        return new InventoryArrangement(placements);
    }

    /// <summary>
    /// Each item of the root by its stock index (an item counts one, then its bag's contents, as the trade window numbers them).
    /// </summary>
    static Dictionary<ItemInstance, int> Indexed(Inventory root)
    {
        var index = new Dictionary<ItemInstance, int>(ReferenceEqualityComparer.Instance);
        int at = 0;
        foreach (var item in root.Items)
        {
            index[item] = at;
            at += 1 + (item.Contents?.All().Count() ?? 0);
        }

        return index;
    }

    /// <summary>
    /// A grid's items in the asked order, those it does not name after them in the grid's own order.
    /// </summary>
    static List<ItemInstance> Ordered(List<ItemInstance> own, int[] order, Dictionary<ItemInstance, int> index)
    {
        var rank = new Dictionary<int, int>(order.Length);
        for (int i = 0; i < order.Length; i++) rank.TryAdd(order[i], i);

        int Rank(ItemInstance item) => index.TryGetValue(item, out int at) && rank.TryGetValue(at, out int r) ? r : int.MaxValue;
        return [.. own.Select((item, i) => (item, i)).OrderBy(p => Rank(p.item)).ThenBy(p => p.i).Select(p => p.item)];
    }

    static IEnumerable<ItemPlacement> Kept(List<ItemInstance> own) => own.Select(i => new ItemPlacement(i, i.X, i.Y));

    /// <summary>
    /// Each item at the first free cell row by row where it fits as it lies; null when one does not fit.
    /// </summary>
    static List<ItemPlacement>? Pack(List<ItemInstance> items, int columns, int rows)
    {
        var grid = new Footprints(columns, rows);
        var placed = new List<ItemPlacement>(items.Count);
        foreach (var item in items)
        {
            if (grid.FirstFit(item.Width, item.Height) is not { } at) return null;

            grid.Take(at.X, at.Y, item.Width, item.Height);
            placed.Add(new ItemPlacement(item, at.X, at.Y));
        }

        return placed;
    }
}

/// <summary>
/// The taken cells of a grid, for first-fit packing row by row.
/// </summary>
sealed class Footprints(int columns, int rows)
{
    readonly bool[,] _taken = new bool[Math.Max(1, columns), Math.Max(1, rows)];

    public (int X, int Y)? FirstFit(int width, int height)
    {
        for (int y = 0; y + height <= rows; y++)
        {
            for (int x = 0; x + width <= columns; x++)
                if (Free(x, y, width, height)) return (x, y);
        }

        return null;
    }

    bool Free(int x, int y, int width, int height)
    {
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
                if (_taken[x + dx, y + dy]) return false;
        }

        return true;
    }

    public void Take(int x, int y, int width, int height)
    {
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++) _taken[x + dx, y + dy] = true;
        }
    }
}
