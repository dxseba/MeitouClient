using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.FirstAid;
using Meitou.Simulation.Items;
using Meitou.Simulation.Perception;

namespace Meitou.CheatMenu;

/// <summary>
/// The squad switches the mod API has no filter for, as Harmony patches (README "Harmony"):
/// <list type="bullet">
/// <item>Perfect stealth (<c>notifyICanSeeYouSneaking</c> skipped when one of the two is the player's): a prefix on <c>SightMap.Sees</c> answers
/// 0 for a sneaking target when exactly one of looker and target is of the player's faction;</item>
/// <item>Weightless items (each section's weight multiplier 0): a postfix on <c>Inventory.ContentWeight(bool)</c> answers 0 for a player
/// character's inventory;</item>
/// <item>Infinite consumables (a million charges added around <c>drainMedkit</c>, <c>eatItem</c>, <c>reloadCheck</c>, then taken back): prefixes
/// and postfixes on <c>FirstAidSystem.Drain</c> and <c>FeedSystem.Eat</c> (not for a limb) do the same for a player character's item, and a
/// prefix on <c>Ammunition.UseOne</c> keeps the bolt in a player character's inventory.</item>
/// </list>
/// The patches read the cheat system's state, set in its serial steps. A throw is logged once and the game's own rule goes on.
/// </summary>
internal static class SquadPatches
{
    /// <summary>The original's extra charges around a use (<b>Verified</b>, 1,000,000).</summary>
    const float Extra = 1_000_000;

    static ModContext? s_context;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        Patch(context, AccessTools.Method(typeof(SightMap), nameof(SightMap.Sees)), prefix: nameof(SeesBefore));
        Patch(context, AccessTools.Method(typeof(Inventory), nameof(Inventory.ContentWeight), [typeof(bool)]), postfix: nameof(WeightAfter));
        Patch(context, AccessTools.Method(typeof(FirstAidSystem), "Drain"), nameof(DrainBefore), nameof(DrainAfter));
        Patch(context, AccessTools.Method(typeof(FeedSystem), "Eat"), nameof(EatBefore), nameof(EatAfter));
        Type[] useOneArgs = [typeof(Inventory), typeof(string), typeof(bool).MakeByRefType()];
        var useOne = AccessTools.Method(typeof(Ammunition), nameof(Ammunition.UseOne), useOneArgs);
        Patch(context, useOne, prefix: nameof(UseOneBefore));
    }

    static void Patch(ModContext context, MethodInfo? target, string? prefix = null, string? postfix = null)
    {
        if (target is null)
        {
            context.LogError($"a squad cheat's method was not found ({prefix ?? postfix}): that cheat does nothing");
            return;
        }

        context.Harmony.Patch(target, prefix: Bind(prefix), postfix: Bind(postfix));
    }

    static HarmonyMethod? Bind(string? name) => name is null ? null : new HarmonyMethod(typeof(SquadPatches), name);

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a squad cheat patch failed, the game's own rule goes on: {e}");
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool SeesBefore(int looker, int target, float stealth, bool[] ____player, int ____count, ref float __result)
    {
        try
        {
            if (stealth < 0 || CheatSystem.Current is not { } cheats || !cheats.On(Toggle.PerfectStealth)) return true;
            if ((uint)looker >= (uint)____count || (uint)target >= (uint)____count || ____player[looker] == ____player[target]) return true;

            __result = 0;
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    static void WeightAfter(Inventory __instance, ref float __result)
    {
        try
        {
            if (CheatSystem.Current?.PlayerInventory(__instance, Toggle.Weightless) == true) __result = 0;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void DrainBefore(World world, int m, (ItemInstance Item, Inventory Owner) kit, out float __state) =>
        __state = Lift(world, m, kit.Item);

    static void DrainAfter((ItemInstance Item, Inventory Owner) kit, float __state) => Drop(kit.Item, __state);

    static void EatBefore(World world, int slot, ItemInstance item, out float __state) =>
        __state = item.ItemFunction == 17 ? float.NaN : Lift(world, slot, item);

    static void EatAfter(ItemInstance item, float __state) => Drop(item, __state);

    /// <summary>Adds the extra charges for a player character's use; the charges it had, or NaN when nothing was added.</summary>
    static float Lift(World world, int slot, ItemInstance item)
    {
        try
        {
            if (CheatSystem.Of(world) is not { } cheats || !cheats.On(Toggle.InfiniteConsumables)) return float.NaN;
            if (world.Characters.Cold(slot) is not { IsPlayer: true }) return float.NaN;

            float had = item.Charges;
            item.Charges = had + Extra;
            return had;
        }
        catch (Exception e)
        {
            Fault(e);
            return float.NaN;
        }
    }

    /// <summary>Puts the charges back as they were before the use.</summary>
    static void Drop(ItemInstance item, float had)
    {
        if (!float.IsNaN(had)) item.Charges = had;
    }

    static bool UseOneBefore(Inventory? inventory, string ammoId, out bool unitGone, ref bool __result)
    {
        unitGone = false;
        try
        {
            if (CheatSystem.Current?.PlayerInventory(inventory, Toggle.InfiniteConsumables) != true) return true;
            if (Ammunition.Find(inventory, ammoId) is null) return true;

            __result = true;
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }
#pragma warning restore IDE1006
}
