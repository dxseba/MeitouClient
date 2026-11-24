using Meitou.Data.Fcs;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;
using Meitou.StackSort.Search;

namespace Meitou.StackSort;

/// <summary>Which way a layout keeps its free space: Height in a block of rows at the bottom, Width in a block of columns at the right.</summary>
public enum Goal { Height, Width }

/// <summary>
/// The bridge between live grids and the pure search: copies a grid into a <see cref="PackProblem"/> (a read), merges stacks and applies a
/// chosen layout (the command's effects). Nothing here computes a layout on the live inventory.
/// </summary>
static class Packer
{
    /// <summary>
    /// A grid section of an inventory and its items in list order when taken.
    /// </summary>
    public readonly record struct Grid(Inventory Inventory, string Section, ItemInstance[] Items);

    /// <summary>
    /// The state of a grid's items, to undo a sort: the inventory's list and each item's cell, units and charges.
    /// </summary>
    public readonly record struct ItemState(Grid Grid, ItemInstance[] Kept, (ItemInstance Item, int X, int Y, int Quantity, float Charges)[] Values);

    /// <summary>The main grid and the worn bag's (<paramref name="bag"/> null), or only the bag's (true) or the main grid (false).</summary>
    public static List<Grid> Grids(Inventory root, bool? bag)
    {
        var grids = new List<Grid>();
        if (bag != true) grids.Add(Of(root, "main"));
        if (bag != false && root.Backpack?.Contents is { } contents) grids.Add(Of(contents, "backpack_content"));
        return grids;
    }

    public static Grid Of(Inventory inventory, string section) => new(inventory, section, [.. inventory.Items.Where(i => i.Section == section)]);

    /// <summary>
    /// A grid as a search problem: each item's footprint as it lies, its group (one per record, by first appearance), record type (-1 for
    /// plain items), item function (-1 for none) and flags (<see cref="ItemFacts"/>).
    /// </summary>
    public static PackProblem Problem(Grid grid, ItemFacts facts)
    {
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        var items = grid.Items.Select(i =>
            new PackItem(i.Width, i.Height, GroupOf(groups, i.Record), TypeOf(i), FunctionOf(i), facts.Flags(i.Record)));
        return new PackProblem(grid.Inventory.Width, grid.Inventory.Height, [.. items], [.. grid.Items.Select(i => i.Record)],
            [.. grid.Items.Select(i => i.Quantity)]);
    }

    static int GroupOf(Dictionary<string, int> groups, string record)
    {
        if (!groups.TryGetValue(record, out int group)) groups[record] = group = groups.Count;
        return group;
    }

    static int TypeOf(ItemInstance item) => item.Type == FcsRecordType.Item ? -1 : (int)item.Type;

    static int FunctionOf(ItemInstance item) => item.ItemFunction == 0 ? -1 : item.ItemFunction;

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

    /// <summary>
    /// The grids' items at their cells in <paramref name="layouts"/> (one per grid), in placement order (row, then column).
    /// </summary>
    public static List<ItemPlacement> Placements(List<Grid> grids, IReadOnlyList<PackLayout> layouts) =>
        [.. grids.SelectMany((g, n) => g.Items.Select((item, i) => new ItemPlacement(item, layouts[n].X[i], layouts[n].Y[i])))
            .OrderBy(p => p.Y).ThenBy(p => p.X)];

    /// <summary>
    /// Moves the grids' items to <paramref name="layouts"/> and lists each grid's items in placement order, after its other sections' items.
    /// </summary>
    public static void Apply(List<Grid> grids, IReadOnlyList<PackLayout> layouts)
    {
        for (int n = 0; n < grids.Count; n++)
        {
            var (inventory, section, items) = grids[n];
            var layout = layouts[n];
            inventory.Items.RemoveAll(i => i.Section == section);
            foreach (int i in Enumerable.Range(0, items.Length).OrderBy(i => layout.Y[i]).ThenBy(i => layout.X[i]))
            {
                (items[i].X, items[i].Y) = (layout.X[i], layout.Y[i]);
                inventory.Items.Add(items[i]);
            }
        }
    }

    /// <summary>
    /// Merges the grids' partial stacks before the first sort (<c>consolidate_stacks</c>, the command's own effect): each grid's items in list
    /// order through the inventory's auto-add, which tops up earlier stacks and puts what is left on a free cell. When an item finds no room
    /// the grids are put back unmerged. Returns the grids as they are now.
    /// </summary>
    public static List<Grid> Merge(Inventory root, bool bag)
    {
        var grids = Grids(root, bag);
        var before = Snapshot(grids);
        foreach (var (inventory, section, items) in grids)
        {
            inventory.Items.RemoveAll(i => i.Section == section);
            if (items.Any(item => !inventory.AddTo(section, item)))
            {
                Restore(before);
                break;
            }
        }
        return Grids(root, bag);
    }

    /// <summary>A layout's place in the grids, to tell layouts apart and to notice an inventory changed since.</summary>
    public static string Signature(List<Grid> grids) =>
        string.Join(";", grids.SelectMany(g => g.Inventory.Items.Where(i => i.Section == g.Section).Select(Cell)));

    static string Cell(ItemInstance i) => $"{i.Record}@{i.X},{i.Y}x{i.Quantity}";
}

/// <summary>
/// The record facts the search's affinity reads (StackSort.md "Score"): flag 1 for a <c>food crop</c> record, 2 for a <c>trade item</c>. Read on
/// the simulation's thread, cached per record.
/// </summary>
sealed class ItemFacts(PopulationData? data)
{
    readonly Dictionary<string, int> _flags = new(StringComparer.Ordinal);

    public int Flags(string record)
    {
        if (_flags.TryGetValue(record, out int flags)) return flags;

        var r = data?.Db.Find(record);
        flags = (r?.GetBool("food crop") == true ? 1 : 0) | (r?.GetBool("trade item") == true ? 2 : 0);
        return _flags[record] = flags;
    }
}
