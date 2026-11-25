using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.QuickStartSelect;

/// <summary>The window's list half: title, the two tool rows, the start list with its scroll bar, and the bottom buttons.</summary>
sealed partial class StartWindow
{
    /// <summary>The window's parts in pixels from its top left.</summary>
    readonly record struct Geometry(float S, float Width, float Height, float Tools1, float Tools2, UiRect List, UiRect Details, float Bottom)
    {
        public float Pad => 12 * S;
        public float Row => 26 * S;
        public float Button => 28 * S;
    }

    Geometry _geo;

    /// <summary>The window's controls, built each time the start window is arranged; null while the list is off.</summary>
    UiNode? Build(UiScreenContext context)
    {
        try
        {
            if (!Active) return null;

            _anchor = context.Anchor;
            _scale = Math.Max(.6f, context.Frame.Viewport.Height / 1080f);
            _ink = DarkText(context) ? DarkInk : LightInk;
            TouchOpen();
            _geo = Measure(_anchor.Width, _anchor.Height, _scale);
            return Window(context);
        }
        catch (Exception)
        {
            return null;
        }
    }

    Geometry Measure(float w, float h, float s)
    {
        float pad = 12 * s, button = 28 * s;
        float tools1 = pad + 36 * s, tools2 = tools1 + button + 6 * s;
        float top = tools2 + button + 10 * s, bottom = h - pad - button, height = bottom - 8 * s - top;
        if (settings.Wide)
        {
            float listWidth = (w - 2 * pad) * .42f;
            var list = new UiRect(pad, top, listWidth, height);
            return new(s, w, h, tools1, tools2, list, new(pad + listWidth + 14 * s, top, w - 2 * pad - listWidth - 14 * s, height), bottom);
        }

        var narrow = new UiRect(pad, top, w - 2 * pad, height * .45f);
        float detailsTop = narrow.Y + narrow.Height + 10 * s;
        return new(s, w, h, tools1, tools2, narrow, new(pad, detailsTop, w - 2 * pad, top + height - detailsTop), bottom);
    }

    UiNode Window(UiScreenContext context)
    {
        var root = new UiNode("qss") { Placement = UiPlacement.Fill, Style = _panel };
        root.Children.Add(Text("qss-title", tr("Choose your beginning"), _geo.Pad, _geo.Pad, _geo.Width - 2 * _geo.Pad, 30 * _scale, "Center",
            1.2f));
        AddTools(root);
        AddList(root, context);
        if (_showOptions || settings.Wide) AddOptions(root);
        if (!_showOptions || settings.Wide) AddDetails(root, context);
        AddBottom(root);
        AddPopup(root);
        return root;
    }

    #region Tools

    /// <summary>
    /// Row one: the layout button, the two sort buttons (the active one names its order) and the race filter; row two: "Search:", the box, X
    /// (clears the search and the filter) and how many of all are shown.
    /// </summary>
    void AddTools(UiNode root)
    {
        float s = _scale, x = _geo.Pad, y = _geo.Tools1, h = _geo.Button;
        root.Children.Add(Button("qss-layout", settings.Wide ? tr("Compact view") : tr("Wide view"), x, y, 130 * s, h, "layout"));
        string az = !settings.ByDifficulty && settings.SortDesc ? "Z-A" : "A-Z";
        string diff = settings.ByDifficulty && settings.SortDesc ? tr("HARD-EASY") : tr("EASY-HARD");
        root.Children.Add(Button("qss-az", az, x + 136 * s, y, 60 * s, h, "sort:By name"));
        root.Children.Add(Button("qss-diff", diff, x + 202 * s, y, 110 * s, h, "sort:By difficulty"));
        float raceX = x + 318 * s;
        root.Children.Add(Button("qss-race", RaceCaption(), raceX, y, Math.Max(80 * s, _geo.Width - _geo.Pad - raceX), h, "popup:Races"));

        y = _geo.Tools2;
        root.Children.Add(Text("qss-search", tr("Search:"), x, y, 70 * s, h));
        float count = 90 * s, clear = h, box = Math.Max(60 * s, _geo.Width - 2 * _geo.Pad - 76 * s - clear - count - 12 * s);
        root.Children.Add(new UiNode("qss-query", UiNodeKind.TextBox)
        {
            Placement = new(box, h, Left: x + 76 * s, Top: y), Text = _query, Action = _act, Argument = "query", MaxLength = 64,
        });
        root.Children.Add(Button("qss-clear", "X", x + 76 * s + box + 6 * s, y, clear, h, "clear"));
        string shown = $"{_view.Count.ToString(CultureInfo.InvariantCulture)} / {starts.Count.ToString(CultureInfo.InvariantCulture)}";
        root.Children.Add(Text("qss-count", shown, _geo.Width - _geo.Pad - count, y, count, h, "Right VCenter"));
    }

    /// <summary>"All races" or the chosen race with how many starts have it.</summary>
    string RaceCaption() => _race is null ? tr("All races") : $"{_race} ({RaceCount(_race)})";

    int RaceCount(string race) => starts.Count(s => s.Races.Contains(race));

    #endregion

    #region List

