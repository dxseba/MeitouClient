using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation.Session;

namespace Meitou.DirectorPanel;

/// <summary>
/// The World tab's time of day. The mod API has no clock control, so a postfix on every constructor of <c>Meitou.Game.Host.WorldSession</c> keeps
/// the newest session; its <see cref="GameClock"/> is the host's own (the one a load sets), read and set only on the host's thread (UI actions and
/// the frame callback). Setting the time moves only forward, as the original's (an hour earlier than now is the next day); freezing holds the
/// hour of day each frame while game time runs on. A throw in the postfix is logged once and the World tab says the clock is not found.
/// </summary>
internal static class ClockPatch
{
    static WeakReference<WorldSession>? s_session;
    static ModContext? s_context;
    static int s_faulted;

    /// <summary>The frozen hour of day; null while time runs.</summary>
    internal static double? Frozen { get; set; }

    internal static GameClock? Clock => s_session is { } weak && weak.TryGetTarget(out var session) ? session.Clock : null;

    public static void Install(ModContext context)
    {
        s_context = context;
        var ctors = AccessTools.GetDeclaredConstructors(typeof(WorldSession));
        if (ctors.Count == 0)
        {
            context.LogError("WorldSession constructors not found: the time of day cannot be set");
            return;
        }

        foreach (var ctor in ctors) context.Harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(ClockPatch), nameof(Created)));
        context.AddFrame(_ => Hold());
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Created(WorldSession __instance)
    {
        try
        {
            s_session = new WeakReference<WorldSession>(__instance);
            Frozen = null;
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"the clock capture failed: {e}");
        }
    }
#pragma warning restore IDE1006

    /// <summary>Sets the time of day, forward only; false without a clock.</summary>
    internal static bool Set(int hour, int minute)
    {
        if (Clock is not { } clock) return false;

        double wanted = Math.Clamp(hour, 0, 23) + Math.Clamp(minute, 0, 59) / 60.0;
        long day = wanted < clock.HourOfDay ? clock.Day + 1 : clock.Day;
        clock.SetTime(day, wanted);
        if (Frozen is not null) Frozen = wanted;
        return true;
    }

    static void Hold()
    {
        if (Frozen is { } hour && Clock is { } clock) clock.HoldHourOfDay(hour);
    }
}
