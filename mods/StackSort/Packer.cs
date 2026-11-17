using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>
/// Tries several orders of the grid's items, each grouping items of one record together, re-adds them through the auto-add (which fills from the top
/// left and merges partial stacks) and keeps the layout whose used rows end highest. Pure function of the inventory, so it stays deterministic.
/// </summary>
static class Packer
{
    static readonly Func<ItemInstance, long>[] GroupKeys =
    [
        i => i.Width * (long)i.Height,
        i => i.Height,
        i => i.Width,
    ];

    /// <summary>Packs the main grid and the worn bag's (<paramref name="bag"/> null), or only the bag's (true) or the main grid (false).</summary>
    public static bool Pack(Inventory root, bool? bag = null)
    {
        var grids = Grids(root, bag);
        var original = Snapshot(grids);
        (int Rows, ItemState[] State)? best = null;

        foreach (var key in GroupKeys)
        {
            if (TryLayout(grids, key) && UsedRows(grids) is var rows && (best is null || rows < best.Value.Rows))
                best = (rows, Snapshot(grids));
            Restore(grids, original);
        }

        if (best is null) return false;
        Restore(grids, best.Value.State);
        return true;
    }

    readonly record struct Grid(Inventory Inventory, string Section, ItemInstance[] Items);
    readonly record struct ItemState(Grid Grid, ItemInstance[] Kept, (ItemInstance Item, int X, int Y, int Quantity, float Charges)[] Values);

    static List<Grid> Grids(Inventory root, bool? bag)
    {
        var grids = new List<Grid>();
        if (bag != true) grids.Add(new(root, "main", [.. root.Items]));
        if (bag != false && root.Backpack?.Contents is { } contents) grids.Add(new(contents, "backpack_content", [.. contents.Items]));
        return grids;
    }

    static ItemState[] Snapshot(List<Grid> grids) =>
        [.. grids.Select(g => new ItemState(g, [.. g.Inventory.Items], [.. g.Items.Select(i => (i, i.X, i.Y, i.Quantity, i.Charges))]))];

    static void Restore(List<Grid> grids, ItemState[] state)
    {
        foreach (var s in state)
        {
            s.Grid.Inventory.Items.Clear();
            s.Grid.Inventory.Items.AddRange(s.Kept);
            foreach (var v in s.Values) (v.Item.X, v.Item.Y, v.Item.Quantity, v.Item.Charges) = (v.X, v.Y, v.Quantity, v.Charges);
        }
    }

    /// <summary>Groups by record; the groups by the largest key of their items, then record id; items in a group by the key, then quantity.</summary>
    static bool TryLayout(List<Grid> grids, Func<ItemInstance, long> key)
    {
        foreach (var grid in grids)
        {
            var items = grid.Items.Where(i => i.Section == grid.Section)
                .GroupBy(i => i.Record, StringComparer.Ordinal)
                .OrderByDescending(g => g.Max(key)).ThenBy(g => g.Key, StringComparer.Ordinal)
                .SelectMany(g => g.OrderByDescending(key).ThenByDescending(i => i.Quantity))
                .ToArray();

            grid.Inventory.Items.RemoveAll(i => i.Section == grid.Section);
            foreach (var item in items)
            {
                if (item.Width <= 0 || item.Height <= 0 || !grid.Inventory.AddTo(grid.Section, item)) return false;
            }
        }
        return true;
    }

    static int UsedRows(List<Grid> grids) =>
        grids.Sum(g => g.Inventory.Items.Where(i => i.Section == g.Section).Select(i => i.Y + i.Height).DefaultIfEmpty(0).Max());
}
