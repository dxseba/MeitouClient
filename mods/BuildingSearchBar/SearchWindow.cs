using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// The SEARCH window while the build window is open, laid out as the original's (468 by 430, placed just above the build window's top-left):
/// "Search", the query box, a clear and a Next button, the status line and the results list (a scroll view). Dragging its caption moves it; its
/// caption's fold button shrinks it to a SEARCH button, which opens it again.
/// </summary>
sealed class SearchWindow : IUiExtension
{
    /// <summary>
    /// The original's window and widget sizes.
    /// </summary>
    const int Width = 468, Height = 430, Caption = 28, Row = 22, ListHeight = 306, ToggleWidth = 170, ToggleHeight = 26;

    /// <summary>
    /// How far the player dragged the window from its place by the build window, pixels (kept for the session).
    /// </summary>
    Vector2 _moved;

    /// <summary>
    /// Bumped by each new query, so the results list (its scroll offset kept by node id) starts at the top again.
    /// </summary>
    int _generation;

    /// <summary>
    /// The registration, for the results list's scroll offset.
    /// </summary>
    UiRegistration? _registration;

    /// <summary>
    /// Registered style and action keys.
    /// </summary>
    string _panel = "", _text = "", _row = "", _chosenRow = "", _typed = "", _clear = "", _next = "", _pick = "", _fold = "", _drag = "";

    public string Id => "dxseba.buildingsearchbar.ui";

