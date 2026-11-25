using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Player;

namespace Meitou.SlaverMod;

/// <summary>
/// The original's <c>ShacklesNoRunning</c> (it sets the slave's speed order to walk, <c>AbstractMovementBase::setDesiredSpeedOrders(0)</c>): a
/// player-owned slave wearing locked shackles moves at most at its race's walk speed. Harmony postfix on
/// <c>Meitou.Simulation.Player.Orders.TopSpeed</c> (a pure function of the character, read by the body step on any thread).
/// </summary>
static class SpeedPatch
{
    static SlaverSettings? s_settings;
    static Action<string> s_log = _ => { };
    static bool s_faulted;

    internal static void Install(ModContext context, SlaverSettings settings)
    {
        s_settings = settings;
        s_log = context.Log;
        var top = AccessTools.Method(typeof(Orders), nameof(Orders.TopSpeed), [typeof(float), typeof(CharacterCold), typeof(bool)]);
        if (top is null)
        {
            context.Log("Orders.TopSpeed not found: shackled slaves can run (the port needs an update)");
            return;
        }

        context.Harmony.Patch(top, postfix: new HarmonyMethod(typeof(SpeedPatch), nameof(After)));
    }

#pragma warning disable IDE1006 // Harmony injects the result by this name
    static void After(CharacterCold cold, ref float __result)
#pragma warning restore IDE1006
    {
        if (s_faulted || s_settings is not { Enabled: true, ShacklesNoRunning: true }) return;

        try
        {
            if (SlaverSystem.PlayerSlave(cold) && LockSystem.WornShackles(cold) is { Lock.Broken: false })
                __result = MathF.Min(__result, cold.Race?.WalkSpeed ?? 15);
        }
        catch (Exception e)
        {
            s_faulted = true;
            s_log($"the shackled walk cap stopped after an error: {e.Message}");
        }
    }
}
