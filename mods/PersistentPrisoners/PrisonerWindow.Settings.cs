using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// The original's settings under the overview (<b>Verified</b>, DLL strings and languages.ini [en]): "Settings", the "Allow starvation: ON/OFF"
/// toggle, "Minimum Nu:" with its box and "(1 - 300)", "Diagnostic logging", and Apply, which checks the number ("Minimum Nu must be a whole number
/// from 1 to 300.") and makes the choices active ("Saved. Settings are now active."). Port: the choices are the Mod settings page's own (set through
/// its group, kept as the page keeps them); the shortcut and language rows are not ported (the Controls page binds the key).
/// </summary>
sealed partial class PrisonerWindow
{
    const string Invalid = "Minimum Nu must be a whole number from 1 to 300.";
    const string Saved = "Saved. Settings are now active.";

    string _toggle = "", _typed = "", _apply = "";
    bool _starvation, _diagnostics;
    string _minimum = "", _message = "";

    void RegisterSettings(UiRegistration registration)
    {
        _toggle = registration.RegisterAction("toggle", c => Flip(c.Argument));
        _typed = registration.RegisterAction("minimum", c => _minimum = c.Text);
        _apply = registration.RegisterAction("apply", _ => Apply());
    }

    /// <summary>The pending choices start from the active settings each time the window opens.</summary>
    void ResetPending()
    {
        var settings = PersistentPrisonersMod.Settings;
        _starvation = settings.Starvation;
        _diagnostics = settings.Diagnostics;
        _minimum = settings.MinimumNutrition.ToString(CultureInfo.InvariantCulture);
        _message = "";
    }

    void Flip(string which)
    {
        if (which == "starvation") _starvation = !_starvation;
        else _diagnostics = !_diagnostics;
    }

    void Apply()
    {
        if (!int.TryParse(_minimum.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int minimum) || minimum is < 1 or > 300)
        {
            _message = Invalid;
            return;
        }

        Set(nameof(PrisonerSettings.Starvation), _starvation);
        Set(nameof(PrisonerSettings.MinimumNutrition), minimum);
        Set(nameof(PrisonerSettings.Diagnostics), _diagnostics);
        _message = Saved;
        _log(string.Create(CultureInfo.InvariantCulture,
            $"PP: Settings saved. AllowStarvation={(_starvation ? 1 : 0)} MinimumNu={minimum} Diagnostics={(_diagnostics ? 1 : 0)}"));
    }

    void Set(string key, object value) => _group.Settings.First(s => s.Key == key).Set(value);

    IEnumerable<UiNode> SettingsLines()
    {
        yield return Text("prisoners-settings", "Settings", _styles.Text);

        var row = Row("prisoners-settings-row");
        string starvation = _starvation ? "Allow starvation: ON" : "Allow starvation: OFF";
        row.Children.Add(Button("prisoners-starvation", starvation, _toggle, "starvation", true, 190));
        row.Children.Add(Label("prisoners-minimum-label", "Minimum Nu:", 100, _styles.Text));
        row.Children.Add(new UiNode("prisoners-minimum", UiNodeKind.TextBox)
        {
            Placement = new(60, Line), Text = _minimum, Action = _typed, MaxLength = 3,
        });
        row.Children.Add(Label("prisoners-range", "(1 - 300)", 80, _styles.Dim));
        row.Children.Add(Button("prisoners-diagnostics", (_diagnostics ? "[x] " : "[ ] ") + "Diagnostic logging", _toggle, "diagnostics", true, 190));
        row.Children.Add(Button("prisoners-apply", "Apply", _apply, "", true, 90));
        yield return row;

        if (_message != "") yield return Text("prisoners-message", _message, _styles.Dim);
    }
}
