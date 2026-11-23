using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>
/// Replaces the inventory window's ARRANGE with StackSort's strip <c>&lt; StackSort [n] H/W &gt;</c> (screen controls over the button): like items
/// grouped, split stacks merged, the free space in one block. The sort button applies the best layout and cycles the best few, the arrows step
/// through them, H/W switches the goal, and a right click on the sort button undoes the sort.
/// </summary>
public sealed class StackSortMod : IMod
{
    internal static readonly StackSortSettings Settings = new();

    /// <summary>
    /// The installed original's cfg and captions.
    /// </summary>
    internal static OriginalFiles Original { get; private set; } = null!;

    public void Initialize(ModContext context)
    {
        Original = OriginalFiles.Load(context.Folder);
        Settings.WidthGoal = Original.WidthGoal;
        Settings.Consolidate = Original.Consolidate;
        Settings.TopTargets = Original.TopTargets;
        context.AddSettings(Settings);
        context.AddSystem("stacksort", _ => new StackSortSystem(), SystemAnchor.End);
        context.AddUi(new StackSortUi(Original.Strip));
        context.Log("StackSort initialized");
    }
}

/// <summary>
/// A strip button's press: StackSort's arrange of a player character's main grid or worn bag. A paused command, so it applies between ticks while
/// paused as the game's ARRANGE does.
/// </summary>
public sealed record StackSortArrange(CharacterId Character, bool Bag, ArrangeMode Mode) : SimCommand, IPausedCommand;

/// <summary>
/// Applies <see cref="StackSortArrange"/> in the input step, and while paused in the paused interface step; as the arrange filter, answers every
/// other arrange (a machine's, container's or the trade stock's ARRANGE, making room for a held item, the stock grid being built) with the best
/// layout for the <c>Width goal</c> setting.
/// </summary>
sealed class StackSortSystem : IPausedCommandHandler, IInventoryArrangeFilter
{
    /// <summary>
    /// The best layout for the setting's goal; null (the game's arrange) when off, when nothing fits, and for a character window's ARRANGE, which
    /// the strip covers. With <c>Merge stacks</c> the engine merges the partial stacks first.
    /// </summary>
    public InventoryArrangement? Arrange(World world, in InventoryArrangeAsk ask)
    {
        var settings = StackSortMod.Settings;
        if (!settings.Enabled) return null;
        if (ask.Owner.Holder == ItemHolder.Character && ask.Reason == InventoryArrangeReason.Button) return null;
        if (settings.Consolidate && !ask.Merged) return InventoryArrangement.MergeFirst;

        var grids = ask.Grids.Select(g => new Packer.Grid(g.Grid, g.Section, [.. g.Grid.Items])).ToList();
        var layouts = Packer.Layouts(grids, settings.WidthGoal ? Goal.Width : Goal.Height, 1, merge: false);
        return layouts.Count == 0 ? null : new InventoryArrangement(Packer.Placements(layouts[0]));
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        if (!StackSortMod.Settings.Enabled) return;

        foreach (var command in commands)
        {
            if (command is StackSortArrange arrange) LayoutCycle.Run(world, arrange.Character, arrange.Bag, arrange.Mode);
        }
    }
}

/// <summary>
/// What a strip button asks: the next layout (the sort button and <c>&gt;</c>), the previous one (<c>&lt;</c>), the other goal (H/W), or the layout
/// before the first sort (a right click on the sort button).
/// </summary>
public enum ArrangeMode { Next, Previous, SwitchGoal, Undo }

/// <summary>
/// The mod's settings; the last three take their defaults from the installed original's StackSort.cfg.
/// </summary>
public sealed class StackSortSettings
{
    /// <summary>
    /// Whether the strip replaces ARRANGE.
    /// </summary>
    [Setting("StackSort on Arrange", Hint = "Sort: best layout, again to cycle; < >: step; H/W: goal; right-click Sort: undo. Off: vanilla.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>default_dim</c> (H).
    /// </summary>
    [Setting("Width goal", Hint = "default_dim: start with the Width goal (columns packed to the left) instead of Height (rows packed to the top).")]
    public bool WidthGoal { get; set; }

    /// <summary>
    /// <c>consolidate_stacks</c> (true).
    /// </summary>
    [Setting("Merge stacks", Hint = "consolidate_stacks: merge stackable items into full stacks before the first sort.")]
    public bool Consolidate { get; set; } = true;

    /// <summary>
    /// <c>top_targets</c> (5).
    /// </summary>
    [Setting("Layouts in the cycle", Hint = "top_targets: how many layouts a click cycles through.", Min = 1, Max = 10)]
    public int TopTargets { get; set; } = 5;
}
