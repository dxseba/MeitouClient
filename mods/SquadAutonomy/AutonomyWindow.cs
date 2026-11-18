using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Meitou.SquadAutonomy;

/// <summary>
/// The Squad Autonomy window (AUT): one player squad at a time; its autonomy switch, home, work and rest packages and work hours, and a paged
/// package picker. The view comes from the running system; every change goes out as a command.
/// </summary>
sealed class AutonomyWindow(ModContext context, PackageList packages) : IUiExtension
{
    enum Pick { None, Work, Rest }

    const float Width = 640, Line = 22;
    const int PageSize = 14;

    readonly record struct Styles(string Panel, string Text, string Dim, string Button, string Selected);

    readonly Dictionary<string, Action> _clicks = new(StringComparer.Ordinal);
    Styles _styles;
    string _click = "";
    bool _open;
    int _squad, _page;
    Pick _pick;
    bool _all;

    public string Id => "dxseba.squadautonomy.ui";

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
            }),
            registration.RegisterStyle("selected", new() { Background = new(.45f, .32f, .14f, 1), Hover = new(.55f, .40f, .18f, 1), TextPadding = pad }));
        _click = registration.RegisterAction("click", c =>
        {
            if (_clicks.TryGetValue(c.Argument, out var action)) action();
        });
        registration.Contribute(UiSlots.Overlay, Build, order: 59);
    }

    #region Layout

    UiNode Build(UiFrameContext frame)
    {
        _clicks.Clear();
        var root = new UiNode("aut") { Placement = UiPlacement.Fill, Pickable = false };
        if (SquadAutonomyMod.Settings.ShowButton)
        {
            _clicks["toggle"] = () => _open = !_open;
            root.Children.Add(new("aut-toggle", UiNodeKind.Button)
            {
                Placement = new(44, 26, Left: 530, Top: 44), Text = "AUT", Style = _open ? _styles.Selected : _styles.Button, Action = _click,
                Argument = "toggle",
            });
        }

        if (_open && SquadAutonomyMod.Running?.View is { } view) root.Children.Add(Window(frame.Viewport, view));
        return root;
    }

    UiNode Window(UiRect viewport, AutonomyView view)
    {
        var lines = new List<UiNode>();
        if (view.Squads.Count == 0) lines.Add(Text("aut-none", "No squads.", _styles.Dim));
        else
        {
            _squad = (_squad % view.Squads.Count + view.Squads.Count) % view.Squads.Count;
            var squad = view.Squads[_squad];
            lines.Add(Header(squad, view.Squads.Count));
            lines.AddRange(_pick == Pick.None ? Details(squad, view) : Picker(squad));
        }

        var window = new UiNode("aut-window")
        {
            Placement = new(Width, 12 + lines.Count * (Line + 2), Left: (viewport.Width - Width) / 2, Top: 90), Style = _styles.Panel,
            Flow = UiFlow.Column, Padding = 6, Gap = 2,
        };
        window.Children.AddRange(lines);
        return window;
    }

    UiNode Header(SquadRow squad, int count)
    {
        var row = Row("aut-header");
        row.Children.Add(Label("aut-title", "Squad Autonomy", 150, _styles.Text));
        row.Children.Add(Button("prev", "<", 28, () => (_squad, _pick) = (_squad - 1, Pick.None), count > 1));
        row.Children.Add(Label("aut-squad", $"{squad.Name} ({_squad + 1}/{count})", Width - 12 - 150 - 2 * 28 - 70 - 4 * 4, _styles.Text));
        row.Children.Add(Button("next", ">", 28, () => (_squad, _pick) = (_squad + 1, Pick.None), count > 1));
        row.Children.Add(Button("close", "Close", 70, () => _open = false));
        return row;
    }

    List<UiNode> Details(SquadRow squad, AutonomyView view)
    {
        var plan = squad.Plan ?? new(squad.Name, null, null, 8, 20);
        var lines = new List<UiNode>();

        var on = Row("aut-on");
        on.Children.Add(Label("aut-on-l", "Autonomy", 120, _styles.Dim));
        on.Children.Add(Button("switch", squad.Autonomous ? "On" : "Off", 60, () => Switch(squad), selected: squad.Autonomous));
        on.Children.Add(Label("aut-pkg", "Running: " + packages.NameOf(squad.Package), Width - 12 - 120 - 60 - 8, _styles.Text));
        lines.Add(on);

        var home = Row("aut-home");
        home.Children.Add(Label("aut-home-l", "Home", 120, _styles.Dim));
        home.Children.Add(Label("aut-home-v", squad.Home, 220, _styles.Text));
        home.Children.Add(Button("home.set", view.Inspected is { } b ? "Set: " + b : "Select a building", 200,
            () => Home(squad, false), view.Inspected is not null));
        home.Children.Add(Button("home.clear", "Clear", 70, () => Home(squad, true), squad.Home != "None"));
        lines.Add(home);

        lines.Add(PackageLine("work", "Work package", plan.Work, Pick.Work));
        lines.Add(PackageLine("rest", "Rest package", plan.Rest, Pick.Rest));
        lines.Add(HoursLine(squad, plan, view.Hour));

        string hint = !squad.Autonomous ? "Switch autonomy on to let the AI run this squad." : plan.Work is null ? "Pick a work package." : "";
        if (hint != "") lines.Add(Text("aut-hint", hint, _styles.Dim));
        return lines;
    }

    UiNode PackageLine(string key, string title, string? id, Pick pick)
    {
        var row = Row("aut-" + key);
        row.Children.Add(Label($"aut-{key}-l", title, 120, _styles.Dim));
        row.Children.Add(Label($"aut-{key}-v", packages.NameOf(id), 380, _styles.Text));
        row.Children.Add(Button(key + ".pick", "Choose", 90, () => (_pick, _page) = (pick, 0)));
        return row;
    }

    UiNode HoursLine(SquadRow squad, Plan plan, int hour)
    {
        var row = Row("aut-hours");
        row.Children.Add(Label("aut-hours-l", "Work hours", 120, _styles.Dim));
        row.Children.Add(Button("start.down", "-", 24, () => Save(squad, plan with { StartHour = (plan.StartHour + 23) % 24 })));
        row.Children.Add(Label("aut-start", $"{plan.StartHour:00}:00", 60, _styles.Text));
        row.Children.Add(Button("start.up", "+", 24, () => Save(squad, plan with { StartHour = (plan.StartHour + 1) % 24 })));
        row.Children.Add(Label("aut-to", "to", 30, _styles.Dim));
        row.Children.Add(Button("end.down", "-", 24, () => Save(squad, plan with { EndHour = (plan.EndHour + 23) % 24 })));
        row.Children.Add(Label("aut-end", $"{plan.EndHour:00}:00", 60, _styles.Text));
        row.Children.Add(Button("end.up", "+", 24, () => Save(squad, plan with { EndHour = (plan.EndHour + 1) % 24 })));
        row.Children.Add(Label("aut-now", $"Now {hour:00}:00", 100, _styles.Dim));
        return row;
    }

    #endregion

    #region Picker

    List<UiNode> Picker(SquadRow squad)
    {
        var list = _all || packages.Featured.Count == 0 ? packages.All : packages.Featured;
        _page = Math.Clamp(_page, 0, Math.Max(0, (list.Count - 1) / PageSize));
        var plan = squad.Plan ?? new(squad.Name, null, null, 8, 20);

        var bar = Row("aut-pickbar");
        bar.Children.Add(Label("aut-pick-l", _pick == Pick.Work ? "Work package" : "Rest package", 120, _styles.Dim));
        bar.Children.Add(Button("list.featured", "Listed", 70, () => (_all, _page) = (false, 0), packages.Featured.Count > 0, !_all));
        bar.Children.Add(Button("list.all", "All", 50, () => (_all, _page) = (true, 0), selected: _all));
        bar.Children.Add(Button("page.prev", "<", 28, () => _page--, _page > 0));
        bar.Children.Add(Label("aut-page", $"{_page + 1}/{(list.Count + PageSize - 1) / PageSize}", 60, _styles.Text));
        bar.Children.Add(Button("page.next", ">", 28, () => _page++, (_page + 1) * PageSize < list.Count));
        bar.Children.Add(Button("pick.none", "None", 60, () => Choose(squad, plan, null)));
        bar.Children.Add(Button("pick.back", "Back", 60, () => _pick = Pick.None));

        var lines = new List<UiNode> { bar };
        foreach (var (entry, i) in list.Skip(_page * PageSize).Take(PageSize).Select((e, i) => (e, i)))
        {
            var row = Row($"aut-p{i}");
            row.Children.Add(Button($"pick.{i}", entry.Name, Width - 12, () => Choose(squad, plan, entry.Id)));
            lines.Add(row);
        }

        return lines;
    }

    void Choose(SquadRow squad, Plan plan, string? id)
    {
        Save(squad, _pick == Pick.Work ? plan with { Work = id } : plan with { Rest = id });
        _pick = Pick.None;
    }

    #endregion

    #region Commands

    void Switch(SquadRow squad) => context.Enqueue(new SetSquadAutonomy(squad.Id, !squad.Autonomous, Owner: context.Id) { Tick = context.Tick });

    void Save(SquadRow squad, Plan plan) =>
        context.Enqueue(new SquadPlanOrder(squad.Id, plan.Work, plan.Rest, plan.StartHour, plan.EndHour) { Tick = context.Tick });

    void Home(SquadRow squad, bool clear) => context.Enqueue(new SquadHomeOrder(squad.Id, clear) { Tick = context.Tick });

    #endregion

    #region Nodes

    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    static UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    static UiNode Text(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    UiNode Button(string key, string text, float width, Action onClick, bool enabled = true, bool selected = false)
    {
        _clicks[key] = onClick;
        return new("aut-b-" + key, UiNodeKind.Button)
        {
            Placement = new(width, Line), Text = text, Style = selected ? _styles.Selected : _styles.Button, Action = enabled ? _click : "",
            Argument = key, Enabled = enabled,
        };
    }

    #endregion
}
