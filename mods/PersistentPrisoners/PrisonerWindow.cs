using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// The overview (the Prisoner overview key action) with the original's English texts (<b>Verified</b>, DLL strings and its languages.ini [en]):
/// the counts line, the seven columns (a click on a title sorts by it, again reverses), paged rows and the unloaded and feeding hints.
/// </summary>
sealed class PrisonerWindow : IUiExtension
{
    const float Width = 900, Line = 22;
    const int PageSize = 16;

    static readonly (string Title, float Width)[] Columns =
        [("Name", 160), ("Faction", 130), ("Cage", 120), ("Nu", 50), ("Condition", 150), ("Location", 150), ("Status", 110)];

    readonly record struct Styles(string Panel, string Text, string Dim, string Button);

    Styles _styles;
    string _close = "", _page = "", _sort = "";
    volatile bool _open;
    int _first, _sortColumn = -1;
    bool _descending;

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
        _sort = registration.RegisterAction("sort", c => Sort(int.Parse(c.Argument, CultureInfo.InvariantCulture)));
        registration.Contribute(UiSlots.Overlay, Build, order: 57);
    }

    void Sort(int column)
    {
        _descending = column == _sortColumn && !_descending;
        _sortColumn = column;
    }

    UiNode Build(UiFrameContext frame)
    {
        if (!_open || PersistentPrisonersMod.Running is not { } system) return new UiNode("prisoners-hidden") { Visible = false };

        var rows = Sorted(system.Rows);
        _first = Math.Clamp(_first, 0, Math.Max(0, (rows.Count - 1) / PageSize * PageSize));
        var lines = new List<UiNode> { Header(rows.Count), Text("prisoners-counts", Counts(rows), _styles.Dim), Titles() };
        lines.AddRange(rows.Skip(_first).Take(PageSize).Select((r, i) => RowNode(r, _first + i)));
        lines.Add(Text("prisoners-hint-unloaded", "Unloaded: values from the last observation. Release ends protection.", _styles.Dim));
        if (!PersistentPrisonersMod.Settings.Starvation)
            lines.Add(Text("prisoners-hint-feeding", "Starvation OFF: NPCs are fed gradually towards Minimum Nu. Low values may be fatal.", _styles.Dim));

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

    IReadOnlyList<PrisonerRow> Sorted(IReadOnlyList<PrisonerRow> rows)
    {
        if (_sortColumn < 0) return rows;

        Func<PrisonerRow, IComparable> key = _sortColumn switch
        {
            0 => r => r.Name, 1 => r => r.Faction, 2 => r => r.Cage, 3 => r => r.Nu, 4 => r => r.Condition, 5 => r => r.Location, _ => r => r.Status,
        };
        return _descending ? [.. rows.OrderByDescending(key)] : [.. rows.OrderBy(key)];
    }

    /// <summary>"Prisoners: N    Corpses: N    Unloaded: N".</summary>
    static string Counts(IReadOnlyList<PrisonerRow> rows) => string.Create(CultureInfo.InvariantCulture,
        $"Prisoners: {rows.Count(r => !r.Dead)}    Corpses: {rows.Count(r => r.Dead)}    Unloaded: {rows.Count(r => !r.Loaded)}");

    UiNode Header(int count)
    {
        var row = Row("prisoners-header");
        row.Children.Add(Label("prisoners-title", "PersistentPrisoners", Width - 12 - 3 * 74, _styles.Text));
        row.Children.Add(Button("prisoners-prev", "<", _page, "prev", _first > 0, 70));
        row.Children.Add(Button("prisoners-next", ">", _page, "next", _first + PageSize < count, 70));
        row.Children.Add(Button("prisoners-close", "Close", _close, "", true, 70));
        return row;
    }

    UiNode Titles()
    {
        var row = Row("prisoners-titles");
        for (int i = 0; i < Columns.Length; i++)
        {
            string title = i == _sortColumn ? Columns[i].Title + (_descending ? " v" : " ^") : Columns[i].Title;
            row.Children.Add(Button($"prisoners-t{i}", title, _sort, i.ToString(CultureInfo.InvariantCulture), true, Columns[i].Width));
        }

        return row;
    }

    UiNode RowNode(PrisonerRow r, int index)
    {
        string[] cells = [r.Name, r.Faction, r.Cage, r.Nu.ToString(CultureInfo.InvariantCulture), r.Condition, r.Location, r.Status];

        var row = Row($"prisoners-row{index}");
        for (int i = 0; i < cells.Length; i++) row.Children.Add(Label($"prisoners-r{index}c{i}", cells[i], Columns[i].Width, _styles.Text));
        return row;
    }

    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    static UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    static UiNode Text(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    UiNode Button(string id, string text, string action, string argument, bool enabled, float width) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line), Text = text, Style = _styles.Button, Action = enabled ? action : "", Argument = argument, Enabled = enabled,
        };
}
