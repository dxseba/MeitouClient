using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>Which way a layout keeps its free space: Height packs the rows towards the top, Width the columns towards the left.</summary>
public enum Goal { Height, Width }

/// <summary>
/// Builds candidate layouts of an inventory's grids: each groups items of one record together, merges partial stacks through the auto-add, and
/// fills row by row (top first) or column by column (left first). The candidates are ranked for a <see cref="Goal"/>. Pure functions of the
/// inventory, so they stay deterministic.
/// </summary>
static class Packer
{
    /// <summary>How many layouts are offered (the original's five best).</summary>
    public const int Offered = 5;

    static readonly Func<ItemInstance, long>[] GroupKeys =
    [
        i => i.Width * (long)i.Height,
        i => i.Height,
        i => i.Width,
    ];

    public readonly record struct Grid(Inventory Inventory, string Section, ItemInstance[] Items);
    public readonly record struct ItemState(Grid Grid, ItemInstance[] Kept, (ItemInstance Item, int X, int Y, int Quantity, float Charges)[] Values);

    /// <summary>The best layout for <paramref name="goal"/> applied (true), or the inventory unchanged (false) when nothing fits.</summary>
    public static bool Pack(Inventory root, bool? bag = null, Goal goal = Goal.Height)
    {
        var grids = Grids(root, bag);
        var layouts = Layouts(grids, goal);
        if (layouts.Count == 0) return false;

        Restore(layouts[0]);
        return true;
    }

    /// <summary>The main grid and the worn bag's (<paramref name="bag"/> null), or only the bag's (true) or the main grid (false).</summary>
    public static List<Grid> Grids(Inventory root, bool? bag)
    {
        var grids = new List<Grid>();
        if (bag != true) grids.Add(new(root, "main", [.. root.Items]));
        if (bag != false && root.Backpack?.Contents is { } contents) grids.Add(new(contents, "backpack_content", [.. contents.Items]));
        return grids;
    }

    /// <summary>Up to <see cref="Offered"/> distinct layouts, best first for <paramref name="goal"/>; the grids are left as they were.</summary>
    public static List<ItemState[]> Layouts(List<Grid> grids, Goal goal)
    {
        var original = Snapshot(grids);
        var found = new List<(long Score, ItemState[] State, string Key)>();
        foreach (var key in GroupKeys)
        {
            foreach (bool columns in new[] { false, true })
            {
                if (TryLayout(grids, key, columns)) found.Add((Score(grids, goal), Snapshot(grids), Signature(grids)));
                Restore(original);
            }
        }

        return [.. found.DistinctBy(f => f.Key).OrderBy(f => f.Score).Take(Offered).Select(f => f.State)];
    }

    public static ItemState[] Snapshot(List<Grid> grids) =>
        [.. grids.Select(g => new ItemState(g, [.. g.Inventory.Items], [.. g.Items.Select(i => (i, i.X, i.Y, i.Quantity, i.Charges))]))];

    public static void Restore(ItemState[] state)
    {
        foreach (var s in state)
        {
            s.Grid.Inventory.Items.Clear();
            s.Grid.Inventory.Items.AddRange(s.Kept);
            foreach (var v in s.Values) (v.Item.X, v.Item.Y, v.Item.Quantity, v.Item.Charges) = (v.X, v.Y, v.Quantity, v.Charges);
        }
    }

    /// <summary>A layout's place in the grids, to tell layouts apart and to notice an inventory changed since.</summary>
    public static string Signature(List<Grid> grids) =>
        string.Join(";", grids.SelectMany(g => g.Inventory.Items.Where(i => i.Section == g.Section).Select(i => $"{i.Record}@{i.X},{i.Y}x{i.Quantity}")));

    /// <summary>Groups by record; the groups by the largest key of their items, then record id; items in a group by the key, then quantity.</summary>
    static bool TryLayout(List<Grid> grids, Func<ItemInstance, long> key, bool columns)
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

            if (columns && !ByColumns(grid)) return false;
        }
        return true;
    }

    /// <summary>Lays the merged items out again column by column, left first, in their row-filled order.</summary>
    static bool ByColumns(Grid grid)
    {
        var inventory = grid.Inventory;
        var placed = inventory.Items.Where(i => i.Section == grid.Section).OrderBy(i => i.Y).ThenBy(i => i.X).ToList();
        inventory.Items.RemoveAll(i => i.Section == grid.Section);
        foreach (var item in placed)
        {
            if (!PlaceInColumns(inventory, grid.Section, item)) return false;
        }
        return true;
    }

    static bool PlaceInColumns(Inventory inventory, string section, ItemInstance item)
    {
        for (int x = 0; x + item.Width <= inventory.Width; x++)
        {
            for (int y = 0; y + item.Height <= inventory.Height; y++)
            {
                if (!inventory.IsFree(section, x, y, item.Width, item.Height)) continue;

                (item.X, item.Y, item.Section) = (x, y, section);
                inventory.Items.Add(item);
                return true;
            }
        }
        return false;
    }

    /// <summary>Lower is better: the used rows (Height) or used columns (Width), the other as a tie-break.</summary>
    static long Score(List<Grid> grids, Goal goal)
    {
        long rows = grids.Sum(g => Used(g, i => i.Y + i.Height));
        long columns = grids.Sum(g => Used(g, i => i.X + i.Width));
        return goal == Goal.Height ? rows * 1000 + columns : columns * 1000 + rows;
    }

    static int Used(Grid g, Func<ItemInstance, int> end) =>
        g.Inventory.Items.Where(i => i.Section == g.Section).Select(end).DefaultIfEmpty(0).Max();
}
