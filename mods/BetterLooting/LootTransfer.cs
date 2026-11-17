using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;

namespace Meitou.BetterLooting;

/// <summary>
/// Moves items without the right click's walk or equip: into the looter's worn bag and main grid (the original places loot straight into the
/// inventory or backpack window's grid), what does not fit staying where it was.
/// </summary>
static class LootTransfer
{
    public const int MoneyFunction = 14;

    static readonly MethodInfo? CashMethod = AccessTools.Method(typeof(Meitou.Simulation.Player.PlayerSystem), "Cash");

    /// <summary>Moves the stack <paramref name="item"/> of <paramref name="owner"/> into <paramref name="looter"/>; the units moved.</summary>
    public static int Take(Inventory owner, ItemInstance item, CharacterCold looter, bool bagFirst)
    {
        int at = owner.Items.IndexOf(item);
        if (at < 0 || looter.Inventory is not { } into) return 0;

        int before = item.Quantity;
        owner.Items.RemoveAt(at);
        if (Place(into, item, bagFirst, InventoryCommands.MainClosed(looter))) return before;

        if (item.Quantity > 0) owner.Items.Insert(at, item);
        return before - item.Quantity;
    }

    static bool Place(Inventory into, ItemInstance item, bool bagFirst, bool mainShut)
    {
        var bag = InventoryCommands.FitsInBag(item) ? into.Backpack?.Contents : null;
        bool IntoBag() => bag is not null && bag.AddTo("backpack_content", item);
        bool IntoMain() => !mainShut && into.AddTo("main", item);

        return bagFirst ? IntoBag() || IntoMain() : IntoMain() || IntoBag();
    }

    /// <summary>Money into the player's purse (the engine's own cash step, by reflection); false when it is not reachable.</summary>
    public static bool Cash(World world, PopulationData data, Inventory owner, ItemInstance item)
    {
        if (CashMethod is null || !owner.Items.Contains(item)) return false;

        CashMethod.Invoke(null, [world, data, item, item.Quantity]);
        owner.Remove(item);
        return true;
    }
}

/// <summary>The engine's private equipment refresh (fighter and look from the inventory), by reflection as the captive strip uses it.</summary>
sealed class EquipmentRefresh(object assembly, MethodInfo refresh)
{
    public static EquipmentRefresh? Create(PopulationData data)
    {
        var type = AccessTools.TypeByName("Meitou.Simulation.Bodies.CharacterAssembly");
        var method = type is null ? null : AccessTools.Method(type, "RefreshEquipment");
        return method is null ? null : new(Activator.CreateInstance(type!, data)!, method);
    }

    public void Refresh(CharacterCold cold) => refresh.Invoke(assembly, [cold]);
}
