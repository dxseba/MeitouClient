using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.PersistentPrisoners;

/// <summary>The prisoner overview (the Prisoner overview key action): one row per prisoner, paged; the rows come from the running system.</summary>
sealed class PrisonerWindow : IUiExtension
{
    const float Width = 720, Line = 22;
    const int PageSize = 16;

    static readonly (string Title, float Width)[] Columns = [("Name", 170), ("Faction", 150), ("Nutrition", 80), ("Condition", 100), ("Place", 190)];

    readonly record struct Styles(string Panel, string Text, string Dim, string Button);

    Styles _styles;
    string _close = "", _page = "";
    volatile bool _open;
    int _first;

    public string Id => "dxseba.persistentprisoners.ui";

    public void Toggle() => _open = !_open;

    public void Register(UiRegistration registration)
    {
        var ink = new System.Numerics.Vector4(.91f, .86f, .74f, 1);
        var pad = new Meitou.Data.Ui.UiInsets(6, 1, 6, 1);
        _styles = new(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .95f), Text = ink }),
            registration.RegisterStyle("text", new() { Text = ink, TextPadding = pad }),
            registration.RegisterStyle("dim", new() { Text = new(.66f, .62f, .54f, 1), TextPadding = pad }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Disabled = new(.14f, .12f, .10f, .8f), TextPadding = pad,
            }));
        _close = registration.RegisterAction("close", _ => _open = false);
        _page = registration.RegisterAction("page", c => _first += c.Argument == "next" ? PageSize : -PageSize);
        registration.Contribute(UiSlots.Overlay, Build, order: 57);
    }

    UiNode Build(UiFrameContext frame)
    {
        if (!_open || PersistentPrisonersMod.Running is not { } system) return new UiNode("prisoners-hidden") { Visible = false };

        var rows = system.Rows;
        _first = Math.Clamp(_first, 0, Math.Max(0, (rows.Count - 1) / PageSize * PageSize));
        var lines = new List<UiNode> { Header(rows.Count), Titles() };
        if (rows.Count == 0) lines.Add(Label("prisoners-none", "No prisoners in your cages.", Width - 12, _styles.Dim));
        lines.AddRange(rows.Skip(_first).Take(PageSize).Select((r, i) => RowNode(r, _first + i)));

        var window = new UiNode("prisoners-window")
        {
            Placement = new(Width, 12 + lines.Count * (Line + 2), Left: (frame.Viewport.Width - Width) / 2, Top: 100),
            Style = _styles.Panel, Flow = UiFlow.Column, Padding = 6, Gap = 2,
        };
        window.Children.AddRange(lines);

        var root = new UiNode("prisoners") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(window);
        return root;
    }

    UiNode Header(int count)
    {
        var row = Row("prisoners-header");
        string title = $"Prisoners ({count.ToString(CultureInfo.InvariantCulture)})";
        row.Children.Add(Label("prisoners-title", title, Width - 12 - 3 * 74, _styles.Text));
        row.Children.Add(Button("prisoners-prev", "<", _page, "prev", _first > 0));
        row.Children.Add(Button("prisoners-next", ">", _page, "next", _first + PageSize < count));
        row.Children.Add(Button("prisoners-close", "Close", _close, "", true));
        return row;
    }

    UiNode Titles()
    {
        var row = Row("prisoners-titles");
        for (int i = 0; i < Columns.Length; i++) row.Children.Add(Label($"prisoners-t{i}", Columns[i].Title, Columns[i].Width, _styles.Dim));
        return row;
    }

    UiNode RowNode(PrisonerRow r, int index)
    {
        string name = r.OwnSquad ? r.Name + " (yours)" : r.Name;
        string place = r.Loaded ? r.Place : r.Place + " (away)";
        string[] cells = [name, r.Faction, r.Nutrition.ToString(CultureInfo.InvariantCulture), r.Condition, place];

        var row = Row($"prisoners-row{index}");
        for (int i = 0; i < cells.Length; i++) row.Children.Add(Label($"prisoners-r{index}c{i}", cells[i], Columns[i].Width, _styles.Text));
        return row;
    }

    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    static UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    UiNode Button(string id, string text, string action, string argument, bool enabled) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(70, Line), Text = text, Style = _styles.Button, Action = enabled ? action : "", Argument = argument, Enabled = enabled,
        };
}
