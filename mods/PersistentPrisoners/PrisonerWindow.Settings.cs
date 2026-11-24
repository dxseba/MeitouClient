using System.Globalization;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// The original's settings under the overview (<b>Verified</b>, DLL strings and languages.ini): "Settings", the "Allow starvation: ON/OFF" toggle,
/// "Minimum Nu:" with its box and "(1 - 300)", "Diagnostic logging", the shortcut ("{shortcut}  |  Change", then "Press a shortcut (Esc:
/// cancel)") and Apply, which checks the number ("Minimum Nu must be a whole number from 1 to 300.") and makes the choices active ("Saved. Settings
/// are now active."). Port: the choices are the Mod settings page's own (set through its group, kept as the page keeps them) and the shortcut is
/// the key action's (<see cref="ModKeyAction.Rebind"/>, kept as the Controls page keeps it).
/// </summary>
sealed partial class PrisonerWindow
{
    const string Invalid = "Minimum Nu must be a whole number from 1 to 300.";
    const string Saved = "Saved. Settings are now active.";

    string _toggle = "", _typed = "", _apply = "", _change = "";
    bool _starvation, _diagnostics;
    string _minimum = "", _message = "";
    volatile bool _capturing;
    Binding? _shortcut;

    /// <summary>The overview's key action, set once it is added.</summary>
    public ModKeyAction? Action { get; set; }

    void RegisterSettings(UiRegistration registration)
    {
        _toggle = registration.RegisterAction("toggle", c => Flip(c.Argument));
        _typed = registration.RegisterAction("minimum", c => _minimum = c.Text);
        _apply = registration.RegisterAction("apply", _ => Apply());
        _change = registration.RegisterAction("shortcut", _ => _capturing = !_capturing);
        registration.AddKeyInput(Capture);
    }

    /// <summary>The pending choices start from the active settings and keys each time the window opens.</summary>
    void ResetPending()
    {
        var settings = PersistentPrisonersMod.Settings;
        _starvation = settings.Starvation;
        _diagnostics = settings.Diagnostics;
        _minimum = settings.MinimumNutrition.ToString(CultureInfo.InvariantCulture);
        var keys = Action?.Keys.Where(k => !k.IsMouse).ToList() ?? [];
        _shortcut = keys.Count > 0 ? keys[0] : null;
        _message = "";
    }

    void Flip(string which)
    {
        if (which == "starvation") _starvation = !_starvation;
        else _diagnostics = !_diagnostics;
    }

    /// <summary>
    /// While capturing, the next key with the modifiers held becomes the pending shortcut (a modifier alone waits; Esc cancels); every key is
    /// taken from the game meanwhile.
    /// </summary>
    bool Capture(UiKeyInput input)
    {
        if (!_capturing || !_open) return false;

        if (input.Cancel) _capturing = false;
        else if (!IsModifier(input.Key))
        {
            _shortcut = Binding.Of(input.Key).With(input.Modifiers);
            _capturing = false;
        }

        return true;
    }

    static bool IsModifier(Key key) =>
        key is Key.LeftShift or Key.RightShift or Key.LeftControl or Key.RightControl or Key.LeftAlt or Key.RightAlt;

    void Apply()
    {
        if (_shortcut is not { } shortcut)
        {
            _message = _texts.Get("message.shortcut_first", "Choose a shortcut first.");
            return;
        }

        if (!int.TryParse(_minimum.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int minimum) || minimum is < 1 or > 300)
        {
            _message = _texts.Get("message.minimum_invalid", Invalid);
            return;
        }

        if (!Rebind(shortcut)) return;

        Set(nameof(PrisonerSettings.Starvation), _starvation);
        Set(nameof(PrisonerSettings.MinimumNutrition), minimum);
        Set(nameof(PrisonerSettings.Diagnostics), _diagnostics);
        _message = _texts.Get("message.saved", Saved);
        _log(string.Create(CultureInfo.InvariantCulture,
            $"PP: Settings saved. AllowStarvation={(_starvation ? 1 : 0)} MinimumNu={minimum} Diagnostics={(_diagnostics ? 1 : 0)} Shortcut={shortcut}"));
    }

    /// <summary>Binds the overview to <paramref name="shortcut"/> unless it already is; false (with the error shown) when it is refused.</summary>
    bool Rebind(Binding shortcut)
    {
        if (Action is not { } action || action.Keys.Contains(shortcut)) return true;
        if (action.Rebind(shortcut)) return true;

        _message = _texts.Get("error.shortcut", "Invalid keyboard shortcut.");
        return false;
    }

    void Set(string key, object value) => _group.Settings.First(s => s.Key == key).Set(value);

    IEnumerable<UiNode> SettingsLines()
    {
        yield return Text("prisoners-settings", _texts.Get("settings.title", "Settings"), _styles.Text);
        yield return ChoicesRow();
        yield return ShortcutRow();

        if (_message != "") yield return Text("prisoners-message", _message, _styles.Dim);
    }

    UiNode ChoicesRow()
    {
        var row = Row("prisoners-settings-row");
        string starvation = _starvation
            ? _texts.Get("settings.starvation_on", "Allow starvation: ON")
            : _texts.Get("settings.starvation_off", "Allow starvation: OFF");
        row.Children.Add(Button("prisoners-starvation", starvation, _toggle, "starvation", true, 190));
        row.Children.Add(Label("prisoners-minimum-label", _texts.Get("settings.minimum", "Minimum Nu:"), 100, _styles.Text));
        row.Children.Add(new UiNode("prisoners-minimum", UiNodeKind.TextBox)
        {
            Placement = new(60, Line), Text = _minimum, Action = _typed, MaxLength = 3, TextScale = _scale,
        });
        row.Children.Add(Label("prisoners-range", _texts.Get("settings.range", "(1 - 300)"), 80, _styles.Dim));
        string diagnostics = (_diagnostics ? "[x] " : "[ ] ") + _texts.Get("settings.diagnostics", "Diagnostic logging");
        row.Children.Add(Button("prisoners-diagnostics", diagnostics, _toggle, "diagnostics", true, 190));
        row.Children.Add(Button("prisoners-apply", _texts.Get("button.apply", "Apply"), _apply, "", true, 90));
        return row;
    }

    UiNode ShortcutRow()
    {
        var row = Row("prisoners-shortcut-row");
        string text = _capturing
            ? _texts.Get("shortcut.capture", "Press a shortcut (Esc: cancel)")
            : _texts.Get("shortcut.change", "{shortcut}  |  Change", ("shortcut", Shown(_shortcut)));
        row.Children.Add(Button("prisoners-shortcut", text, _change, "", true, 320));
        return row;
    }

    /// <summary>A shortcut as shown: the language's Ctrl, Shift and Alt (and Tab), joined by "+"; empty for none.</summary>
    string Shown(Binding? binding)
    {
        if (binding is not { } b) return "";

        var parts = new List<string>();
        if ((b.Modifiers & KeyModifiers.Ctrl) != 0) parts.Add(_texts.Get("shortcut.ctrl", "Ctrl"));
        if ((b.Modifiers & KeyModifiers.Shift) != 0) parts.Add(_texts.Get("shortcut.shift", "Shift"));
        if ((b.Modifiers & KeyModifiers.Alt) != 0) parts.Add(_texts.Get("shortcut.alt", "Alt"));
        parts.Add(b.Key == Key.Tab ? _texts.Get("shortcut.tab", "Tab") : b.Key.ToString());
        return string.Join("+", parts);
    }
}
