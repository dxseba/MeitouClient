using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.SquadAutonomy;

/// <summary>
/// The Squad Autonomy window (AUT): one player squad at a time, with the original's options: Enable Autonomy, the home building, the AI packages
/// with priorities (Add AI Package, Clear AI Packages, a searchable picker), the work hours and the Combat switches; this port's off-work
/// package. The view comes from the running system; every change goes out as a command.
/// </summary>
sealed class AutonomyWindow(ModContext context, PackageList packages) : IUiExtension
{
    /// <summary>
    /// What the picker is choosing for.
    /// </summary>
    enum Pick { None, Add, Rest }

    /// <summary>
    /// The window's width and a line's height.
    /// </summary>
    const float Width = 640, Line = 22;

    /// <summary>
    /// Packages per picker page.
    /// </summary>
    const int PageSize = 14;

    /// <summary>
    /// The original's priority slider's range.
    /// </summary>
    const int MaxPriority = 5;

    /// <summary>
    /// The button font's height at text scale 1 (<b>Unknown</b>, an estimate), for <see cref="AutonomySettings.BtnFontSize"/>.
    /// </summary>
    const float FontPx = 18;

    /// <summary>
    /// The registered style keys.
    /// </summary>
    readonly record struct Styles(string Panel, string Text, string Dim, string Button, string Selected, string Box);

    /// <summary>
    /// The click handlers of this frame's buttons, by argument.
    /// </summary>
    readonly Dictionary<string, Action> _clicks = new(StringComparer.Ordinal);

    /// <summary>
    /// The styles.
    /// </summary>
    Styles _styles;

    /// <summary>
    /// The click and search actions.
    /// </summary>
    string _click = "", _searchAction = "", _autAction = "", _autStyle = "";

    /// <summary>
    /// Whether the AUT button's press has moved it (a drag, not a click).
    /// </summary>
    bool _dragged;

    /// <summary>
    /// Whether the window is open.
    /// </summary>
    bool _open;

    /// <summary>
    /// The shown squad's index and the picker's page.
    /// </summary>
    int _squad, _page;

    /// <summary>
    /// The priority the next added package takes (the original's slider).
    /// </summary>
    int _priority;

    /// <summary>
    /// What the picker chooses for.
    /// </summary>
    Pick _pick;

    /// <summary>
    /// Whether the picker lists every package (this port's) rather than the listed ones.
    /// </summary>
    bool _all;

    /// <summary>
    /// The picker's search text.
    /// </summary>
    string _search = "";

    public string Id => "dxseba.squadautonomy.ui";

