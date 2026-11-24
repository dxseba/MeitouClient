using Meitou.Game.Host.Input;
using KeyBinding = Meitou.Game.Host.Input.Binding;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The <c>HoldKey</c> setting in the original's syntax (README "Facts"): <c>NONE</c> (or empty) for always; a modifier alone (<c>ALT</c>,
/// <c>CTRL</c>, <c>SHIFT</c>, also <c>L</c>/<c>R</c> sided); a key or mouse button (<c>F</c>, <c>F5</c>, <c>SPACE</c>, <c>PAGEUP</c>, <c>X1</c>,
/// <c>MMB</c>...) with any modifiers before it joined by <c>+</c> (<c>CTRL+B</c>). A key becomes the binding of the mod's hold key action (a chord
/// with its modifiers); a mouse button binds plain and the modifiers are tested apart. <see cref="Error"/> names what did not read.
/// </summary>
readonly record struct HoldKey(bool Always, KeyModifiers Modifiers, Binding? Binding, string? Error)
{
    static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ESC"] = Key.Escape, ["RETURN"] = Key.Enter, ["DEL"] = Key.Delete, ["INS"] = Key.Insert, ["PGUP"] = Key.PageUp, ["PGDN"] = Key.PageDown,
    };

    static readonly Dictionary<string, MouseButton> Buttons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MMB"] = MouseButton.Middle, ["X1"] = MouseButton.Button4, ["X2"] = MouseButton.Button5,
    };

    public static HoldKey Parse(string text)
    {
        string t = text.Trim();
        if (t.Length == 0 || t.Equals("NONE", StringComparison.OrdinalIgnoreCase) || t.Equals("always", StringComparison.OrdinalIgnoreCase))
            return new(true, KeyModifiers.None, null, null);

        var modifiers = KeyModifiers.None;
        KeyBinding? binding = null;
        foreach (string part in t.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Modifier(part) is { } m) modifiers |= m;
            else if (binding is not null || Bound(part) is not { } b) return Fallback($"'{text}' does not read as a key");
            else binding = b;
        }

        if (binding is { IsMouse: false } key && modifiers != KeyModifiers.None) binding = key.With(modifiers);
        return modifiers == KeyModifiers.None && binding is null ? Fallback($"'{text}' names no key") : new(false, modifiers, binding, null);
    }

    /// <summary>ALT, the original's default, for a text that does not read.</summary>
    static HoldKey Fallback(string error) => new(false, KeyModifiers.Alt, null, error);

    static KeyModifiers? Modifier(string part) => part.ToUpperInvariant() switch
    {
        "ALT" or "LALT" or "RALT" => KeyModifiers.Alt,
        "CTRL" or "LCTRL" or "RCTRL" or "CONTROL" => KeyModifiers.Ctrl,
        "SHIFT" or "LSHIFT" or "RSHIFT" => KeyModifiers.Shift,
        _ => null,
    };

    /// <summary>A key or mouse button by its name; null for an unknown one, the left and right mouse buttons (the game's clicks) and Escape and F6
    /// (keys a mod's action may not take).</summary>
    static KeyBinding? Bound(string part)
    {
        if (Buttons.TryGetValue(part, out var button)) return KeyBinding.Of(button);

        Key key;
        if (Aliases.TryGetValue(part, out var alias)) key = alias;
        else if (part.Length == 1 && char.IsAsciiDigit(part[0])) key = Key.D0 + (part[0] - '0');
        else if (!Enum.TryParse(part, ignoreCase: true, out key) || int.TryParse(part, out _)) return null;

        return key is Key.Escape or Key.F6 or Key.LeftAlt or Key.RightAlt or Key.LeftControl or Key.RightControl or Key.LeftShift or Key.RightShift
            ? null
            : KeyBinding.Of(key);
    }
}
