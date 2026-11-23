using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>
/// Replaces the inventory window's ARRANGE with StackSort's strip <c>&lt; StackSort [n] H/W &gt;</c> (screen controls over the button): like items
/// grouped, split stacks merged, the free space in one block. The sort button applies the best layout and cycles the best few, the arrows step
/// through them, H/W switches the goal, and Ctrl+click on the sort button undoes the sort (the original's right click).
/// </summary>
public sealed class StackSortMod : IMod
{
    internal static readonly StackSortSettings Settings = new();

    /// <summary>
    /// The installed original's cfg and captions.
    /// </summary>
    internal static OriginalFiles Original { get; private set; } = null!;

    /// <summary>
    /// Whether Ctrl was held at the last frame (read by the sort button's click on the host's thread).
    /// </summary>
    internal static volatile bool CtrlHeld;

    /// <summary>The strip's ARRANGE commands and what each asks, read on the tick thread; one without an entry (the game's button) is Next.</summary>
    static readonly ConditionalWeakTable<ArrangeInventory, StrongBox<ArrangeMode>> Modes = new();

    public void Initialize(ModContext context)
    {
        Original = OriginalFiles.Load(context.Folder);
        Settings.WidthGoal = Original.WidthGoal;
        Settings.Consolidate = Original.Consolidate;
        Settings.TopTargets = Original.TopTargets;
        context.AddSettings(Settings);
        context.Harmony.Patch(ArrangeMethod(), prefix: new HarmonyMethod(typeof(StackSortMod), nameof(BeforeArrange)));
        context.Harmony.Patch(ApplyMethod(), prefix: new HarmonyMethod(typeof(StackSortMod), nameof(BeforeApply)));
        context.AddFrame(frame => CtrlHeld = frame.Ctrl);
        context.AddUi(new StackSortUi(Original.Strip));
        context.Log("StackSort initialized");
    }

    /// <summary>
    /// The game's ARRANGE command for a strip button, tagged with what it asks. It is the game's own command so that it applies in the paused
    /// interface step too (a mod's command waits for the next tick).
    /// </summary>
    internal static ArrangeInventory Order(CharacterId character, bool bag, ArrangeMode mode, long tick)
    {
        var arrange = new ArrangeInventory(character, bag) { Tick = tick };
        Modes.AddOrUpdate(arrange, new(mode));
        return arrange;
    }

    /// <summary>
    /// The public whole-inventory arrange other callers use (a machine's storage, putting a held item back); no hook covers it.
    /// </summary>
    static System.Reflection.MethodInfo ArrangeMethod() =>
        AccessTools.Method(typeof(InventoryCommands), nameof(InventoryCommands.Arrange), [typeof(Inventory)])
            ?? throw new MissingMethodException(nameof(InventoryCommands), "Arrange");

    /// <summary>
    /// The player system's private command step, where <see cref="ArrangeInventory"/> is applied (while paused too); no hook takes it over.
    /// </summary>
    static System.Reflection.MethodInfo ApplyMethod() =>
        AccessTools.Method(typeof(Meitou.Simulation.Player.PlayerSystem), "ApplyActionCommand")
            ?? throw new MissingMethodException("PlayerSystem", "ApplyActionCommand");

    #pragma warning disable IDE1006 // Harmony binds __result by name
    /// <summary>Other callers of the whole-inventory arrange: the best layout for the setting's goal.</summary>
    static bool BeforeArrange(Inventory root, ref bool __result)
    #pragma warning restore IDE1006
    {
        if (!Settings.Enabled || !Packer.Pack(root, null, Settings.WidthGoal ? Goal.Width : Goal.Height, Settings.Consolidate)) return true;

        __result = true;
        return false;
    }

    /// <summary>An ARRANGE of a character window runs through the layout cycle.</summary>
    static bool BeforeApply(World world, SimCommand command)
    {
        if (!Settings.Enabled || command is not ArrangeInventory arrange) return true;

        var mode = Modes.TryGetValue(arrange, out var box) ? box.Value : ArrangeMode.Next;
        LayoutCycle.Run(world, arrange.Character, arrange.Bag, mode);
        return false;
    }
}

/// <summary>
/// What a strip button asks: the next layout (the sort button and <c>&gt;</c>), the previous one (<c>&lt;</c>), the other goal (H/W), or the layout
/// before the first sort (Ctrl+click on the sort button).
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
    [Setting("StackSort on Arrange", Hint = "Sort: best layout, again to cycle; < >: step; H/W: goal; Ctrl+click Sort: undo. Off: vanilla.")]
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
