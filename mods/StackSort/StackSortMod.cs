using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation.Items;

namespace Meitou.StackSort;

/// <summary>
/// Replaces the inventory window's ARRANGE with a packing that keeps like items together, merges split stacks and leaves the free space in one block
/// at the bottom. When no candidate layout fits, the vanilla arrange runs unchanged.
/// </summary>
public sealed class StackSortMod : IMod
{
    internal static readonly StackSortSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.Harmony.Patch(ArrangeMethod([typeof(Inventory)]), prefix: new HarmonyMethod(typeof(StackSortMod), nameof(BeforeArrange)));
        context.Harmony.Patch(ArrangeMethod([typeof(Inventory), typeof(bool)]), prefix: new HarmonyMethod(typeof(StackSortMod), nameof(BeforeArrangeWindow)));
        context.Log("StackSort initialized");
    }

    static System.Reflection.MethodInfo ArrangeMethod(Type[] parameters) =>
        AccessTools.Method(typeof(InventoryCommands), nameof(InventoryCommands.Arrange), parameters)
            ?? throw new MissingMethodException(nameof(InventoryCommands), "Arrange");

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static bool BeforeArrange(Inventory root, ref bool __result) => Replace(root, null, ref __result);

    /// <summary>The inventory window's ARRANGE button: only the grid the window shows.</summary>
    static bool BeforeArrangeWindow(Inventory root, bool bag, ref bool __result) => Replace(root, bag, ref __result);

    static bool Replace(Inventory root, bool? bag, ref bool __result)
    #pragma warning restore IDE1006
    {
        if (!Settings.Enabled || !Packer.Pack(root, bag)) return true;
        __result = true;
        return false;
    }
}

public sealed class StackSortSettings
{
    [Setting("StackSort on Arrange", Hint = "Arrange groups like items together and leaves the free space in one block. Off: vanilla arrange.")]
    public bool Enabled { get; set; } = true;
}