    public void Register(UiRegistration registration)
    {
        var ink = new System.Numerics.Vector4(.91f, .86f, .74f, 1);
        var pad = new UiInsets(6, 1, 6, 1);
        _styles = new(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .95f), Text = ink }),
            registration.RegisterStyle("text", new() { Text = ink, TextPadding = pad }),
            registration.RegisterStyle("dim", new() { Text = new(.66f, .62f, .54f, 1), TextPadding = pad }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Disabled = new(.14f, .12f, .10f, .8f), TextPadding = pad,
            }),
            registration.RegisterStyle("selected", new() { Background = new(.45f, .32f, .14f, 1), Hover = new(.55f, .40f, .18f, 1), TextPadding = pad }),
            registration.RegisterStyle("box", new() { Background = new(.06f, .05f, .04f, 1), Text = ink, TextPadding = pad }));
        _click = registration.RegisterAction("click", OnClick);
        _searchAction = registration.RegisterAction("search", c => (_search, _page) = (c.Text, 0));
        _autAction = registration.RegisterAction("aut", OnAutButton);
        _autStyle = registration.RegisterStyle("aut", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" });
        registration.Contribute(UiSlots.Overlay, Build, order: 59);
    }

    /// <summary>
    /// Runs the clicked button's handler.
    /// </summary>
    void OnClick(UiActionContext c)
    {
        if (_clicks.TryGetValue(c.Argument, out var action)) action();
    }

    #region Layout

    /// <summary>
    /// The AUT button and, while open, the window.
    /// </summary>
    UiNode Build(UiFrameContext frame)
    {
        _clicks.Clear();
        var root = new UiNode("aut") { Placement = UiPlacement.Fill, Pickable = false };
        if (SquadAutonomyMod.Settings.ShowButton) root.Children.Add(AutButton(frame.Viewport));

        if (_open && SquadAutonomyMod.Running?.View is { } view) root.Children.Add(Window(frame.Viewport, view));
        return root;
    }

    /// <summary>
    /// The original's AUT button on the main bar: placed and sized by its settings as shares of the screen, its caption at the font size; it can
    /// be dragged while its position is not locked.
    /// </summary>
    UiNode AutButton(UiRect viewport)
    {
        var s = SquadAutonomyMod.Settings;
        float scale = Math.Clamp(s.BtnFontSize * viewport.Height / 1080 / FontPx, 0.1f, UiHost.MaxTextScale);
        return new("aut-toggle", UiNodeKind.Button)
        {
            Placement = new(s.BtnWidth / 100 * viewport.Width, s.BtnHeight / 100 * viewport.Height, Left: s.BtnLeft * viewport.Width,
                Top: s.BtnTop * viewport.Height),
            Text = "AUT", Style = _autStyle, Action = _autAction, Pointer = UiPointerEvents.Drag | UiPointerEvents.Release, TextScale = scale,
        };
    }

    /// <summary>
    /// The AUT button: a click opens or closes the window; a drag, while not locked, moves it and keeps the place (<c>BtnLeft</c>,
    /// <c>BtnTop</c>).
    /// </summary>
    void OnAutButton(UiActionContext c)
    {
        var s = SquadAutonomyMod.Settings;
        if (c.Kind == UiEventKind.Press) _dragged = false;
        else if (c.Kind == UiEventKind.Drag && !s.LockPosition && c.Delta != System.Numerics.Vector2.Zero) Move(c);
        else if (c.Kind == UiEventKind.Release && !_dragged) _open = !_open;
    }

    /// <summary>
    /// Moves the AUT button by the drag's step, kept on the screen.
    /// </summary>
    void Move(UiActionContext c)
    {
        _dragged = true;
        var s = SquadAutonomyMod.Settings;
        var viewport = c.Frame.Viewport;
        float left = Math.Clamp(s.BtnLeft + c.Delta.X / viewport.Width, 0, 1);
        float top = Math.Clamp(s.BtnTop + c.Delta.Y / viewport.Height, 0, 1);
        if (SquadAutonomyMod.SettingsGroup is not { } group)
        {
            (s.BtnLeft, s.BtnTop) = (left, top);
            return;
        }

        group.Settings.First(x => x.Key == nameof(AutonomySettings.BtnLeft)).Set(left);
        group.Settings.First(x => x.Key == nameof(AutonomySettings.BtnTop)).Set(top);
    }

    /// <summary>
    /// The window: the header, then the options or the picker.
    /// </summary>
    UiNode Window(UiRect viewport, AutonomyView view)
    {
        var lines = new List<UiNode>();
        if (view.Squads.Count == 0) lines.Add(Text("aut-none", "No squad selected/invalid squad", _styles.Dim));
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

    /// <summary>
    /// The title, the squad pager and Close.
    /// </summary>
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

    /// <summary>
    /// The squad's options.
    /// </summary>
    List<UiNode> Details(SquadRow squad, AutonomyView view)
    {
        var plan = squad.Plan ?? Plan.Default(squad.Name);
        var lines = new List<UiNode> { EnableLine(squad), HomeLine(squad, view) };
        lines.AddRange(PackageLines(plan));
        lines.Add(PriorityLine(squad, plan));
        lines.Add(HourLine(squad, plan, "start", "Start Work Time", plan.StartHour, h => plan with { StartHour = h }, $"Now {view.Hour:00}:00"));
        lines.Add(HourLine(squad, plan, "end", "End Work Time", plan.EndHour, h => plan with { EndHour = h }, ""));
        lines.Add(RestLine(plan));
        lines.Add(RecoveryLine(squad, plan));
        lines.Add(ThresholdLine(squad, plan));
        lines.Add(CombatLine(squad, plan));

        string hint = !squad.Autonomous ? "Enable Autonomy to let the AI run this squad." : plan.Packages.Count == 0 ? "Add an AI package." : "";
        if (hint != "") lines.Add(Text("aut-hint", hint, _styles.Dim));
        return lines;
    }

    /// <summary>
    /// Enable Autonomy and the running package.
    /// </summary>
    UiNode EnableLine(SquadRow squad)
    {
        var row = Row("aut-on");
        row.Children.Add(Label("aut-on-l", "Enable Autonomy", 140, _styles.Dim));
        row.Children.Add(Button("switch", squad.Autonomous ? "On" : "Off", 60, () => Switch(squad), selected: squad.Autonomous));
        row.Children.Add(Label("aut-pkg", "Running: " + packages.NameOf(squad.Package), Width - 12 - 140 - 60 - 8, _styles.Text));
        return row;
    }

    /// <summary>
    /// Home:, Set Squad Home Building and Clear Buildings.
    /// </summary>
    UiNode HomeLine(SquadRow squad, AutonomyView view)
    {
        var row = Row("aut-home");
        row.Children.Add(Label("aut-home-l", "Home:", 60, _styles.Dim));
        row.Children.Add(Label("aut-home-v", squad.Home, 210, _styles.Text));
        string set = view.Inspected is { } b ? "Set Squad Home Building: " + b : "Set Squad Home Building";
        row.Children.Add(Button("home.set", set, 230, () => Home(squad, false)));
        row.Children.Add(Button("home.clear", "Clear Buildings", 110, () => Home(squad, true), squad.Home != "None"));
        return row;
    }

    /// <summary>
    /// The plan's packages, highest priority first.
    /// </summary>
    IEnumerable<UiNode> PackageLines(Plan plan)
    {
        if (plan.Packages.Count == 0)
        {
            yield return Text("aut-pk-none", "AI package: None", _styles.Text);
            yield break;
        }

        int i = 0;
        foreach (var p in plan.Packages.OrderByDescending(p => p.Priority))
            yield return Text($"aut-pk-{i++}", $"AI package: {packages.NameOf(p.Id)} (priority {p.Priority})", _styles.Text);
    }

    /// <summary>
    /// Priority, Add AI Package and Clear AI Packages.
    /// </summary>
    UiNode PriorityLine(SquadRow squad, Plan plan)
    {
        var row = Row("aut-priority");
        row.Children.Add(Label("aut-priority-l", "Priority", 140, _styles.Dim));
        row.Children.Add(Button("priority.down", "-", 24, () => _priority = Math.Max(0, _priority - 1), _priority > 0));
        row.Children.Add(Label("aut-priority-v", _priority.ToString(System.Globalization.CultureInfo.InvariantCulture), 30, _styles.Text));
        row.Children.Add(Button("priority.up", "+", 24, () => _priority = Math.Min(MaxPriority, _priority + 1), _priority < MaxPriority));
        row.Children.Add(Button("add", "Add AI Package", 140, () => (_pick, _page, _search) = (Pick.Add, 0, "")));
        row.Children.Add(Button("clear", "Clear AI Packages", 150, () => Save(squad, plan with { Packages = [] }), plan.Packages.Count > 0));
        return row;
    }

    /// <summary>
    /// A work-hours slider (0-24) as - and + buttons.
    /// </summary>
    UiNode HourLine(SquadRow squad, Plan plan, string key, string title, int value, Func<int, Plan> with, string note)
    {
        var row = Row("aut-" + key);
        row.Children.Add(Label($"aut-{key}-l", title, 140, _styles.Dim));
        row.Children.Add(Button(key + ".down", "-", 24, () => Save(squad, with(value - 1)), value > 0));
        row.Children.Add(Label($"aut-{key}-v", $"{value:00}:00", 60, _styles.Text));
        row.Children.Add(Button(key + ".up", "+", 24, () => Save(squad, with(value + 1)), value < 24));
        row.Children.Add(Label($"aut-{key}-n", note, 120, _styles.Dim));
        return row;
    }

    /// <summary>
    /// This port's off-work package.
    /// </summary>
    UiNode RestLine(Plan plan)
    {
        var row = Row("aut-rest");
        row.Children.Add(Label("aut-rest-l", "Off-work package", 140, _styles.Dim));
        row.Children.Add(Label("aut-rest-v", packages.NameOf(plan.Rest), 360, _styles.Text));
        row.Children.Add(Button("rest.pick", "Choose", 90, () => (_pick, _page, _search) = (Pick.Rest, 0, "")));
        return row;
    }

    /// <summary>
    /// The original's Rest Until Healed When Wounded and Allow Sleep Outside Work Hours.
    /// </summary>
    UiNode RecoveryLine(SquadRow squad, Plan plan)
    {
        var rules = plan.Rules;
        var row = Row("aut-recovery");
        row.Children.Add(Label("aut-recovery-l", "Basic", 64, _styles.Dim));
        row.Children.Add(Button("rest.healed", "Rest Until Healed When Wounded", 230,
            () => Save(squad, plan with { Resting = rules with { UntilHealed = !rules.UntilHealed } }), selected: rules.UntilHealed));
        row.Children.Add(Button("rest.sleep", "Allow Sleep Outside Work Hours", 230,
            () => Save(squad, plan with { Resting = rules with { SleepOffWork = !rules.SleepOffWork } }), selected: rules.SleepOffWork));
        return row;
    }

    /// <summary>
    /// The original's Rest Health Threshold and Healed Health Threshold sliders (0-100) as - and + buttons, in steps of 5.
    /// </summary>
    UiNode ThresholdLine(SquadRow squad, Plan plan)
    {
        var rules = plan.Rules;
        var row = Row("aut-thresholds");
        Threshold(row, squad, plan, "rest", "Rest Health Threshold", rules.RestHealth, v => rules with { RestHealth = v });
        Threshold(row, squad, plan, "heal", "Healed Health Threshold", rules.HealedHealth, v => rules with { HealedHealth = v });
        return row;
    }

    /// <summary>
    /// One threshold slider of <see cref="ThresholdLine"/>.
    /// </summary>
    void Threshold(UiNode row, SquadRow squad, Plan plan, string key, string title, int value, Func<int, RestRules> with)
    {
        row.Children.Add(Label($"aut-{key}h-l", title, 170, _styles.Dim));
        row.Children.Add(Button(key + "h.down", "-", 24, () => Save(squad, plan with { Resting = with(Math.Max(0, value - 5)) }), value > 0));
        row.Children.Add(Label($"aut-{key}h-v", value.ToString(System.Globalization.CultureInfo.InvariantCulture), 36, _styles.Text));
        row.Children.Add(Button(key + "h.up", "+", 24, () => Save(squad, plan with { Resting = with(Math.Min(100, value + 5)) }), value < 100));
    }

    /// <summary>
    /// The original's Combat switches.
    /// </summary>
    UiNode CombatLine(SquadRow squad, Plan plan)
    {
        var row = Row("aut-combat");
        row.Children.Add(Label("aut-combat-l", "Combat", 64, _styles.Dim));
        foreach (var (flag, text, width) in Switches)
        {
            bool on = (plan.Switches & flag) != 0;
            row.Children.Add(Button("combat." + flag, text, width, () => Save(squad, plan with { Switches = plan.Switches ^ flag }), selected: on));
        }

        return row;
    }

    /// <summary>
    /// The switches as the original's window names them, and their buttons' widths.
    /// </summary>
    static readonly (CombatSwitches Flag, string Text, float Width)[] Switches =
    [
        (CombatSwitches.AttackEnemies, "Attack Enemies", 112), (CombatSwitches.ProtectAllies, "Protect Allies", 106),
        (CombatSwitches.DoMedic, "Do Medic", 80), (CombatSwitches.DoRescue, "Rescue Downed Allies", 150),
        (CombatSwitches.ManTurrets, "Man Turrets", 92),
    ];

    #endregion

    #region Picker

    /// <summary>
    /// The package picker: Listed or All, a search box, pages of packages.
    /// </summary>
    List<UiNode> Picker(SquadRow squad)
    {
        var source = _all || packages.Featured.Count == 0 ? packages.All : packages.Featured;
        var list = source.Where(p => _search == "" || p.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
        _page = Math.Clamp(_page, 0, Math.Max(0, (list.Count - 1) / PageSize));
        var plan = squad.Plan ?? Plan.Default(squad.Name);

        var lines = new List<UiNode> { PickerBar(squad, plan, list.Count), SearchLine() };
        foreach (var (entry, i) in list.Skip(_page * PageSize).Take(PageSize).Select((e, i) => (e, i)))
        {
            var row = Row($"aut-p{i}");
            row.Children.Add(Button($"pick.{i}", entry.Name, Width - 12, () => Choose(squad, plan, entry.Id)));
            lines.Add(row);
        }

        return lines;
    }

    /// <summary>
    /// The picker's title, list switch, pager, None (off-work only) and Back.
    /// </summary>
    UiNode PickerBar(SquadRow squad, Plan plan, int count)
    {
        var bar = Row("aut-pickbar");
        string title = _pick == Pick.Add ? $"AI package (priority {_priority})" : "Off-work package";
        bar.Children.Add(Label("aut-pick-l", title, 170, _styles.Dim));
        bar.Children.Add(Button("list.featured", "Listed", 70, () => (_all, _page) = (false, 0), packages.Featured.Count > 0, !_all));
        bar.Children.Add(Button("list.all", "All", 50, () => (_all, _page) = (true, 0), selected: _all));
        bar.Children.Add(Button("page.prev", "<", 28, () => _page--, _page > 0));
        bar.Children.Add(Label("aut-page", $"{_page + 1}/{Math.Max(1, (count + PageSize - 1) / PageSize)}", 60, _styles.Text));
        bar.Children.Add(Button("page.next", ">", 28, () => _page++, (_page + 1) * PageSize < count));
        if (_pick == Pick.Rest) bar.Children.Add(Button("pick.none", "None", 60, () => Choose(squad, plan, null)));
        bar.Children.Add(Button("pick.back", "Back", 60, () => _pick = Pick.None));
        return bar;
    }

    /// <summary>
    /// The original's Search box.
    /// </summary>
    UiNode SearchLine()
    {
        var row = Row("aut-search");
        row.Children.Add(Label("aut-search-l", "Search", 70, _styles.Dim));
        row.Children.Add(new("aut-search-box", UiNodeKind.TextBox)
        {
            Placement = new(300, Line), Text = _search, Action = _searchAction, Style = _styles.Box, MaxLength = 64,
        });
        return row;
    }

    /// <summary>
    /// Adds the package at the chosen priority, or sets the off-work package.
    /// </summary>
    void Choose(SquadRow squad, Plan plan, string? id)
    {
        if (_pick == Pick.Add && id is not null) Save(squad, plan with { Packages = [.. plan.Packages, new PlannedPackage(id, _priority)] });
        else if (_pick == Pick.Rest) Save(squad, plan with { Rest = id });

        _pick = Pick.None;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Enable Autonomy's switch.
    /// </summary>
    void Switch(SquadRow squad) => context.Enqueue(new AutonomySwitchOrder(squad.Id, !squad.Autonomous) { Tick = context.Tick });

    /// <summary>
    /// Sends the squad's plan.
    /// </summary>
    void Save(SquadRow squad, Plan plan) =>
        context.Enqueue(new SquadPlanOrder(squad.Id, plan.Packages, plan.Rest, plan.StartHour, plan.EndHour, plan.Switches, plan.Resting)
        {
            Tick = context.Tick,
        });

    /// <summary>
    /// Sets or clears the home.
    /// </summary>
    void Home(SquadRow squad, bool clear) => context.Enqueue(new SquadHomeOrder(squad.Id, clear) { Tick = context.Tick });

    #endregion

    #region Nodes

    /// <summary>
    /// A row of nodes.
    /// </summary>
    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    /// <summary>
    /// A label of a width.
    /// </summary>
    static UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    /// <summary>
    /// A full-width label.
    /// </summary>
    static UiNode Text(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    /// <summary>
    /// A button whose click runs <paramref name="onClick"/>.
    /// </summary>
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
