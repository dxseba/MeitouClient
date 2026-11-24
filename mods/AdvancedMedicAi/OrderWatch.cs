using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// The original's <c>Character::addOrder</c> hook: a postfix on <see cref="PlayerOrders.Give"/> (every order queued for a character, the player's
/// right clicks and the engine's own) tells the world's <see cref="MedicSystem"/>. It runs in the serial phase that gave the order; a throw is
/// caught, logged once, and the order goes on as without the mod.
/// </summary>
static class OrderWatch
{
    static ModContext? s_context;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        var give = AccessTools.Method(typeof(PlayerOrders), nameof(PlayerOrders.Give));
        if (give is null)
        {
            context.LogError("PlayerOrders.Give not found: manual orders are not noticed");
            return;
        }

        context.Harmony.Patch(give, postfix: new HarmonyMethod(typeof(OrderWatch), nameof(GivePostfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void GivePostfix(World world, int slot, OrderEntry order)
    {
        try
        {
            foreach (var system in world.Systems)
            {
                if (system is MedicSystem medics) medics.OrderGiven(world, slot, order.Row);
            }
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"noting an order failed, orders are not watched: {e}");
        }
    }
#pragma warning restore IDE1006
}
