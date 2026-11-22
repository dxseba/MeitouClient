using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LevelUpNotify;

/// <summary>
/// The shown toasts, aged on the real clock as the original's frame timer: a postfix on the interface's <c>BindUi</c> takes the new level ups
/// (only the selected character's when <see cref="LevelUpSettings.WholeSquad"/> is off), drops the expired and the oldest past
/// <see cref="LevelUpSettings.MaxShown"/>, and binds each as "alpha|text" for <see cref="ToastUi"/>.
/// </summary>
static class Toasts
{
    public const string Key = "levelup.toast.";
    public const int Slots = 64;

    sealed record Toast(string Text, long Born);

    static LevelUpSettings s_settings = new();
    static FieldInfo? s_sessionField;
    static MethodInfo? s_selected;
    static readonly List<Toast> Shown = [];
    static LevelWatchSystem? s_watch;
    static long s_lastNumber;

    public static void Install(ModContext context, LevelUpSettings settings)
    {
        s_settings = settings;
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        s_sessionField = fields.Single(f => f.FieldType == typeof(WorldSession));
        s_selected = AccessTools.Method(type, "Selected") ?? throw new MissingMethodException(type.FullName, "Selected");
        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(Toasts), nameof(Bound)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        if (s_sessionField?.GetValue(__instance) is not WorldSession session) return;
        if (session.World.Systems.OfType<LevelWatchSystem>().FirstOrDefault() is not { } watch) return;

        long now = Stopwatch.GetTimestamp();
        TakeNew(__instance, watch, now);
        Expire(now);
        Bind(__result, now);
    }

    static void TakeNew(object player, LevelWatchSystem watch, long now)
    {
        if (!ReferenceEquals(watch, s_watch))
        {
            s_watch = watch;
            Shown.Clear();
            s_lastNumber = watch.Recent.LastOrDefault().Number;
            return;
        }

        var selected = s_settings.WholeSquad ? null : (s_selected!.Invoke(player, null) as IReadOnlyList<CharacterSnapshot>)?.FirstOrDefault();
        foreach (var level in watch.Recent)
        {
            if (level.Number <= s_lastNumber) continue;

            s_lastNumber = level.Number;
            if (s_settings.WholeSquad || selected?.Id == level.Character) Shown.Add(new(level.Text, now));
        }

        int max = Math.Clamp(s_settings.MaxShown, 1, Slots);
        if (Shown.Count > max) Shown.RemoveRange(0, Shown.Count - max);
    }

    static void Expire(long now) => Shown.RemoveAll(t => Age(t, now) >= s_settings.Duration);

    static float Age(Toast toast, long now) => (float)Stopwatch.GetElapsedTime(toast.Born, now).TotalSeconds;

    /// <summary>Each toast's alpha: in over the fade, solid, out over the fade, times the opacity.</summary>
    static void Bind(UiBindings bindings, long now)
    {
        if (!s_settings.Enabled) return;

        float fade = s_settings.FadeSeconds, life = s_settings.Duration, opacity = Math.Clamp(s_settings.Opacity / 100f, 0, 1);
        for (int i = 0; i < Shown.Count; i++)
        {
            float age = Age(Shown[i], now);
            float ramp = age < fade ? age / fade : age > life - fade ? (life - age) / fade : 1;
            float alpha = Math.Clamp(ramp, 0, 1) * opacity;
            bindings.Text[Key + i] = string.Create(CultureInfo.InvariantCulture, $"{alpha:0.###}|{Shown[i].Text}");
        }
    }
}
