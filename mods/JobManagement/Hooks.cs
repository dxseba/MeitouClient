using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Meitou.JobManagement;

/// <summary>
/// The interface hooks. Public: <see cref="ModContext.AddFrame"/> runs the window's steps once a frame on the snapshot on screen. Still Harmony
/// (no public hook does it): a postfix on the private <c>PlayerInterface.BindUi</c> keeps the session, the only way to pause the clock and put its
/// speeds back; and a prefix on <c>GameHotkeys.Key</c> takes the window key (Ctrl+J by default) before the game's own binding of the key sees it
/// (J is the Rescue key's second binding; a mod key action fires beside the game's binding and has no modifiers of its own).
/// </summary>
static class Hooks
{
    static FieldInfo? s_sessionField;

    /// <summary>The interface's session, as the last bind found it; null before the first.</summary>
    internal static WorldSession? Session { get; private set; }

    public static void Install(ModContext context)
    {
        context.AddFrame(frame => JobManagementMod.Window.Frame(frame.Snapshot));

        var player = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = AccessTools.Field(player, "Session") ?? throw new MissingFieldException(player.FullName, "Session");
        var bind = AccessTools.Method(player, "BindUi") ?? throw new MissingMethodException(player.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(Hooks), nameof(Bound)));

        var hotkeys = AccessTools.TypeByName("Meitou.Game.Ui.GameHotkeys") ?? throw new TypeLoadException("Meitou.Game.Ui.GameHotkeys");
        var key = AccessTools.Method(hotkeys, "Key", [typeof(Key), typeof(bool)]) ?? throw new MissingMethodException(hotkeys.FullName, "Key");
        context.Harmony.Patch(key, prefix: new HarmonyMethod(typeof(Hooks), nameof(BeforeKey)));
    }

    #pragma warning disable IDE1006 // Harmony binds __instance by name
    static void Bound(object __instance)
    {
        if (s_sessionField?.GetValue(__instance) is WorldSession session) Session = session;
    }
    #pragma warning restore IDE1006

    /// <summary>The window key (the setting, as the fork's <c>Hotkey</c>) toggles the window instead of reaching the game's bindings.</summary>
    static bool BeforeKey(Key key)
    {
        var settings = JobManagementMod.Settings;
        if (!settings.Enabled || Session is not { } session || WindowKey.Parse(settings.Hotkey) is not { } wanted) return true;
        if (key != wanted.Key || !wanted.Held(session.Input)) return true;

        JobManagementMod.Window.Toggle();
        return false;
    }
}

/// <summary>
/// A window key as the fork's <c>Hotkey</c> writes it: modifiers <c>CTRL</c>, <c>ALT</c>, <c>SHIFT</c> joined to a key name by <c>+</c>
/// (<c>CTRL+J</c>, <c>F6</c>); <c>NONE</c> or anything not understood is no key.
/// </summary>
readonly record struct WindowKey(Key Key, bool Ctrl, bool Alt, bool Shift)
{
    public static WindowKey? Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        bool ctrl = false, alt = false, shift = false;
        foreach (string modifier in parts[..^1])
        {
            switch (modifier.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": ctrl = true; break;
                case "ALT": alt = true; break;
                case "SHIFT": shift = true; break;
                default: return null;
            }
        }

        return KeyOf(parts[^1]) is { } key ? new WindowKey(key, ctrl, alt, shift) : null;
    }

    /// <summary>A key by its name (a digit as itself); null for <c>NONE</c> or an unknown name.</summary>
    static Key? KeyOf(string name)
    {
        if (name is [>= '0' and <= '9']) name = "D" + name;
        if (name.Equals("NONE", StringComparison.OrdinalIgnoreCase) || name.All(char.IsDigit)) return null;

        return Enum.TryParse<Key>(name, ignoreCase: true, out var key) ? key : null;
    }

    /// <summary>Whether every modifier the key names is held.</summary>
    public bool Held(InputState input) =>
        (!Ctrl || input.IsHeld(Key.LeftControl) || input.IsHeld(Key.RightControl))
        && (!Alt || input.IsHeld(Key.LeftAlt) || input.IsHeld(Key.RightAlt))
        && (!Shift || input.IsHeld(Key.LeftShift) || input.IsHeld(Key.RightShift));
}
