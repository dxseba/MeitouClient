using System.Globalization;
using Meitou.Modding;
using Meitou.Game.Ui;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// The overview (the Prisoner overview key action) with the original's texts in the game's language (<see cref="OriginalTexts"/>; the port's
/// English, from the original's DLL strings and languages.ini [en], when the file is absent): the counts line ("Gathering prisoners ..." before the
/// first), the seven columns (a click on a title sorts by it, again reverses; widths from the language's weights), paged rows, the unloaded and
/// feeding hints and the original's settings rows (<see cref="Settings"/>).
/// </summary>
sealed partial class PrisonerWindow : IUiExtension
{
    const float Width = 900, MinColumn = 24;
    const int PageSize = 16;

    static readonly (string Key, string English)[] Titles =
    [
        ("column.name", "Name"), ("column.faction", "Faction"), ("column.cage", "Cage"), ("column.nu", "Nu"),
        ("column.condition", "Condition"), ("column.location", "Location"), ("column.status", "Status"),
    ];

    static readonly float[] OwnWidths = [160, 130, 120, 50, 150, 150, 110];

    readonly record struct Styles(string Panel, string Text, string Dim, string Button);

    readonly ModSettingsGroup _group;
    readonly Action<string> _log;
    readonly OriginalTexts _texts;
    readonly float[] _widths;
    readonly int _fontHeight;
    Styles _styles;
    string _close = "", _page = "", _sort = "";
    volatile bool _open;
    int _first, _sortColumn = -1;
    bool _descending;
    float? _scale;

    public PrisonerWindow(ModSettingsGroup group, OriginalTexts texts, Action<string> log)
    {
        _group = group;
        _log = log;
        _texts = texts;
        _fontHeight = texts.FontHeight();
        _widths = texts.Widths() is { } weights ? Spread(weights) : OwnWidths;
    }

    public string Id => "dxseba.persistentprisoners.ui";

    /// <summary>A row's height: 22, more for a larger <c>font.height</c>.</summary>
    float Line => Math.Max(22, _fontHeight + 6);

    /// <summary>The list's width shared by the weights, each column at least 24 px (as the original clamps tiny columns).</summary>
    static float[] Spread(float[] weights)
    {
        float list = Width - 12 - (weights.Length - 1) * 4;
        float total = weights.Sum();
        return [.. weights.Select(w => Math.Max(MinColumn, MathF.Floor(list * w / total)))];
    }

    public void Toggle()
    {
        if (!_open) ResetPending();
        _open = !_open;
        _capturing = false;
    }

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
        _close = registration.RegisterAction("close", _ => Close());
        _page = registration.RegisterAction("page", c => _first += c.Argument == "next" ? PageSize : -PageSize);
        _sort = registration.RegisterAction("sort", c => Sort(int.Parse(c.Argument, CultureInfo.InvariantCulture)));
        RegisterSettings(registration);
        registration.Contribute(UiSlots.Overlay, Build, order: 57);
    }

    void Close()
    {
        _open = false;
        _capturing = false;
    }

    void Sort(int column)
    {
        _descending = column == _sortColumn && !_descending;
        _sortColumn = column;
    }

    UiNode Build(UiFrameContext frame)
    {
        if (!_open || PersistentPrisonersMod.Running is not { } system) return new UiNode("prisoners-hidden") { Visible = false };

        _scale = TextScale(frame);
        var lines = Table(system.Rows);
        lines.AddRange(Hints());
        lines.AddRange(SettingsLines());

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

    /// <summary><c>font.height</c> as a text scale: the height over the default font's measured line height; null for the game's size.</summary>
    float? TextScale(UiFrameContext frame)
    {
        if (_fontHeight == 0) return null;

        float height = frame.Measure("Ag").Y;
        return height > 0 ? Math.Clamp(_fontHeight / height, .5f, 3) : null;
    }

    IEnumerable<UiNode> Hints()
    {
        string unloaded = _texts.Get("settings.unloaded_hint", "Unloaded: values from the last observation. Release ends protection.");
        yield return Text("prisoners-hint-unloaded", unloaded, _styles.Dim);

        if (PersistentPrisonersMod.Settings.Starvation) yield break;

        string feeding = _texts.Get("settings.feeding_hint", "Starvation OFF: NPCs are fed gradually towards Minimum Nu. Low values may be fatal.");
        yield return Text("prisoners-hint-feeding", feeding, _styles.Dim);
    }

    /// <summary>The header, counts, titles and the page's rows; "Gathering prisoners ..." until the system has gathered them once.</summary>
    List<UiNode> Table(IReadOnlyList<PrisonerRow>? gathered)
    {
        if (gathered is null) return [Header(0), Text("prisoners-counts", _texts.Get("summary.loading", "Gathering prisoners ..."), _styles.Dim)];

        var rows = Sorted(gathered);
        _first = Math.Clamp(_first, 0, Math.Max(0, (rows.Count - 1) / PageSize * PageSize));
        var lines = new List<UiNode> { Header(rows.Count), Text("prisoners-counts", Counts(rows), _styles.Dim), TitleRow() };
        lines.AddRange(rows.Skip(_first).Take(PageSize).Select((r, i) => RowNode(r, _first + i)));
        return lines;
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
    string Counts(IReadOnlyList<PrisonerRow> rows) =>
        _texts.Get("summary.counts", "Prisoners: {prisoners}    Corpses: {corpses}    Unloaded: {unloaded}",
            ("prisoners", rows.Count(r => !r.Dead)), ("corpses", rows.Count(r => r.Dead)), ("unloaded", rows.Count(r => !r.Loaded)));

    UiNode Header(int count)
    {
        var row = Row("prisoners-header");
        string title = _texts.Get("window.title", "PersistentPrisoners {version}", ("version", "")).Trim();
        row.Children.Add(Label("prisoners-title", title, Width - 12 - 3 * 74, _styles.Text));
        row.Children.Add(Button("prisoners-prev", "<", _page, "prev", _first > 0, 70));
        row.Children.Add(Button("prisoners-next", ">", _page, "next", _first + PageSize < count, 70));
        row.Children.Add(Button("prisoners-close", _texts.Get("button.close", "Close"), _close, "", true, 70));
        return row;
    }

    UiNode TitleRow()
    {
        var row = Row("prisoners-titles");
        for (int i = 0; i < Titles.Length; i++)
        {
            string text = _texts.Get(Titles[i].Key, Titles[i].English);
            string title = i == _sortColumn ? text + (_descending ? " v" : " ^") : text;
            row.Children.Add(Button($"prisoners-t{i}", title, _sort, i.ToString(CultureInfo.InvariantCulture), true, _widths[i]));
        }

        return row;
    }

    UiNode RowNode(PrisonerRow r, int index)
    {
        string nu = r.Nu.ToString(CultureInfo.InvariantCulture);
        string[] cells = [r.Name, _texts.Of(r.Faction), r.Cage, nu, _texts.Of(r.Condition), _texts.Of(r.Location), _texts.Of(r.Status)];

        var row = Row($"prisoners-row{index}");
        for (int i = 0; i < cells.Length; i++) row.Children.Add(Label($"prisoners-r{index}c{i}", cells[i], _widths[i], _styles.Text));
        return row;
    }

    UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style, TextScale = _scale };

    UiNode Text(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style, TextScale = _scale };

    UiNode Button(string id, string text, string action, string argument, bool enabled, float width) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line), Text = text, Style = _styles.Button, Action = enabled ? action : "", Argument = argument, Enabled = enabled,
            TextScale = _scale,
        };
}
