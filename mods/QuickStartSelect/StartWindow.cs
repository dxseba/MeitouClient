using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.QuickStartSelect;

/// <summary>What the window shows over its list or details: nothing, the race filter, the town choice or the relations.</summary>
enum Popup
{
    None,
    Races,
    Towns,
    Relations,
}

/// <summary>
/// The list window in place of the game's "Choose your beginning" window: the game's own widgets are hidden (screen overrides on
/// <c>new-game</c>) and the window's controls are laid out in the start name box, moved over the whole window's rectangle (a share of the screen).
/// Begin and Cancel press the game's own buttons once the start is chosen in its window, so everything the game does on a start stays its own.
/// </summary>
sealed partial class StartWindow(QssSettings settings, ModSettingsGroup group, IReadOnlyList<StartInfo> starts, Func<string, string> tr)
    : IUiExtension
{
    /// <summary>The game widget the window is laid out in (moved and emptied).</summary>
    const string Host = "TitleTextBox";

    /// <summary>A gap of this long without the window arranged means it was closed; the next arrange opens it afresh.</summary>
    static readonly long ReopenTicks = Stopwatch.Frequency / 2;

    /// <summary>A second press on the same row within this long is a double click.</summary>
    static readonly long DoubleClickTicks = Stopwatch.Frequency * 2 / 5;

    string _query = "", _selected = "";
    string? _race;
    int _offset, _descOffset, _popupOffset, _help = -1;
    bool _showOptions;
    Popup _popup;
    long _lastArranged, _lastPress;
    string _lastPressed = "";
    UiRect _anchor;
    float _scale = 1;
    List<StartInfo> _view = [];

    string _act = "", _row = "", _chosen = "", _panel = "", _flat = "";

    public string Id => "dxseba.quickstartselect.ui";

    public void Register(UiRegistration registration)
    {
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxSkin" });
        _flat = registration.RegisterStyle("flat", new() { Skin = "Kenshi_GenericTextBoxFlatSkin" });
        _row = registration.RegisterStyle("row", new() { TextPadding = new UiInsets(6, 0, 4, 0) });
        _chosen = registration.RegisterStyle("chosen", new() { Background = new(1, 1, 1, .18f), TextPadding = new UiInsets(6, 0, 4, 0) });
        foreach (string align in new[] { "Left VCenter", "Center", "Right VCenter" })
            _alignStyles[align] = registration.RegisterStyle("text-" + align.Split(' ')[0].ToLowerInvariant(), new() { TextAlign = align });
        _act = registration.RegisterAction("act", Act);
        PointerPatch.Window = this;
        registration.AddScreenOverrides("new-game", "*", Override);
        registration.AddScreenControls("new-game", Host, UiAnchorPlacement.Inside, Build);
    }

    /// <summary>The window replaces the game's: enabled, the game's members found and some start listed.</summary>
    bool Active => settings.Enabled && GameAccess.StartReady && starts.Count > 0;

    #region Game window

    /// <summary>The game's widgets are hidden; the start name box is moved over the window's rectangle and emptied.</summary>
    UiWidgetOverride? Override(UiScreenContext context)
    {
        try
        {
            if (!Active) return null;

            return context.AnchorWidget == Host
                ? new UiWidgetOverride { Bounds = WindowRect(context.Frame.Viewport), Caption = "" }
                : new UiWidgetOverride { Visible = false };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The window: the layout's share of the screen width, the height's share, centred.</summary>
    UiRect WindowRect(UiRect screen)
    {
        float width = screen.Width * (settings.Wide ? settings.WideWidth : settings.CompactWidth) / 100f;
        float height = screen.Height * settings.WindowHeight / 100f;
        return new((screen.Width - width) / 2, (screen.Height - height) / 2, width, height);
    }

    #endregion

    #region Opening

    /// <summary>
    /// Called each arrange: after a gap the window opened again, so it starts from the top with the first row chosen, no popup, and the
    /// previous town pick dropped (the original's list returns at each opening).
    /// </summary>
    void TouchOpen()
    {
        long now = Stopwatch.GetTimestamp();
        bool reopened = now - _lastArranged > ReopenTicks;
        _lastArranged = now;
        Refresh();
        if (!reopened) return;

        _offset = _descOffset = _popupOffset = 0;
        _popup = Popup.None;
        _help = -1;
        _selected = _view.Count > 0 ? _view[0].Start.Id : "";
        TownPick.Clear();
    }

    /// <summary>The rows shown: search and race filter applied, then the chosen order.</summary>
    void Refresh()
    {
        string query = Difficulty.Normalise(_query);
        var rows = starts.Where(s => (query.Length == 0 || s.SearchName.Contains(query, StringComparison.Ordinal))
            && (_race is null || s.Races.Contains(_race)));
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var sorted = settings.ByDifficulty
            ? rows.OrderBy(s => s.Rank).ThenBy(s => s.Name, comparer)
            : rows.OrderBy(s => s.Name, comparer);
        _view = settings.SortDesc ? [.. sorted.Reverse()] : [.. sorted];
    }

    StartInfo? Selected => starts.FirstOrDefault(s => s.Start.Id == _selected);

    #endregion

    #region Actions

    /// <summary>Every control's action: its argument names what it does ("verb:value").</summary>
    void Act(UiActionContext c)
    {
        try
        {
            string[] parts = c.Argument.Split(':', 2);
            string value = parts.Length > 1 ? parts[1] : "";
            if (c.Kind == UiEventKind.Text)
            {
                Typed(c.Text);
                return;
            }

            Dispatch(parts[0], value, c);
        }
        catch (Exception)
        {
            // a stale argument (the list changed under the press) does nothing
        }
    }

    void Dispatch(string verb, string value, UiActionContext c)
    {
        switch (verb)
        {
            case "row": PressRow(value); break;
            case "layout": SetSetting("Layout", settings.Wide ? "Compact" : "Wide"); break;
            case "sort": SortBy(value); break;
            case "clear": Typed(""); _race = null; break;
            case "popup": Toggle(Enum.Parse<Popup>(value)); break;
            case "race": _race = value.Length == 0 ? null : value; _popup = Popup.None; _offset = 0; break;
            case "town": PickTown(value); break;
            case "scroll": Scroll(value, c); break;
            case "desc": _descOffset = Math.Max(0, _descOffset + int.Parse(value, CultureInfo.InvariantCulture)); break;
            case "pop": _popupOffset = Math.Max(0, _popupOffset + int.Parse(value, CultureInfo.InvariantCulture)); break;
            case "options": _showOptions = !_showOptions; break;
            case "opt": OptionPressed(int.Parse(value, CultureInfo.InvariantCulture), c); break;
            case "less": Step(int.Parse(value, CultureInfo.InvariantCulture), -1); break;
            case "more": Step(int.Parse(value, CultureInfo.InvariantCulture), 1); break;
            case "random": RandomStart(); break;
            case "begin": Begin(); break;
            case "cancel": GameAccess.Press("CancelButton"); break;
        }
    }

    void Typed(string text)
    {
        _query = text;
        _offset = 0;
    }

    void Toggle(Popup popup)
    {
        _popup = _popup == popup ? Popup.None : popup;
        _popupOffset = 0;
    }

    /// <summary>A sort button: the other order chosen ascending, or the chosen one reversed.</summary>
    void SortBy(string sort)
    {
        if (settings.Sort == sort) SetSetting("SortDesc", !settings.SortDesc);
        else
        {
            SetSetting("Sort", sort);
            SetSetting("SortDesc", false);
        }
    }

    /// <summary>A value the window's buttons change, kept as the settings page keeps it.</summary>
    void SetSetting(string key, object value)
    {
        if (group.Settings.FirstOrDefault(s => s.Key == key) is { } setting) setting.Set(value);
    }

    /// <summary>A row press chooses its start; the second press within the double-click time begins.</summary>
    void PressRow(string id)
    {
        long now = Stopwatch.GetTimestamp();
        bool twice = id == _lastPressed && now - _lastPress < DoubleClickTicks;
        _lastPressed = id;
        _lastPress = now;
        if (_selected != id) _descOffset = 0;
        _selected = id;
        if (twice) Begin();
    }

    void PickTown(string town)
    {
        TownPick.Set(_selected, town.Length == 0 ? null : town);
        _popup = Popup.None;
    }

    /// <summary>A random listed start other than the chosen one (only the choice changes, as a row click).</summary>
    void RandomStart()
    {
        var others = _view.Where(s => s.Start.Id != _selected).ToList();
        if (others.Count == 0) return;

        _selected = others[Random.Shared.Next(others.Count)].Start.Id;
        _descOffset = 0;
        int index = _view.FindIndex(s => s.Start.Id == _selected);
        int rows = ListRows();
        if (index < _offset || index >= _offset + rows) _offset = Math.Max(0, index - rows / 2);
    }

    /// <summary>Begin: the start chosen in the game's window, then (asked first unless the setting is off) its own Begin pressed.</summary>
    void Begin()
    {
        if (Selected is not { } start || !GameAccess.Choose(start.Start.Id)) return;

        // In play the game's Begin asks itself.
        if (!settings.ConfirmBegin || GameAccess.InPlay() || !GameAccess.Ask(tr("Begin"), tr("Begin a new game with this start?"), PressBegin))
            PressBegin();
    }

    static void PressBegin() => GameAccess.Press("BeginButton");

    #endregion

    #region Pointer

    /// <summary>The description's rectangle as last laid out (window pixels).</summary>
    UiRect _descArea;

    /// <summary>Whether the pointer (as last arranged) is over <paramref name="r"/> (window pixels).</summary>
    bool Hovered(UiRect r) => PointerPatch.Mouse is { } m && new UiRect(_anchor.X + r.X, _anchor.Y + r.Y, r.Width, r.Height).Contains(m.X, m.Y);

    /// <summary>
    /// A wheel step at <paramref name="p"/> (screen pixels), above 0 up: over the popup, the list or the description it scrolls that by three
    /// rows a step; anywhere else over the open window it is swallowed. False while the window is not shown.
    /// </summary>
    public bool Wheel(System.Numerics.Vector2 p, float steps)
    {
        if (!Active || System.Diagnostics.Stopwatch.GetTimestamp() - _lastArranged > ReopenTicks) return false;
        if (!_anchor.Contains(p.X, p.Y)) return false;

        int rows = -(int)MathF.Round(steps * 3);
        if (_popup != Popup.None && Hovered(_geo.List)) _popupOffset = Math.Max(0, _popupOffset + rows);
        else if (Hovered(_geo.List)) _offset = Math.Max(0, _offset + rows);
        else if (Hovered(_descArea)) _descOffset = Math.Max(0, _descOffset + rows);
        return true;
    }

    #endregion

    #region Colours

    /// <summary>Dark text on the vanilla parchment (the Faithful look), light on Meitou's dark windows, unless the setting picks one.</summary>
    bool DarkText(UiScreenContext context) => settings.TextColour switch
    {
        "dark" => true,
        "light" => false,
        _ => context.Frame.Mode == UiMode.Faithful,
    };

    Vector4 _ink;

    static readonly Vector4 DarkInk = new(.078f, .031f, .024f, 1), LightInk = new(.91f, .86f, .74f, 1);

    #endregion
}
