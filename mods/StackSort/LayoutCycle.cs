using System.Collections.Concurrent;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>
/// The window ARRANGE's state per character and grid: the layouts offered for the current goal, which one is shown, and the layout before the first
/// click (for undo). A cycle is dropped once the grid changes by other means (its signature no longer matches). The shown layout's number and the
/// goal are published for the strip's captions.
/// </summary>
static class LayoutCycle
{
    sealed class Cycle(Packer.ItemState[] undo, List<Packer.ItemState[]> layouts, Goal goal)
    {
        public Packer.ItemState[] Undo { get; } = undo;
        public List<Packer.ItemState[]> Layouts { get; } = layouts;
        public Goal Goal { get; } = goal;
        public int Index { get; set; }
        public string After { get; set; } = "";
    }

    static readonly Dictionary<(CharacterId Character, bool Bag), Cycle> Cycles = [];
    static Goal? s_goal;

    /// <summary>The shown layout's number (1-based) per character and grid, for the strip (read on the host's thread).</summary>
    public static readonly ConcurrentDictionary<(CharacterId Character, bool Bag), int> Shown = new();

    /// <summary>The goal the next sort targets, for the strip.</summary>
    public static Goal Current => CurrentGoal;

    public static void Run(World world, CharacterId character, bool bag, ArrangeMode mode)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(character, out int slot) || table.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory }) return;

        var key = (character, bag);
        var grids = Packer.Grids(inventory, bag);
        var cycle = Cycles.GetValueOrDefault(key) is { } c && c.After == Packer.Signature(grids) ? c : null;
        if (cycle is null) Shown.TryRemove(key, out _);

        if (mode == ArrangeMode.Undo) Undo(world, key, cycle);
        else Arrange(world, key, inventory, bag, cycle, mode);
    }

    static Goal CurrentGoal => s_goal ?? (StackSortMod.Settings.WidthGoal ? Goal.Width : Goal.Height);

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
        world.Post("StackSort: sort undone.");
    }

    /// <summary>
    /// The next (or with <see cref="ArrangeMode.Previous"/> the previous) layout of the cycle, or a new cycle when none runs for the goal;
    /// <see cref="ArrangeMode.SwitchGoal"/> switches the goal first.
    /// </summary>
    static void Arrange(World world, (CharacterId, bool) key, Inventory inventory, bool bag, Cycle? cycle, ArrangeMode mode)
    {
        if (mode == ArrangeMode.SwitchGoal) s_goal = CurrentGoal == Goal.Height ? Goal.Width : Goal.Height;

        int step = mode == ArrangeMode.Previous ? -1 : 1;
        if (cycle is not null && cycle.Goal == CurrentGoal)
        {
            int count = cycle.Layouts.Count;
            cycle.Index = (cycle.Index + step + count) % count;
        }
        else cycle = Start(world, key, inventory, bag, cycle);

        if (cycle is null) return;

        Packer.Restore(cycle.Layouts[cycle.Index]);
        cycle.After = Packer.Signature(Packer.Grids(inventory, bag));
        Shown[key] = cycle.Index + 1;
        world.Post(StackSortMod.Original.Caption(cycle.Index + 1, cycle.Goal));
    }

    /// <summary>A new cycle from the layout before any sort (an earlier cycle's undo state), or null when no layout fits (vanilla arrange then).</summary>
    static Cycle? Start(World world, (CharacterId, bool) key, Inventory inventory, bool bag, Cycle? earlier)
    {
        if (earlier is not null) Packer.Restore(earlier.Undo);

        var grids = Packer.Grids(inventory, bag);
        var undo = earlier?.Undo ?? Packer.Snapshot(grids);
        var settings = StackSortMod.Settings;
        var layouts = Packer.Layouts(grids, CurrentGoal, settings.TopTargets, settings.Consolidate);
        if (layouts.Count == 0)
        {
            Cycles.Remove(key);
            Shown.TryRemove(key, out _);
            if (!InventoryCommands.Arrange(inventory, bag)) world.Post("Cannot arrange inventory: the items do not fit.");
            return null;
        }

        var cycle = new Cycle(undo, layouts, CurrentGoal);
        Cycles[key] = cycle;
        return cycle;
    }
}
