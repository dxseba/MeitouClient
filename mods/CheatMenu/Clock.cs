using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation.Session;

namespace Meitou.CheatMenu;

/// <summary>
/// Skip to morning / night and Set time: the mod API has no clock control, so a postfix on <c>Meitou.Game.Host.WorldSession</c>'s constructors
/// keeps the newest session, whose <see cref="GameClock"/> is the host's own (read and set on the host's thread only, from the window's
/// actions). Time moves forward only, as the original's skip (an hour earlier than now is the next day). A throw in the postfix is logged once.
/// </summary>
internal static class Clock
{
    static WeakReference<WorldSession>? s_session;
    static ModContext? s_context;
    static int s_faulted;

    static GameClock? Current => s_session is { } weak && weak.TryGetTarget(out var session) ? session.Clock : null;

    public static void Install(ModContext context)
    {
        s_context = context;
        var ctors = AccessTools.GetDeclaredConstructors(typeof(WorldSession));
        if (ctors.Count == 0) context.LogError("WorldSession constructors not found: the time cannot be set");

        foreach (var ctor in ctors) context.Harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(Clock), nameof(Created)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Created(WorldSession __instance)
    {
        try
        {
            s_session = new WeakReference<WorldSession>(__instance);
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"the clock capture failed: {e}");
        }
    }
#pragma warning restore IDE1006

    /// <summary>Moves the clock forward to <paramref name="hour"/> (0 to 23.99); false without a clock.</summary>
    internal static bool SkipTo(double hour)
    {
        if (Current is not { } clock || !double.IsFinite(hour)) return false;

        hour = Math.Clamp(hour, 0, 23.99);
        long day = hour <= clock.HourOfDay ? clock.Day + 1 : clock.Day;
        clock.SetTime(day, hour);
        return true;
    }
}
