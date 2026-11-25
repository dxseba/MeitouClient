using Meitou.Game.Ui;

namespace Meitou.SentientSands;

/// <summary>The hub's layout: tabs, the list, the text, the editor boxes, the buttons and a status line.</summary>
sealed partial class HubWindow
{
    readonly record struct Styles(string Panel, string Text, string Button, string Box);

    readonly record struct Actions(string Tab, string Row, string Button, string Page, string TextPage, string Title, string Edit, string Close);

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.08f, .08f, .07f, .96f), Text = new(.92f, .88f, .78f, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.92f, .88f, .78f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.2f, .16f, .12f, .95f), Hover = new(.32f, .24f, .17f, 1), Disabled = new(.12f, .1f, .08f, .8f),
                TextPadding = new(6, 2, 6, 2),
            }),
            registration.RegisterStyle("box", new() { Text = new(.95f, .95f, .9f, 1), TextPadding = new(6, 2, 6, 2) }));
        var actions = new Actions(
            registration.RegisterAction("tab", a => (_tab, _page) = (int.Parse(a.Argument), 1)),
            registration.RegisterAction("row", a => Choose(a.Argument)),
            registration.RegisterAction("button", a => Buttons().ElementAtOrDefault(int.Parse(a.Argument)).Run?.Invoke()),
            registration.RegisterAction("page", a => Turn(int.Parse(a.Argument))),
            registration.RegisterAction("textpage", a => _textPage = Math.Max(0, _textPage + int.Parse(a.Argument))),
            registration.RegisterAction("title", a => _title = a.Text),
            registration.RegisterAction("edit", a => _edit = a.Text),
            registration.RegisterAction("close", _ => _open = false));
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 62);
    }

    int _shownTab = -1;

    void Turn(int step)
    {
        _page = Math.Clamp(_page + step, 1, Math.Max(1, _pages));
        Load();
    }

    UiNode Build(Styles styles, Actions actions)
    {
        if (!_open) return new UiNode("ss-hub-hidden") { Visible = false };

        if (_shownTab != _tab)
        {
            _shownTab = _tab;
            Load();
        }

        var body = new UiNode("ss-hub") { Placement = new(Width, Height, Left: 120, Top: 80), Style = styles.Panel, Flow = UiFlow.Column,
            Padding = 4, Gap = 3 };
        body.Children.Add(TabRow(styles, actions));
        body.Children.Add(Middle(styles, actions));
        body.Children.Add(Editor(styles, actions));
        body.Children.Add(ButtonRow(styles, actions));
        body.Children.Add(Label("ss-hub-status", _status, styles.Text, Width - 12));

        var root = new UiNode("ss-hub-root") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(body);
        return root;
    }

    UiNode TabRow(Styles styles, Actions actions)
    {
        var row = new UiNode("ss-hub-tabs") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        for (int i = 0; i < Tabs.Length; i++)
            row.Children.Add(Button($"ss-hub-t{i}", context.Tr(Tabs[i]), actions.Tab, i.ToString(), styles.Button, 120, i != _tab));

        row.Children.Add(Button("ss-hub-close", context.Tr("Close"), actions.Close, "", styles.Button, 100, true));
        return row;
    }

    UiNode Middle(Styles styles, Actions actions)
    {
        var middle = new UiNode("ss-hub-mid") { Placement = new(UiLength.Fill, (Rows + 1) * (Line + 1)), Flow = UiFlow.Row, Gap = 6 };
        middle.Children.Add(List(styles, actions));
        middle.Children.Add(Text(styles, actions));
        return middle;
    }

    UiNode List(Styles styles, Actions actions)
    {
        var list = new UiNode("ss-hub-list") { Placement = new(ListWidth, UiLength.Fill), Flow = UiFlow.Column, Gap = 1 };
        foreach (var (row, i) in _rows.Take((int)Rows).Select((r, i) => (r, i)))
            list.Children.Add(Button($"ss-hub-r{i}", Cut(row.Label, 40), actions.Row, row.Id, styles.Button, ListWidth, row.Id != _selected));

        if (_tab == 1) list.Children.Add(Pager("ss-hub-p", $"{_page} / {_pages}", actions.Page, styles));
        return list;
    }

    UiNode Text(Styles styles, Actions actions)
    {
        var text = new UiNode("ss-hub-text") { Placement = new(UiLength.Fill, UiLength.Fill), Flow = UiFlow.Column, Gap = 0 };
        int per = (int)Rows - 1;
        _textPage = Math.Min(_textPage, Math.Max(0, (_text.Count - 1) / per));
        foreach (var (line, i) in _text.Skip(_textPage * per).Take(per).Select((l, i) => (l, i)))
            text.Children.Add(Label($"ss-hub-x{i}", line, styles.Text, Width - ListWidth - 30));

        if (_text.Count > per) text.Children.Add(Pager("ss-hub-xp", $"{_textPage + 1} / {(_text.Count - 1) / per + 1}", actions.TextPage, styles));
        return text;
    }

    UiNode Editor(Styles styles, Actions actions)
    {
        var row = new UiNode("ss-hub-edit") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        if (_tab == 1) row.Children.Add(Box("ss-hub-title", _title, actions.Title, styles.Box, 220));

        if (_tab is 1 or 3) row.Children.Add(Box("ss-hub-box", _edit, actions.Edit, styles.Box, Width - (_tab == 1 ? 240 : 16)));
        return row;
    }

    UiNode ButtonRow(Styles styles, Actions actions)
    {
        var row = new UiNode("ss-hub-buttons") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        foreach (var ((caption, _), i) in Buttons().Select((b, i) => (b, i)))
            row.Children.Add(Button($"ss-hub-b{i}", context.Tr(caption), actions.Button, i.ToString(), styles.Button, 130, true));

        return row;
    }

    UiNode Pager(string id, string text, string action, Styles styles)
    {
        var row = new UiNode(id) { Placement = new(UiLength.Fill, Line + 2), Flow = UiFlow.Row, Gap = 4 };
        row.Children.Add(Button(id + "-prev", "<", action, "-1", styles.Button, 36, true));
        row.Children.Add(Label(id + "-n", text, styles.Text, 80));
        row.Children.Add(Button(id + "-next", ">", action, "1", styles.Button, 36, true));
        return row;
    }

    static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

    static UiNode Label(string id, string text, string style, float width) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    static UiNode Box(string id, string text, string action, string style, float width) =>
        new(id, UiNodeKind.TextBox) { Placement = new(width, Line + 4), Text = text, Action = action, MaxLength = 2000, Style = style };

    static UiNode Button(string id, string text, string action, string argument, string style, float width, bool enabled) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line + 2), Text = text, Action = action, Argument = argument, Style = style, Enabled = enabled,
        };
}
