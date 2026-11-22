using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>
/// Replaces the inventory window's ARRANGE with StackSort: like items grouped, split stacks merged, the free space in one block. The original's four
/// controls (<c>&lt; StackSort N H/W &gt;</c>, right-click undo) cannot be added to the game's button, so they ride on it: a click applies the best
/// layout and further clicks cycle the five best, Shift+click switches the goal between Height and Width, Ctrl+click undoes the sort.
/// </summary>
public sealed class StackSortMod : IMod
{
    internal static readonly StackSortSettings Settings = new();

    /// <summary>
    /// The installed original's cfg and captions.
    /// </summary>
    internal static OriginalFiles Original { get; private set; } = null!;

    /// <summary>The window ARRANGE commands said with a modifier held, read on the tick thread.</summary>
    static readonly ConditionalWeakTable<ArrangeInventory, StrongBox<ArrangeMode>> Modes = new();

    public void Initialize(ModContext context)
    {
        Original = OriginalFiles.Load(context.Folder);
        Settings.WidthGoal = Original.WidthGoal;
        Settings.Consolidate = Original.Consolidate;
        Settings.TopTargets = Original.TopTargets;
        context.AddSettings(Settings);
        context.Harmony.Patch(ArrangeMethod(), prefix: new HarmonyMethod(typeof(StackSortMod), nameof(BeforeArrange)));
        PatchWindowButton(context);
        context.Log("StackSort initialized");
    }

    static System.Reflection.MethodInfo ArrangeMethod() =>
        AccessTools.Method(typeof(InventoryCommands), nameof(InventoryCommands.Arrange), [typeof(Inventory)])
            ?? throw new MissingMethodException(nameof(InventoryCommands), "Arrange");

    /// <summary>The interface's <c>Say</c> tags an ARRANGE with its modifier; <c>PlayerSystem.ApplyActionCommand</c> runs it through the cycle.</summary>
    static void PatchWindowButton(ModContext context)
    {
        var player = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        var say = AccessTools.Method(player, "Say") ?? throw new MissingMethodException(player.FullName, "Say");
        var apply = AccessTools.Method(typeof(Meitou.Simulation.Player.PlayerSystem), "ApplyActionCommand")
            ?? throw new MissingMethodException("PlayerSystem", "ApplyActionCommand");

        context.Harmony.Patch(say, prefix: new HarmonyMethod(typeof(StackSortMod), nameof(BeforeSay)));
        context.Harmony.Patch(apply, prefix: new HarmonyMethod(typeof(StackSortMod), nameof(BeforeApply)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    /// <summary>Other callers of the whole-inventory arrange (a machine's storage, putting a held item back): the best layout for the setting's goal.</summary>
    static bool BeforeArrange(Inventory root, ref bool __result)
    {
        if (!Settings.Enabled || !Packer.Pack(root, null, Settings.WidthGoal ? Goal.Width : Goal.Height, Settings.Consolidate)) return true;

        __result = true;
        return false;
    }

    static void BeforeSay(object __instance, Meitou.Simulation.SimCommand command)
    {
        if (command is not ArrangeInventory arrange) return;

        var t = Traverse.Create(__instance);
        var mode = t.Field("CtrlHeld").GetValue<bool>() ? ArrangeMode.Undo
            : t.Property("SlowModifier").GetValue<bool>() ? ArrangeMode.SwitchGoal
            : ArrangeMode.Next;
        Modes.AddOrUpdate(arrange, new(mode));
    }

    static bool BeforeApply(Meitou.Simulation.World world, Meitou.Simulation.SimCommand command)
    #pragma warning restore IDE1006
    {
        if (!Settings.Enabled || command is not ArrangeInventory arrange) return true;

        var mode = Modes.TryGetValue(arrange, out var box) ? box.Value : ArrangeMode.Next;
        LayoutCycle.Run(world, arrange, mode);
        return false;
    }
}

public enum ArrangeMode { Next, SwitchGoal, Undo }

/// <summary>
/// The mod's settings; the last three take their defaults from the installed original's StackSort.cfg.
/// </summary>
public sealed class StackSortSettings
{
    /// <summary>
    /// Whether ARRANGE runs StackSort.
    /// </summary>
    [Setting("StackSort on Arrange", Hint = "Click: best layout, again to cycle the best ones. Shift+click: Height/Width goal. Ctrl+click: undo. Off: vanilla.")]
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
