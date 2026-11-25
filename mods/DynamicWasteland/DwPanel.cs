using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.DynamicWasteland;

/// <summary>
/// The original's world panel (F4 in the original; its key here is bound on the Controls page) with the tabs WORLD (nations, enlist, hire
/// out the player's company), WARS (wars, alliances, truces), NEWS (the chronicle), POWER (career, decrees, faith and company), PEOPLE
/// (spy, expose, blackmail, court), MISSIONS (the board) and SOCIETY (crisis, faiths, companies, caravans, troubled towns); and the political
/// map: territory blobs tinted in the owner's colour under each held town, labelled with the owner. Every change goes out as a
/// <see cref="DwOrder"/>.
/// </summary>
sealed class DwPanel(Func<string, string> tr, Action<SimCommand> enqueue, Func<long> tick) : IUiExtension
{
    enum Tab { World, Wars, News, Power, People, Missions, Society }

    const float Width = 900, Line = 22;
    const int PageSize = 18;

    readonly Dictionary<string, Action> _clicks = new(StringComparer.Ordinal);
    string _panel = "", _text = "", _dim = "", _button = "", _selected = "", _click = "";
    Tab _tab;
    int _page;

    /// <summary>Whether the panel shows (the key action toggles it, on the host's thread).</summary>
    public bool Open { get; set; }

    public string Id => "dxseba.dynamicwasteland.ui";

    public void Register(UiRegistration registration)
    {
        var ink = new Vector4(.91f, .86f, .74f, 1);
        var pad = new UiInsets(6, 1, 6, 1);
        _panel = registration.RegisterStyle("panel", new() { Background = new(.09f, .08f, .07f, .95f), Text = ink });
        _text = registration.RegisterStyle("text", new() { Text = ink, TextPadding = pad });
        _dim = registration.RegisterStyle("dim", new() { Text = new(.66f, .62f, .54f, 1), TextPadding = pad });
        _button = registration.RegisterStyle("button", new()
        {
            Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Disabled = new(.14f, .12f, .10f, .8f), TextPadding = pad,
        });
        _selected = registration.RegisterStyle("selected", new() { Background = new(.45f, .32f, .14f, 1), TextPadding = pad });
        _click = registration.RegisterAction("click", c =>
        {
            if (_clicks.TryGetValue(c.Argument, out var action)) action();
        });
        registration.Contribute(UiSlots.Overlay, Build, order: 61);
        registration.AddMapMarkers(Markers);
    }

    /// <summary>The political map (Overlay on): each held town's territory blob (BlobAlpha) under its owner-tinted marker, labelled from zoom 1.</summary>
    static IEnumerable<UiMapMarker> Markers(UiMapContext map)
    {
        var s = DynamicWastelandMod.Settings;
        if (!s.Enabled || !s.MapOverlay || DynamicWastelandMod.Running?.View is not { } view) return [];

        float alpha = Math.Clamp(1 - s.BlobAlpha, 0.1f, 0.9f);
        var blobs = DynamicWastelandMod.TerritoryReady
            ? view.Towns.Select(t => new UiMapMarker(t.At, "", t.Colour with { W = alpha }, null, null, 0, t.Territory) { Image = TerritoryImage.File })
            : [];
        return blobs.Concat(view.Towns.Select(t => new UiMapMarker(t.At, t.Group, t.Colour, t.Owner, t.Tooltip, 0, 1.15f)));
    }

    UiNode Build(UiFrameContext frame)
    {
        _clicks.Clear();
        var root = new UiNode("dw") { Placement = UiPlacement.Fill, Pickable = false };
        if (!Open || !DynamicWastelandMod.Settings.Enabled) return root;

        var view = DynamicWastelandMod.Running?.View;
        var lines = new List<UiNode> { Header() };
        if (view is null) lines.Add(Text("dw-none", tr("offline (local templates)"), _dim));
        else lines.AddRange(Content(view));

        var window = new UiNode("dw-window")
        {
            Placement = new(Width, 12 + lines.Count * (Line + 2), Left: (frame.Viewport.Width - Width) / 2, Top: 70), Style = _panel,
            Flow = UiFlow.Column, Padding = 6, Gap = 2,
        };
        window.Children.AddRange(lines);
        root.Children.Add(window);
        return root;
    }

