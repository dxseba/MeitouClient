using System.Globalization;
using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.OrganizeCraftingStations;

/// <summary>
/// The original's search panel on the overview's Research and Crafting tabs (<c>AddScreenControls("overview", "ResearchTab" / "CraftTab",
/// Inside)</c>, the tab page being the parent the original finds from its panels): a <c>::</c> drag handle, the query box with its
/// "Search items..." hint, a clear button inside the box and the "shown / listed" counter, sized and placed as the original's
/// (<see cref="Layout"/>). Ctrl+F or / puts the cursor in the box; Ctrl+Shift+F8 turns the controls off and on for the session.
/// </summary>
sealed class SearchBar(ModContext mod, CraftingSettings settings, ModSettingsGroup group) : IUiExtension
{
    const string ResearchTab = "ResearchTab", CraftTab = "CraftTab";

    /// <summary>
    /// Whether the controls are on (Ctrl+Shift+F8; on at start, not saved, as the original's).
    /// </summary>
    public bool ControlsOn { get; private set; } = true;

    /// <summary>
    /// Frames counted by the mod's frame callback, and the frame the panel was last built in.
    /// </summary>
    int _frame, _builtFrame = -10;

    /// <summary>
    /// The tab the panel was last built on; a change of tab clears the query, as the original's new target does.
    /// </summary>
    string _tab = "";

    /// <summary>
    /// A request to put the cursor in the box at the next build (auto-focus, Ctrl+F, /).
    /// </summary>
    bool _focus;

    /// <summary>
    /// How far the handle has been dragged since the press.
    /// </summary>
    Vector2 _dragged;

    /// <summary>
    /// The panel's last place in its tab page and that page's size, for the drag's release.
    /// </summary>
    Vector2 _place, _page;

    string _panel = "", _text = "", _hint = "", _count = "", _button = "", _typed = "", _clear = "", _drag = "";

    public string Id => "dxseba.organizecraftingstations.ui";

