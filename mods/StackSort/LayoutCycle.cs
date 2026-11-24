using System.Collections.Concurrent;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.StackSort.Search;

namespace Meitou.StackSort;

/// <summary>
/// The window ARRANGE's state per character and grid: the layouts offered for the current goal (one per ranked target), which one is shown,
/// and the layout before the first click (for undo). A cycle is dropped once the grid changes by other means (its signature no longer matches).
/// The shown layout's number and the goal are published for the strip's captions.
/// </summary>
static class LayoutCycle
{
    sealed class Cycle(Packer.ItemState[] undo, List<Packer.Grid> grids, List<PackLayout[]> layouts, Goal goal)
    {
        public Packer.ItemState[] Undo { get; } = undo;
        public List<Packer.Grid> Grids { get; } = grids;
        public List<PackLayout[]> Layouts { get; } = layouts;
        public Goal Goal { get; } = goal;
        public int Index { get; set; }
        public string After { get; set; } = "";
    }

    static readonly Dictionary<(CharacterId Character, bool Bag), Cycle> Cycles = [];
    static Goal? s_goal;

    /// <summary>The shown layout's number (1-based) per character and grid, for the strip (read on the host's thread).</summary>
    public static readonly ConcurrentDictionary<(CharacterId Character, bool Bag), int> Shown = new();

    /// <summary>The goal the next sort targets, for the strip.</summary>
    public static Goal Current => s_goal ?? (StackSortMod.Settings.WidthGoal ? Goal.Width : Goal.Height);

    public static void Run(World world, CharacterId character, bool bag, ArrangeMode mode, ItemFacts facts)
    {
        if (Inventory(world, character) is not { } inventory) return;

        var key = (character, bag);
        var cycle = Live(key, inventory, bag);
        if (cycle is null) Shown.TryRemove(key, out _);

        if (mode == ArrangeMode.Undo) Undo(world, key, cycle);
        else Arrange(world, key, inventory, bag, cycle, mode, facts);
    }

    /// <summary>
    /// A player character's inventory, or null.
    /// </summary>
    static Inventory? Inventory(World world, CharacterId character)
    {
        var table = world.Characters;
        return table.TryResolveNext(character, out int slot) && table.Cold(slot) is { IsPlayer: true, Inventory: { } inventory } ? inventory : null;
    }

    /// <summary>
    /// The grid's cycle while the grid still shows its layout, else null.
    /// </summary>
    static Cycle? Live((CharacterId, bool) key, Inventory inventory, bool bag) =>
        Cycles.GetValueOrDefault(key) is { } c && c.After == Packer.Signature(Packer.Grids(inventory, bag)) ? c : null;

    /// <summary>
    /// The grid's cells and units as text, for the precompute to notice changes; null without a player character's inventory or while a cycle
    /// shows on the grid (a press then steps the cycle and needs no search).
    /// </summary>
    public static string? Signature(World world, CharacterId character, bool bag)
    {
        if (Inventory(world, character) is not { } inventory || Live((character, bag), inventory, bag) is not null) return null;

        return $"{Current}|{Packer.Signature(Packer.Grids(inventory, bag))}";
    }

    /// <summary>
    /// The grid's search problems as a press would see them (with Merge stacks, merged on a copy of the inventory), for the precompute; null
    /// without a player character's inventory or while a cycle shows on the grid.
    /// </summary>
    public static List<PackProblem>? Problems(World world, CharacterId character, bool bag, ItemFacts facts)
    {
        if (Inventory(world, character) is not { } inventory || Live((character, bag), inventory, bag) is not null) return null;

        var grids = StackSortMod.Settings.Consolidate ? Packer.Merge(inventory.Copy(), bag) : Packer.Grids(inventory, bag);
        return [.. grids.Select(g => Packer.Problem(g, facts))];
    }

    static void Undo(World world, (CharacterId, bool) key, Cycle? cycle)
    {
        if (cycle is null)
        {
            world.Post("StackSort: nothing to undo.");
            return;
        }

        Packer.Restore(cycle.Undo);
        Cycles.Remove(key);
        Shown.TryRemove(key, out _);
        StackSortMod.Log(1, () => $"Restored the layout before the sort for {Grid(key.Item2)}");
        world.Post("StackSort: sort undone.");
    }

    /// <summary>
    /// The next (or with <see cref="ArrangeMode.Previous"/> the previous) layout of the cycle, or a new cycle when none runs for the goal;
    /// <see cref="ArrangeMode.SwitchGoal"/> switches the goal first.
    /// </summary>
    static void Arrange(World world, (CharacterId, bool) key, Inventory inventory, bool bag, Cycle? cycle, ArrangeMode mode, ItemFacts facts)
    {
        if (mode == ArrangeMode.SwitchGoal) s_goal = Current == Goal.Height ? Goal.Width : Goal.Height;

        int step = mode == ArrangeMode.Previous ? -1 : 1;
        if (cycle is not null && cycle.Goal == Current)
        {
            int count = cycle.Layouts.Count;
            cycle.Index = (cycle.Index + step + count) % count;
        }
        else cycle = Start(world, key, inventory, bag, cycle, facts);

        if (cycle is null) return;

        Packer.Apply(cycle.Grids, cycle.Layouts[cycle.Index]);
        cycle.After = Packer.Signature(Packer.Grids(inventory, bag));
        Shown[key] = cycle.Index + 1;
        StackSortMod.Log(1, () => $"Applying layout [{cycle.Index + 1}/{cycle.Layouts.Count}] target={cycle.Goal} for {Grid(bag)}");
        world.Post(StackSortMod.Original.Caption(cycle.Index + 1, cycle.Goal));
    }

    /// <summary>
    /// The grid a cycle sorts, for the log.
    /// </summary>
    static string Grid(bool bag) => bag ? "the bag" : "the inventory";

    /// <summary>
    /// A new cycle from the layout before any sort (an earlier cycle's undo state): Merge stacks merges first (the first click's
    /// consolidation), then the search (waiting for its precompute when one runs) offers its layouts; null when nothing fits (vanilla arrange).
    /// </summary>
    static Cycle? Start(World world, (CharacterId, bool) key, Inventory inventory, bool bag, Cycle? earlier, ItemFacts facts)
    {
        if (earlier is not null) Packer.Restore(earlier.Undo);

        var undo = earlier?.Undo ?? Packer.Snapshot(Packer.Grids(inventory, bag));
        var settings = StackSortMod.Settings;
        var grids = settings.Consolidate ? Packer.Merge(inventory, bag) : Packer.Grids(inventory, bag);
        var layouts = LayoutSearch.Layouts(grids, facts, Current, settings.TopTargets, StackSortMod.Original.Budget);
        int items = grids.Sum(g => g.Items.Length);
        StackSortMod.Log(1, () => $"Packed {items} items of {Grid(bag)}: {layouts.Count} layouts for target={Current}");
        if (layouts.Count == 0)
        {
            Packer.Restore(undo);
            Cycles.Remove(key);
            Shown.TryRemove(key, out _);
            if (!InventoryCommands.Arrange(inventory, bag)) world.Post("Cannot arrange inventory: the items do not fit.");
            return null;
        }

        var cycle = new Cycle(undo, grids, layouts, Current);
        Cycles[key] = cycle;
        return cycle;
    }
}