    IEnumerable<UiNode> Content(DwView view) => _tab switch
    {
        Tab.World => WorldLines(view),
        Tab.Wars => Paged("dw-war", [.. view.Wars, .. view.Pacts], tr("at peace, no active truces")),
        Tab.News => Paged("dw-news", view.News, ""),
        Tab.Power => PowerLines(view),
        Tab.People => PeopleLines(view),
        Tab.Missions => MissionLines(view),
        _ => Paged("dw-soc", view.Society, ""),
    };

    void Send(string action, string target = "", int id = -1) => enqueue(new DwOrder(action, target, id) { Tick = tick() + 1 });

    UiNode Header()
    {
        var row = Row("dw-header");
        row.Children.Add(Label("dw-title", "Dynamic Wasteland", 150, _text));
        foreach (var (tab, caption) in new[]
        {
            (Tab.World, "WORLD"), (Tab.Wars, "WARS"), (Tab.News, "NEWS"), (Tab.Power, "POWER"), (Tab.People, "PEOPLE"), (Tab.Missions, "MISSIONS"),
            (Tab.Society, "SOCIETY"),
        })
        {
            row.Children.Add(Button("tab-" + caption, tr(caption), 82, () => (_tab, _page) = (tab, 0), selected: _tab == tab));
        }

        row.Children.Add(Button("prev", "<", 24, () => _page = Math.Max(0, _page - 1)));
        row.Children.Add(Button("next", ">", 24, () => _page++));
        row.Children.Add(Button("close", "X", 24, () => Open = false));
        return row;
    }

    #region World

    static readonly (string Caption, float Width)[] Columns =
    [
        ("Nation", 200), ("Towns", 50), ("Military", 60), ("Economy", 60), ("Stability", 60), ("Exhaustion", 70), ("Treasury", 80),
    ];

    IEnumerable<UiNode> WorldLines(DwView view)
    {
        var head = Row("dw-nh");
        foreach (var (caption, w) in Columns) head.Children.Add(Label("dw-nh-" + caption, tr(caption), w, _dim));
        yield return head;

        var rows = Page(view.Nations, PageSize / 2);
        for (int i = 0; i < rows.Count; i++)
        {
            foreach (var node in NationLines(view, rows[i], i)) yield return node;
        }
    }

    IEnumerable<UiNode> NationLines(DwView view, NationRow n, int i)
    {
        var row = Row("dw-n" + i);
        string[] cells = [n.Name, Num(n.Towns), Num(n.Military), Num(n.Economy), Num(n.Stability), Num(n.Exhaustion), Num(n.Treasury)];
        for (int c = 0; c < cells.Length; c++)
        {
            row.Children.Add(c == 0
                ? new UiNode($"dw-n{i}-{c}", UiNodeKind.Label) { Placement = new(Columns[c].Width, Line), Text = cells[c], Style = _text, TextColour = n.Colour }
                : Label($"dw-n{i}-{c}", cells[c], Columns[c].Width, _text));
        }

        string faction = n.Faction;
        row.Children.Add(Button("enlist" + i, tr("ENLIST"), 70, () => Send("enlist", faction), selected: view.Patron == faction));
        if (view.HasCompany) row.Children.Add(Button("hire" + i, tr("HIRE OUT"), 80, () => Send("hire-out", faction)));
        yield return row;

        string relations = n.Leader + " (" + n.Government + ", " + n.Faith + ")" + (n.Wars.Length > 0 ? "   " + tr("At war with") + ": " + n.Wars : "") +
            (n.Allies.Length > 0 ? "   " + tr("ALLIANCE") + ": " + n.Allies : "");
        yield return Text("dw-nr" + i, "    " + relations, _dim);
    }

