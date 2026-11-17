using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>
/// The window ARRANGE's state per character and grid: the layouts offered for the current goal, which one is shown, and the layout before the first
/// click (for undo). A cycle is dropped once the grid changes by other means (its signature no longer matches).
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

    public static void Run(World world, ArrangeInventory arrange, ArrangeMode mode)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(arrange.Character, out int slot) || table.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory }) return;

        var key = (arrange.Character, arrange.Bag);
        var grids = Packer.Grids(inventory, arrange.Bag);
        var cycle = Cycles.GetValueOrDefault(key) is { } c && c.After == Packer.Signature(grids) ? c : null;

        if (mode == ArrangeMode.Undo) Undo(world, key, cycle);
        else Arrange(world, key, inventory, arrange.Bag, cycle, mode == ArrangeMode.SwitchGoal);
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
        world.Post("StackSort: sort undone.");
    }

    static void Arrange(World world, (CharacterId, bool) key, Inventory inventory, bool bag, Cycle? cycle, bool switchGoal)
    {
        if (switchGoal) s_goal = CurrentGoal == Goal.Height ? Goal.Width : Goal.Height;

        if (cycle is not null && cycle.Goal == CurrentGoal) cycle.Index = (cycle.Index + 1) % cycle.Layouts.Count;
        else cycle = Start(world, key, inventory, bag, cycle);

        if (cycle is null) return;

        Packer.Restore(cycle.Layouts[cycle.Index]);
        cycle.After = Packer.Signature(Packer.Grids(inventory, bag));
        world.Post($"StackSort {cycle.Index + 1}/{cycle.Layouts.Count} ({(cycle.Goal == Goal.Height ? "H" : "W")})");
    }

    /// <summary>A new cycle from the layout before any sort (an earlier cycle's undo state), or null when no layout fits (vanilla arrange then).</summary>
    static Cycle? Start(World world, (CharacterId, bool) key, Inventory inventory, bool bag, Cycle? earlier)
    {
        if (earlier is not null) Packer.Restore(earlier.Undo);

        var grids = Packer.Grids(inventory, bag);
        var undo = earlier?.Undo ?? Packer.Snapshot(grids);
        var layouts = Packer.Layouts(grids, CurrentGoal);
        if (layouts.Count == 0)
        {
            Cycles.Remove(key);
            if (!InventoryCommands.Arrange(inventory, bag)) world.Post("Cannot arrange inventory: the items do not fit.");
            return null;
        }

        var cycle = new Cycle(undo, layouts, CurrentGoal);
        Cycles[key] = cycle;
        return cycle;
    }
}
