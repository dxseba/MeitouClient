using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Meitou.JobManagement;

/// <summary>
/// The interface hooks. Public: <see cref="ModContext.AddFrame"/> runs the window's steps once a frame on the snapshot on screen, and a key action
/// (<see cref="ModContext.AddKeyAction"/>) toggles the window: by default the <c>Hotkey</c> setting's key as a chord (Ctrl+J), which fires before
/// the game's own binding of the key (J is the Rescue key's second binding).
/// </summary>
static class Hooks
{
    const string KeyName = "window", KeyLabel = "Job Management window";

    public static void Install(ModContext context)
    {
        context.AddFrame(frame => JobManagementMod.Window.Frame(frame.Snapshot, frame.Ctrl));
        AddWindowKey(context);
    }

    /// <summary>The window's key action; a setting key the Controls page keeps for itself (Escape, F6, a modifier) falls back to Ctrl+J.</summary>
    static void AddWindowKey(ModContext context)
    {
        try
        {
            context.AddKeyAction(KeyName, KeyLabel, Pressed, Defaults(JobManagementMod.Settings.Hotkey));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            context.Log($"window key '{JobManagementMod.Settings.Hotkey}' refused ({e.Message}); default Ctrl+J");
            context.AddKeyAction(KeyName, KeyLabel, Pressed, Binding.Of(Key.J).With(KeyModifiers.Ctrl));
        }
    }

    /// <summary>The key action's default: the setting's key as a chord of its modifiers (a plain key without any); none for <c>NONE</c>.</summary>
    static Binding[] Defaults(string hotkey) => WindowKey.Parse(hotkey) is { } key ? [Binding.Of(key.Key).With(key.Modifiers)] : [];

    /// <summary>The window key toggles the window.</summary>
    static void Pressed(ModKeyContext press)
    {
        if (JobManagementMod.Settings.Enabled) JobManagementMod.Window.Toggle();
    }
}

/// <summary>
/// A window key as the fork's <c>Hotkey</c> writes it: modifiers <c>CTRL</c>, <c>ALT</c>, <c>SHIFT</c> joined to a key name by <c>+</c>
/// (<c>CTRL+J</c>, <c>F9</c>); <c>NONE</c> or anything not understood is no key.
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

    /// <summary>The modifiers the key names, for a chord.</summary>
    public KeyModifiers Modifiers =>
        (Ctrl ? KeyModifiers.Ctrl : KeyModifiers.None) | (Alt ? KeyModifiers.Alt : KeyModifiers.None) | (Shift ? KeyModifiers.Shift : KeyModifiers.None);
}