    public void Register(UiRegistration registration)
    {
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxFlatSkin" });
        _button = registration.RegisterStyle("button", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" });
        _text = registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1) });
        _hint = registration.RegisterStyle("hint", new() { Text = new(.6f, .58f, .52f, 1) });
        _count = registration.RegisterStyle("count", new() { Text = new(.91f, .86f, .74f, 1), TextAlign = "Right VCenter" });
        _typed = registration.RegisterAction("typed", c => Typed(c.Text));
        _clear = registration.RegisterAction("clear", _ => Typed(""));
        _drag = registration.RegisterAction("drag", Dragged);
        registration.AddScreenControls("overview", ResearchTab, UiAnchorPlacement.Inside, c => Build(c, ResearchTab));
        registration.AddScreenControls("overview", CraftTab, UiAnchorPlacement.Inside, c => Build(c, CraftTab));
        registration.AddKeyInput(Key);
    }

    /// <summary>
    /// Counts a frame (the mod's frame callback).
    /// </summary>
    public void Frame() => _frame++;

    #region Actions

    void Typed(string text)
    {
        if (text == ListFilter.Typed) return;

        ListFilter.SetQuery(text);
        if (settings.DebugLogging) mod.Log($"search input changed query=\"{text}\"");
    }

    /// <summary>
    /// A drag of the handle moves the panel; its release keeps the place (the original's <c>searchInputLeft</c> / <c>Top</c>).
    /// </summary>
    void Dragged(UiActionContext c)
    {
        if (c.Kind == UiEventKind.Drag)
        {
            _dragged += c.Delta;
            return;
        }

        if (c.Kind != UiEventKind.Release || _dragged == Vector2.Zero) return;

        _dragged = Vector2.Zero;
        Set(nameof(CraftingSettings.Left), (int)MathF.Round(_place.X));
        Set(nameof(CraftingSettings.Top), (int)MathF.Round(_place.Y));
        Set(nameof(CraftingSettings.PositionCustomized), true);
        if (settings.DebugLogging) mod.Log($"search container drag finalized left={_place.X} top={_place.Y}");
    }

    void Set(string key, object value) => group.Settings.First(s => s.Key == key).Set(value);

    /// <summary>
    /// Ctrl+Shift+F8 turns the controls off and on (left to the game and the other mods too); Ctrl+F or / while the panel shows asks for the cursor.
    /// </summary>
    bool Key(UiKeyInput input)
    {
        if (input.Kind != UiKeyInputKind.Down) return false;

        if (input.Key == Game.Host.Input.Key.F8 && input.Ctrl && input.Shift && !input.Alt)
        {
            ControlsOn = !ControlsOn;
            mod.Log(ControlsOn ? "controls toggled ON" : "controls toggled OFF");
            return false;
        }

        bool shown = _frame - _builtFrame <= 2;
        bool find = input.Key == Game.Host.Input.Key.F && input.Ctrl && !input.Shift && !input.Alt;
        bool slash = input.Key == Game.Host.Input.Key.Slash && !input.Ctrl && !input.Alt;
        if (!shown || !(find || slash)) return false;

        _focus = true;
        return true;
    }

    #endregion

    #region Layout

    /// <summary>
    /// The panel on <paramref name="tab"/>'s page, or nothing while the mod or its controls are off.
    /// </summary>
    UiNode? Build(UiScreenContext context, string tab)
    {
        if (!settings.Enabled || !ControlsOn) return null;

        Shown(tab);
        var page = new Vector2(context.Anchor.Width, context.Anchor.Height);
        var layout = Layout.Of(page, settings);
        _page = page;
        _place = Place(layout, page);
        var counts = tab == ResearchTab ? ListFilter.ResearchCount : ListFilter.CraftCount;
        return Panel(context, layout, counts);
    }

    /// <summary>
    /// Notes the build: a tab shown after a frame without the panel, or another tab, asks for the cursor (auto-focus); a change of tab clears the
    /// query.
    /// </summary>
    void Shown(string tab)
    {
        if ((_frame - _builtFrame > 1 || tab != _tab) && settings.AutoFocus) _focus = true;
        if (tab != _tab && _tab.Length > 0)
        {
            ListFilter.SetQuery("");
            if (settings.DebugLogging) mod.Log("search query reset reason=target_changed");
        }

        _tab = tab;
        _builtFrame = _frame;
    }

    /// <summary>
    /// The panel's top-left in its page: the kept place or the original's default (16 from the page's right and top), moved by a drag in
    /// progress, kept 8 inside the page.
    /// </summary>
    Vector2 Place(Layout layout, Vector2 page)
    {
        var at = settings.PositionCustomized ? new Vector2(settings.Left, settings.Top) : new Vector2(page.X - layout.Width - 16, 16);
        at += _dragged;
        float x = Math.Min(Math.Max(8, at.X), Math.Max(8, page.X - layout.Width - 8));
        float y = Math.Min(Math.Max(8, at.Y), Math.Max(8, page.Y - layout.Height - 8));
        return new(x, y);
    }

    UiNode Panel(UiScreenContext context, Layout l, (int Visible, int Total) counts)
    {
        var panel = new UiNode("otcs-panel") { Placement = new(l.Width, l.Height, Left: _place.X, Top: _place.Y), Style = _panel };
        panel.Children.Add(new UiNode("otcs-drag", UiNodeKind.Button)
        {
            Placement = new(28, l.Field, Left: 8, Top: 8), Text = "::", Style = _button, Action = _drag,
            Pointer = UiPointerEvents.Drag | UiPointerEvents.Release,
        });
        if (l.Count > 0) panel.Children.Add(Counter(context, l, counts));

        bool focus = _focus;
        _focus = false;
        panel.Children.Add(new UiNode("otcs-edit", UiNodeKind.TextBox)
        {
            Placement = new(l.Edit, l.Field, Left: 42, Top: 8), Text = ListFilter.Typed, Action = _typed, MaxLength = 64, Focus = focus,
        });
        if (ListFilter.Typed.Length == 0)
        {
            panel.Children.Add(new UiNode("otcs-hint", UiNodeKind.Label)
            {
                Placement = new(Math.Max(0, l.Edit - 16), l.Field, Left: 52, Top: 9), Text = "Search items...", Style = _hint,
            });
        }

        if (settings.ShowClearButton)
        {
            panel.Children.Add(new UiNode("otcs-clear", UiNodeKind.Button)
            {
                Placement = new(l.Clear, l.Clear, Left: l.Edit - l.Clear + 42, Top: (l.Field - l.Clear) / 2 + 8), Text = "x", Style = _button,
                Action = _clear,
            });
        }

        return panel;
    }

    /// <summary>
    /// "shown / listed", its width the text's plus 8 (at least 44, at most the counter's room), its right edge kept.
    /// </summary>
    UiNode Counter(UiScreenContext context, Layout l, (int Visible, int Total) counts)
    {
        string text = counts.Visible.ToString(CultureInfo.InvariantCulture) + " / " + counts.Total.ToString(CultureInfo.InvariantCulture);
        float measured = context.Frame.Measure(text).X;
        float width = Math.Min(l.Count, Math.Max(Math.Max(5 * text.Length + 6, 44), measured + 8));
        float right = l.Width - 16;
        return new UiNode("otcs-count", UiNodeKind.Label)
        {
            Placement = new(width, l.Field, Left: right - width, Top: 8), Text = text, Style = _count,
        };
    }

    #endregion
}

/// <summary>
/// The original's panel geometry (<b>Verified</b>, decompiled scaffold builder): width = field width + 50 (+ 82 with the counter), at most the page
/// less 8, at least 240; height = field height + 16; the counter's room is the width less 170 (none below 44, at most 72); the box from 42 is
/// the width less 50, the counter's room and its 10 px of gaps (at least 120); the clear button is the field's height (18 to 32) square.
/// </summary>
readonly record struct Layout(float Width, float Height, float Field, float Count, float Edit, float Clear)
{
    public static Layout Of(Vector2 page, CraftingSettings s)
    {
        int counter = s.ShowEntryCount ? 72 + 8 + 2 : 0;
        float inner = page.X > 8 ? page.X - 8 : page.X;
        float width = s.SearchWidth + 50 + counter;
        if (inner > 0 && inner < width) width = inner;
        width = Math.Max(width, 240);
        if (inner > 0 && inner < width) width = inner;

        float count = !s.ShowEntryCount || width - 170 < 44 ? 0 : Math.Min(72, width - 170);
        float gaps = count > 0 ? 10 : 0;
        float edit = Math.Max(120, width - 50 - count - gaps);
        int field = s.SearchHeight;
        float clear = field < 33 ? Math.Max(field, 18) : 32;
        return new(width, field + 16, field, count, edit, clear);
    }
}
