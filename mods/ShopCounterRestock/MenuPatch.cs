using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Production;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The original's counter menu: its Restock... row opens the settings window, and with Shift held it assigns the selected worker without opening
/// it. Meitou's job offer row always assigns, so a prefix on <c>Meitou.Game.Building.BuildMode.GiveOffered</c> (the row's press, host thread)
/// opens the window instead when Shift is up (read each frame through <c>AddFrame</c>), and lets the assignment through with Shift. A throw is
/// logged once and the row assigns as without the patch.
/// </summary>
static class MenuPatch
{
    static ModContext? s_context;
    static volatile bool s_shift;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        context.AddFrame(frame => s_shift = frame.Shift);

        var type = AccessTools.TypeByName("Meitou.Game.Building.BuildMode");
        var give = type is null ? null : AccessTools.Method(type, "GiveOffered");
        if (give is null)
        {
            context.LogError("BuildMode.GiveOffered not found: the Restock... row assigns, the window opens from the building panel only");
            return;
        }

        context.Harmony.Patch(give, prefix: new HarmonyMethod(typeof(MenuPatch), nameof(GivePrefix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool GivePrefix(ModJobOffer offer, BuildingSnapshot site)
    {
        try
        {
            if (offer.Type != ShopCounterRestockMod.JobType || s_shift || RestockWindow.Instance is not { } window) return true;

            window.Open(site.Id);
            return false;
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"the Restock... row failed, it assigns instead: {e}");
            return true;
        }
    }
#pragma warning restore IDE1006
}