    static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);

    #endregion

    #region Power, people, missions

    /// <summary>The career's lines, the player's own actions, and the decrees against each other nation (Commander and above).</summary>
    IEnumerable<UiNode> PowerLines(DwView view)
    {
        for (int i = 0; i < view.Career.Count; i++) yield return Text("dw-c" + i, view.Career[i], _text);

        var row = Row("dw-pa");
        if (view.Patron != "") row.Children.Add(Button("resign", tr("RESIGN"), 90, () => Send("resign")));
        if (view.Rank >= 3) row.Children.Add(Button("levy", tr("LEVY (2000)"), 120, () => Send("levy")));
        row.Children.Add(Button("faith", tr("FOUND FAITH"), 120, () => Send("found-faith"), enabled: view.Merit >= 150 && view.Influence >= 50));
        row.Children.Add(Button("company", tr("FOUND COMPANY"), 140, () => Send("found-company"), enabled: !view.HasCompany));
        yield return row;

        if (view.Patron == "" || view.Rank < 4)
        {
            yield return Text("dw-dec-none", tr("Decrees need the rank of Commander."), _dim);
            yield break;
        }

        var others = Page(view.Nations.Where(n => n.Faction != view.Patron).ToList(), PageSize - 4);
        for (int i = 0; i < others.Count; i++)
        {
            string faction = others[i].Faction;
            var d = Row("dw-dec" + i);
            d.Children.Add(Label("dw-dec-n" + i, others[i].Name, 300, _text));
            d.Children.Add(Button("war" + i, tr("WAR (30)"), 100, () => Send("decree-war", faction)));
            d.Children.Add(Button("peace" + i, tr("TREATY (20)"), 110, () => Send("decree-peace", faction)));
            yield return d;
        }
    }

    IEnumerable<UiNode> PeopleLines(DwView view)
    {
        var people = Page(view.People, PageSize);
        for (int i = 0; i < people.Count; i++)
        {
            var p = people[i];
            int id = p.Id;
            var row = Row("dw-p" + i);
            row.Children.Add(Label("dw-pt" + i, p.Text, Width - 12 - 4 * 74 - 16, p.Active ? _text : _dim));
            row.Children.Add(Button("spy" + i, tr("SPY"), 70, () => Send("spy", id: id)));
            row.Children.Add(Button("expose" + i, tr("EXPOSE"), 70, () => Send("expose", id: id), enabled: p.Known));
            row.Children.Add(Button("blackmail" + i, tr("BLACKMAIL"), 70, () => Send("blackmail", id: id), enabled: p.Known));
            row.Children.Add(Button("flirt" + i, tr("COURT"), 70, () => Send("flirt", id: id)));
            yield return row;
        }
    }

    IEnumerable<UiNode> MissionLines(DwView view)
    {
        if (view.Missions.Count == 0)
        {
            yield return Text("dw-m-none", tr("No missions on the board."), _dim);
            yield break;
        }

        for (int i = 0; i < view.Missions.Count; i++)
        {
            var m = view.Missions[i];
            int id = m.Id;
            var row = Row("dw-m" + i);
            row.Children.Add(Label("dw-mt" + i, m.Text, Width - 12 - 110, _text));
            row.Children.Add(m.Active ? Label("dw-ma" + i, tr("accepted"), 100, _dim) : Button("accept" + i, tr("ACCEPT"), 100, () => Send("accept", id: id)));
            yield return row;
        }
    }

    #endregion

    #region Helpers

    IEnumerable<UiNode> Paged(string id, IReadOnlyList<string> items, string empty)
    {
        if (items.Count == 0 && empty.Length > 0) return [Text(id + "-none", empty, _dim)];

        return Page(items, PageSize).Select((s, i) => Text($"{id}-{i}", s, _text));
    }

    List<T> Page<T>(IReadOnlyList<T> items, int size)
    {
        int pages = Math.Max(1, (items.Count + size - 1) / size);
        _page = Math.Clamp(_page, 0, pages - 1);
        return items.Skip(_page * size).Take(size).ToList();
    }

    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    static UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    static UiNode Text(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    UiNode Button(string key, string text, float width, Action onClick, bool selected = false, bool enabled = true)
    {
        if (enabled) _clicks[key] = onClick;
        return new("dw-b-" + key, UiNodeKind.Button)
        {
            Placement = new(width, Line), Text = text, Style = selected ? _selected : _button, Action = enabled ? _click : "", Argument = key,
            Enabled = enabled,
        };
    }

    #endregion
}