    /// <summary>How many rows the list shows.</summary>
    int ListRows() => Math.Max(1, (int)(_geo.List.Height / _geo.Row));

    /// <summary>The list: one row per shown start from the scroll offset (the name, the difficulty in its colour at the right), then the bar.</summary>
    void AddList(UiNode root, UiScreenContext context)
    {
        var list = _geo.List;
        root.Children.Add(new UiNode("qss-list") { Placement = Place(list), Style = _flat, Pickable = false });
        int rows = ListRows();
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _view.Count - rows));
        bool dark = DarkText(context);
        float width = list.Width - 22 * _scale;
        for (int i = 0; i < rows && _offset + i < _view.Count; i++)
        {
            var start = _view[_offset + i];
            float y = list.Y + i * _geo.Row;
            root.Children.Add(new UiNode($"qss-row{i}", UiNodeKind.Button)
            {
                Placement = new(width, _geo.Row, Left: list.X, Top: y), Text = start.Name, Action = _act, Argument = "row:" + start.Start.Id,
                Style = start.Start.Id == _selected ? _chosen : _row, TextColour = _ink,
            });
            root.Children.Add(new UiNode($"qss-rowd{i}", UiNodeKind.Label)
            {
                Placement = new(width * .45f, _geo.Row, Left: list.X + width * .55f - 6 * _scale, Top: y), Text = start.DifficultyText,
                TextColour = Difficulty.Colour(start.Rank, dark), Style = _row, Pickable = false,
            });
        }

        AddScrollBar(root, list, rows);
    }

    /// <summary>The bar at the list's right: up and down a row, and a track whose press or drag scrolls to that point, with its thumb.</summary>
    void AddScrollBar(UiNode root, UiRect list, int rows)
    {
        if (_view.Count <= rows) return;

        float w = 18 * _scale, x = list.X + list.Width - w, arrow = w;
        root.Children.Add(Button("qss-up", "^", x, list.Y, w, arrow, "scroll:up"));
        root.Children.Add(Button("qss-down", "v", x, list.Y + list.Height - arrow, w, arrow, "scroll:down"));
        var track = new UiRect(x, list.Y + arrow, w, list.Height - 2 * arrow);
        root.Children.Add(new UiNode("qss-track") { Placement = Place(track), Action = _act, Argument = "scroll:track", Pointer = UiPointerEvents.Drag });
        float share = (float)rows / _view.Count, thumb = Math.Max(10 * _scale, track.Height * share);
        float at = track.Y + (track.Height - thumb) * _offset / Math.Max(1, _view.Count - rows);
        root.Children.Add(new UiNode("qss-thumb", UiNodeKind.Button) { Placement = new(w, thumb, Left: x, Top: at), Pickable = false });
    }

    /// <summary>Up and down move three rows; a press or drag on the track scrolls to the point under the pointer.</summary>
    void Scroll(string how, UiActionContext c)
    {
        int rows = ListRows(), most = Math.Max(0, _view.Count - rows);
        if (how == "up") _offset = Math.Max(0, _offset - 3);
        else if (how == "down") _offset = Math.Min(most, _offset + 3);
        else if (c.Kind is UiEventKind.Press or UiEventKind.Drag)
        {
            float top = _anchor.Y + _geo.List.Y + 18 * _scale, height = _geo.List.Height - 36 * _scale;
            float t = Math.Clamp((c.Position.Y - top) / Math.Max(1, height), 0, 1);
            _offset = (int)MathF.Round(t * most);
        }
    }

    #endregion

    #region Bottom

    /// <summary>Cancel at the left; Random start and Begin at the right; in the compact layout the options or description toggle between.</summary>
    void AddBottom(UiNode root)
    {
        float s = _scale, y = _geo.Bottom, h = _geo.Button, w = 140 * s, right = _geo.Width - _geo.Pad;
        root.Children.Add(Button("qss-cancel", tr("Cancel"), _geo.Pad, y, w, h, "cancel"));
        root.Children.Add(Button("qss-begin", tr("Begin"), right - w, y, w, h, "begin", Selected is not null));
        root.Children.Add(Button("qss-random", tr("Random start"), right - 2 * w - 6 * s, y, w, h, "random", _view.Count > 1));
        if (!settings.Wide)
        {
            string caption = _showOptions ? tr("Description") : tr("Advanced options");
            root.Children.Add(Button("qss-options", caption, _geo.Pad + w + 6 * s, y, w, h, "options", GameAccess.OptionsReady));
        }
    }

    #endregion

    #region Nodes

    static UiPlacement Place(UiRect r) => new(r.Width, r.Height, Left: r.X, Top: r.Y);

    UiNode Text(string id, string text, float x, float y, float w, float h, string align = "Left VCenter", float scale = 1, Vector4? colour = null) =>
        new(id, UiNodeKind.Label)
        {
            Placement = new(w, h, Left: x, Top: y), Text = text, TextColour = colour ?? _ink, TextScale = scale, Style = AlignStyle(align),
        };

    UiNode Button(string id, string text, float x, float y, float w, float h, string argument, bool enabled = true) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(w, h, Left: x, Top: y), Text = text, Action = _act, Argument = argument, Enabled = enabled,
        };

    #endregion
}
