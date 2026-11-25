using System.Collections.Immutable;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Player;

namespace Meitou.QuestsContracts;

/// <summary>The player's sneaking <see cref="Thief"/> reaches for <see cref="Mark"/>'s pack (the menu's Pickpocket).</summary>
public sealed record PickpocketOrder(CharacterId Thief, CharacterId Mark) : SimCommand;

/// <summary>
/// Pickpocketing (the original adds the game's Loot order, 26, to the menu of a standing, conscious man who is not the player's while the player's
/// man sneaks): the approach roll is the game's steal roll with no item (the mark watches with 1.1 x thievery / 100); noticed, the game's
/// outcome (the bark, the Theft) and no window; unnoticed, the mark's inventory opens through the game's open-loot order, and every item taken
/// rolls as a pickpocket's take does. Serial phases; the open marks are published for <see cref="PickpocketPatch"/>.
/// </summary>
sealed class Pickpockets
{
    const float OpenSeconds = 60;

    readonly Dictionary<CharacterId, long> _open = [];

    /// <summary>The marks whose inventory may open as a pickpocket's now (swapped whole in the serial step).</summary>
    internal static volatile ImmutableHashSet<CharacterId> Open = [];

    public void Order(World world, PickpocketOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Thief, out int t) || table.Cold(t) is not { IsPlayer: true } thief) return;
        if (!Orders.Has(thief, CharacterOrders.Sneak))
        {
            world.Post("Sneak first to pickpocket.");
            return;
        }

        if (!table.TryResolveNext(order.Mark, out int m) || table.Cold(m) is not { } mark || !Eligible(mark)) return;
        if (world.Systems.OfType<CrimeSystem>().FirstOrDefault() is not { } crimes) return;

        float chance = 1.1f * (thief.Stats?[StatsEnumerated.Thieving] ?? 0) / 100;
        if (crimes.StealRoll(world, order.Thief, order.Mark, chance, mark.Faction, order.Mark))
        {
            world.Post($"Caught with your hand in {mark.Name}'s pack.");
            return;
        }

        _open[order.Mark] = world.Tick + (long)(OpenSeconds / world.TickSeconds);
        Publish();
        world.Defer(new OpenLootOrder([order.Thief], order.Mark) { Tick = world.Tick + 1 });
    }

    /// <summary>A mark stays open while its window is open, else for a minute.</summary>
    public void Tick(World world)
    {
        if (_open.Count == 0) return;

        int before = _open.Count;
        foreach (var (mark, until) in _open.ToList())
        {
            if (world.Player.OpenLoot != mark && world.Player.OpeningLoot != mark && world.Tick > until) _open.Remove(mark);
        }

        if (_open.Count != before) Publish();
    }

    void Publish() => Open = [.. _open.Keys];

    /// <summary>Standing, conscious, not the player's, not carried or held, with an inventory.</summary>
    internal static bool Eligible(CharacterCold c) =>
        c is { IsPlayer: false, Inventory: not null, Medical.Incapacitated: false, InCombat: false } && c.CarriedBy.IsNone
            && c.InSomething == Simulation.Locks.HeldState.None;
}

/// <summary>
/// Opens a pickpocket's mark to the game's open-loot path: Harmony postfix on <c>Meitou.Simulation.Carrying.CarrySystem.CanBePickpocketed</c>
/// (the game's own pickpocket test: in a bed or caged), true also for a mark the approach roll let through.
/// </summary>
static class PickpocketPatch
{
    static Action<string> s_log = _ => { };
    static bool s_faulted;

    internal static void Install(ModContext context)
    {
        s_log = context.Log;
        var test = AccessTools.Method(typeof(CarrySystem), nameof(CarrySystem.CanBePickpocketed), [typeof(CharacterTable), typeof(int)]);
        if (test is null)
        {
            context.Log("CarrySystem.CanBePickpocketed not found: pickpocketing is off (the port needs an update)");
            return;
        }

        context.Harmony.Patch(test, postfix: new HarmonyMethod(typeof(PickpocketPatch), nameof(After)));
    }

#pragma warning disable IDE1006 // Harmony injects the result by this name
    static void After(CharacterTable table, int slot, ref bool __result)
#pragma warning restore IDE1006
    {
        if (__result || s_faulted || Pickpockets.Open.IsEmpty) return;

        try
        {
            __result = Pickpockets.Open.Contains(table.IdOf(slot)) && table.Cold(slot) is { } c && Pickpockets.Eligible(c);
        }
        catch (Exception e)
        {
            s_faulted = true;
            s_log($"pickpocketing stopped after an error: {e.Message}");
        }
    }
}
