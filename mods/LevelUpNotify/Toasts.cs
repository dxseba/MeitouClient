using System.Diagnostics;
using Meitou.Modding;

namespace Meitou.LevelUpNotify;

/// <summary>
/// The shown toasts, aged on the real clock as the original's frame timer: once a frame (<see cref="ModContext.AddFrame"/>) it takes the new level
/// ups of the running world's watch (only the selected character's when <see cref="LevelUpSettings.WholeSquad"/> is off), drops the expired and the
/// oldest past <see cref="LevelUpSettings.MaxShown"/>. <see cref="ToastUi"/> reads <see cref="Current"/> on the same (host) thread.
/// </summary>
static class Toasts
{
    public const int Slots = 64;

    sealed record Toast(string Text, long Born);

    static LevelUpSettings s_settings = new();
    static readonly List<Toast> Shown = [];
    static volatile LevelWatchSystem? s_running;
    static LevelWatchSystem? s_watch;
    static long s_lastNumber;

    /// <summary>Makes the watch system of a new world, the one the toasts follow from then on.</summary>
    public static LevelWatchSystem Watch(LevelUpSettings settings) => s_running = new LevelWatchSystem(settings);

    public static void Install(ModContext context, LevelUpSettings settings)
    {
        s_settings = settings;
        context.AddFrame(Update);
    }

    static void Update(ModFrame frame)
    {
        if (s_running is not { } watch) return;

        long now = Stopwatch.GetTimestamp();
        TakeNew(frame, watch, now);
        Shown.RemoveAll(t => Age(t, now) >= s_settings.Duration);
    }

    static void TakeNew(ModFrame frame, LevelWatchSystem watch, long now)
    {
        if (!ReferenceEquals(watch, s_watch))
        {
            s_watch = watch;
            Shown.Clear();
            s_lastNumber = watch.Recent.LastOrDefault().Number;
            return;
        }

        var selected = s_settings.WholeSquad || frame.Selected.Count == 0 ? (Simulation.CharacterId?)null : frame.Selected[0];
        foreach (var level in watch.Recent)
        {
            if (level.Number <= s_lastNumber) continue;

            s_lastNumber = level.Number;
            if (s_settings.WholeSquad || selected == level.Character) Shown.Add(new(level.Text, now));
        }

        int max = Math.Clamp(s_settings.MaxShown, 1, Slots);
        if (Shown.Count > max) Shown.RemoveRange(0, Shown.Count - max);
    }

    static float Age(Toast toast, long now) => (float)Stopwatch.GetElapsedTime(toast.Born, now).TotalSeconds;

    /// <summary>The toasts now, oldest first, each with its fade: in over the fade, solid, out over the fade (opacity not applied).</summary>
    public static List<(float Fade, string Text)> Current()
    {
        var list = new List<(float, string)>();
        if (!s_settings.Enabled) return list;

        long now = Stopwatch.GetTimestamp();
        float fade = s_settings.FadeSeconds, life = s_settings.Duration;
        foreach (var toast in Shown)
        {
            float age = Age(toast, now);
            float ramp = age < fade ? age / fade : age > life - fade ? (life - age) / fade : 1;
            list.Add((Math.Clamp(ramp, 0, 1), toast.Text));
        }

        return list;
    }
}