    public void Register(UiRegistration registration)
    {
        _registration = registration;
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxFlatSkin" });
        _text = registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new UiInsets(4, 0, 4, 0) });
        var rowPad = new UiInsets(6, 0, 4, 0);
        _row = registration.RegisterStyle("row", new() { Text = new(.91f, .86f, .74f, 1), Hover = new(1, 1, 1, .08f), TextPadding = rowPad });
        _chosenRow = registration.RegisterStyle("chosen", new() { Text = new(1, .95f, .8f, 1), Background = new(1, 1, 1, .14f), TextPadding = rowPad });
        _typed = registration.RegisterAction("typed", c => Typed(c.Text));
        _clear = registration.RegisterAction("clear", _ => Typed(""));
        _next = registration.RegisterAction("next", _ => Next());
        _pick = registration.RegisterAction("pick", c => Search.Choose(int.Parse(c.Argument)));
        _fold = registration.RegisterAction("fold", _ => Search.Collapsed = !Search.Collapsed);
        _drag = registration.RegisterAction("drag", Dragged);
        registration.Contribute(UiSlots.Overlay, Build, order: 61);
    }

    #region Actions

    /// <summary>
    /// The query changed: search again from the first row.
    /// </summary>
    void Typed(string text)
    {
        Search.Set(text);
        _generation++;
    }

    /// <summary>
    /// Next: the following result, its row scrolled into view.
    /// </summary>
    void Next()
    {
        Search.Next();
        if (Search.Chosen < 0 || _registration is not { } registration) return;

        string list = ResultsId;
        float top = Search.Chosen * Row, offset = registration.ScrollOffset(list);
        if (top < offset) registration.ScrollTo(list, top);
        else if (top + Row > offset + ListHeight) registration.ScrollTo(list, top + Row - ListHeight);
    }

    /// <summary>
    /// The results list's node id (a new one for each query).
    /// </summary>
    string ResultsId => $"bsb-results{_generation}";

    /// <summary>
    /// A drag of the caption moves the window (and its folded button) by the pointer's movement.
    /// </summary>
    void Dragged(UiActionContext c)
    {
        if (c.Kind == UiEventKind.Drag) _moved += c.Delta;
    }

    #endregion

    #region Layout

    /// <summary>
    /// The window, or its fold button, while the build window is open.
    /// </summary>
    UiNode Build(UiFrameContext frame)
    {
        if (!Search.BuildOpen) return new UiNode("bsb-hidden") { Visible = false };

        var root = new UiNode("bsb") { Placement = UiPlacement.Fill, Pickable = false };
        var origin = (Search.BuildOrigin ?? new Vector2(6, Height + 28)) + _moved;
        root.Children.Add(Search.Collapsed ? Toggle(origin) : Window(origin));
        return root;
    }

    /// <summary>
    /// The folded window: the original's SEARCH button 30 to the right of the build window's left edge, 28 above its top.
    /// </summary>
    UiNode Toggle(Vector2 origin) => new("bsb-toggle", UiNodeKind.Button)
    {
        Placement = new(ToggleWidth, ToggleHeight, Left: Math.Max(6, origin.X + 30), Top: Math.Max(6, origin.Y - 28)),
        Text = "SEARCH", Action = _fold,
    };

    /// <summary>
    /// The open window: at the build window's left edge (at least 6), its bottom 8 above the build window's top (its top at least 20).
    /// </summary>
    UiNode Window(Vector2 origin)
    {
        var window = new UiNode("bsb-window")
        {
            Placement = new(Width, Height, Left: Math.Max(6, origin.X), Top: Math.Max(20, origin.Y - Height - 8)), Style = _panel,
        };
        window.Children.Add(new UiNode("bsb-caption", UiNodeKind.Label)
        {
            Placement = new(Width - 48, Caption - 6, Left: 8, Top: 4), Text = "SEARCH", Style = _text, Pickable = true, Pointer = UiPointerEvents.Drag,
            Action = _drag,
        });
        window.Children.Add(Button("bsb-fold", "-", Width - 34, 4, 26, Caption - 6, _fold));
        AddSearchLine(window);
        AddResults(window);
        return window;
    }

    /// <summary>
    /// "Search", the query box, the clear and Next buttons and the status line.
    /// </summary>
    void AddSearchLine(UiNode window)
    {
        window.Children.Add(Label("bsb-label", "Search", 16, Caption + 16, 56, 24));
        window.Children.Add(new UiNode("bsb-query", UiNodeKind.TextBox)
        {
            Placement = new(286, 30, Left: 92, Top: Caption + 16), Text = Search.Query, Action = _typed, MaxLength = Search.MaxLength,
        });
        window.Children.Add(Button("bsb-clear", "X", 384, Caption + 16, 30, 30, _clear, Search.Query.Length > 0));
        window.Children.Add(Button("bsb-next", ">", 420, Caption + 16, 30, 30, _next, Search.Results.Count > 0));
        window.Children.Add(Label("bsb-status", Search.Status, 16, Caption + 52, 436, 22));
    }

    /// <summary>
    /// The results list, scrolled by its bar or the wheel; a click on a row chooses that building.
    /// </summary>
    void AddResults(UiNode window)
    {
        var list = new UiNode(ResultsId, UiNodeKind.ScrollView)
        {
            Placement = new(436, ListHeight, Left: 16, Top: Caption + 76), Flow = UiFlow.Column, ScrollStep = Row,
        };
        var results = Search.Results;
        for (int i = 0; i < results.Count; i++)
        {
            list.Children.Add(new UiNode($"bsb-row{i}", UiNodeKind.Button)
            {
                Placement = new(UiLength.Percent(100), Row), Text = results[i].Caption, Action = _pick,
                Argument = i.ToString(System.Globalization.CultureInfo.InvariantCulture), Style = i == Search.Chosen ? _chosenRow : _row,
            });
        }

        window.Children.Add(list);
    }

    /// <summary>
    /// A text line.
    /// </summary>
    UiNode Label(string id, string text, float x, float y, float w, float h) =>
        new(id, UiNodeKind.Label) { Placement = new(w, h, Left: x, Top: y), Text = text, Style = _text };

    /// <summary>
    /// A button.
    /// </summary>
    static UiNode Button(string id, string text, float x, float y, float w, float h, string action, bool enabled = true, string argument = "") =>
        new(id, UiNodeKind.Button) { Placement = new(w, h, Left: x, Top: y), Text = text, Action = action, Enabled = enabled, Argument = argument };

    #endregion
}
