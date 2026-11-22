using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// The SEARCH window while the build window is open, laid out as the original's (468 by 430, placed just above the build window's top-left):
/// "Search", the query box, a clear and a Next button, the status line and the results list (13 rows shown, up and down buttons page through
/// longer lists). Its caption's fold button shrinks it to a SEARCH button, which opens it again.
/// </summary>
sealed class SearchWindow : IUiExtension
{
    /// <summary>
    /// The original's window and widget sizes.
    /// </summary>
    const int Width = 468, Height = 430, Caption = 28, Row = 22, Rows = 13, ToggleWidth = 170, ToggleHeight = 26;

    /// <summary>
    /// The first result row shown.
    /// </summary>
    int _top;

    /// <summary>
    /// Registered style and action keys.
    /// </summary>
    string _panel = "", _text = "", _row = "", _chosenRow = "", _typed = "", _clear = "", _next = "", _pick = "", _fold = "", _page = "";

    public string Id => "dxseba.buildingsearchbar.ui";

    public void Register(UiRegistration registration)
    {
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
        _page = registration.RegisterAction("page", c => Page(int.Parse(c.Argument)));
        registration.Contribute(UiSlots.Overlay, Build, order: 61);
    }

    #region Actions

    /// <summary>
    /// The query changed: search again from the first row.
    /// </summary>
    void Typed(string text)
    {
        Search.Set(text);
        _top = 0;
    }

    /// <summary>
    /// Next, keeping the chosen row in view.
    /// </summary>
    void Next()
    {
        Search.Next();
        if (Search.Chosen < _top || Search.Chosen >= _top + Rows) _top = Math.Max(0, Search.Chosen - Rows / 2);
    }

    /// <summary>
    /// A page up (-1) or down (1).
    /// </summary>
    void Page(int step) => _top = Math.Clamp(_top + step * Rows, 0, Math.Max(0, Search.Results.Count - Rows));

    #endregion

    #region Layout

    /// <summary>
    /// The window, or its fold button, while the build window is open.
    /// </summary>
    UiNode Build(UiFrameContext frame)
    {
        if (!Search.BuildOpen) return new UiNode("bsb-hidden") { Visible = false };

        var root = new UiNode("bsb") { Placement = UiPlacement.Fill, Pickable = false };
        var origin = Search.BuildOrigin ?? new Vector2(6, Height + 28);
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
        window.Children.Add(Label("bsb-caption", "SEARCH", 8, 4, Width - 48, Caption - 6));
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
        window.Children.Add(Label("bsb-status", Search.Status, 16, Caption + 52, 380, 22));
        window.Children.Add(Button("bsb-up", "^", 400, Caption + 52, 24, 22, _page, _top > 0, "-1"));
        window.Children.Add(Button("bsb-down", "v", 428, Caption + 52, 24, 22, _page, _top + Rows < Search.Results.Count, "1"));
    }

    /// <summary>
    /// The shown rows of the results list; a click chooses that building.
    /// </summary>
    void AddResults(UiNode window)
    {
        var results = Search.Results;
        _top = Math.Clamp(_top, 0, Math.Max(0, results.Count - Rows));
        for (int i = _top; i < Math.Min(results.Count, _top + Rows); i++)
        {
            window.Children.Add(new UiNode($"bsb-row{i - _top}", UiNodeKind.Button)
            {
                Placement = new(436, Row, Left: 16, Top: Caption + 76 + (i - _top) * Row), Text = results[i].Caption, Action = _pick,
                Argument = i.ToString(System.Globalization.CultureInfo.InvariantCulture), Style = i == Search.Chosen ? _chosenRow : _row,
            });
        }
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
