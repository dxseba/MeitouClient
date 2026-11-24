using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Trading;

namespace Meitou.ItemMarkers;

/// <summary>
/// Which item views show a faction's uniform. The snapshot's item views do not carry <see cref="ItemInstance.Uniform"/> (the original reads the
/// item's uniform faction, vfunc <c>0x2f8</c>), so Harmony postfixes on the methods that make the views note, while the world publishes, each
/// view whose item is a uniform: <c>Meitou.Simulation.World.ItemViewOf</c> (characters' inventories, the loot window's body),
/// <c>Meitou.Simulation.Production.MachineSnapshot.StorageView</c> (storages) and <c>Meitou.Simulation.Trading.TradeSystem.StockViews</c> (a
/// trader's stock). The note is a weak table keyed by the view object (display only: nothing feeds the world, the state hash or a save); the
/// interface looks a cell's view up in the snapshot. A failing postfix logs once and stops noting.
/// </summary>
static class UniformBook
{
    static readonly ConditionalWeakTable<object, object> Uniforms = [];
    static readonly object Yes = new();
    static Action<string>? s_log;
    static bool s_failed;
    static MethodInfo? s_window;

    /// <summary>Whether <paramref name="view"/> (an <see cref="InventoryItemView"/> or <see cref="TradeItemView"/>) was noted as a uniform.</summary>
    public static bool IsUniform(object? view) => view is not null && Uniforms.TryGetValue(view, out _);

    public static void Install(ModContext context)
    {
        s_log = context.Log;
        s_window = AccessTools.PropertyGetter(typeof(Shop), "Window");
        Patch(context, AccessTools.Method(typeof(World), "ItemViewOf"), nameof(CharacterItem));
        Patch(context, AccessTools.Method(typeof(Simulation.Production.MachineSnapshot), "StorageView"), nameof(Storage));
        Patch(context, AccessTools.Method(typeof(TradeSystem), "StockViews"), nameof(Stock));
    }

    static void Patch(ModContext context, MethodInfo? target, string postfix)
    {
        if (target is null)
        {
            context.Log($"{postfix} view method not found: no uniform badge there");
            return;
        }

        context.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(UniformBook), postfix));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void CharacterItem(ItemInstance item, InventoryItemView __result)
    {
        if (s_failed || item is not { Uniform.Length: > 0 }) return;

        try
        {
            Uniforms.AddOrUpdate(__result, Yes);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    static void Storage(Inventory grid, InventoryView __result)
    {
        if (s_failed) return;

        try
        {
            for (int i = 0; i < grid.Items.Count && i < __result.Items.Count; i++) Note(grid.Items[i], __result.Items[i]);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    static void Stock(Shop shop, List<TradeItemView> __result)
    {
        if (s_failed) return;

        try
        {
            if (s_window?.Invoke(shop, null) is not Inventory window) return;

            for (int i = 0; i < window.Items.Count && i < __result.Count; i++) Note(window.Items[i], __result[i]);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }
#pragma warning restore IDE1006

    static void Note(ItemInstance item, object view)
    {
        if (item.Uniform.Length > 0) Uniforms.AddOrUpdate(view, Yes);
    }

    static void Fail(Exception e)
    {
        s_failed = true;
        s_log?.Invoke($"uniform note failed, left off for the session: {e.Message}");
    }
}
