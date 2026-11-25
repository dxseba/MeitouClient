using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Mercenarie;

/// <summary>
/// The guild window (the original's contract board and guild management, J): Board (offers, negotiation, Accept), Contracts (accepted, Cancel),
/// History and Guild (level, XP, prestige, reputation, Guild Houses, the Cats to XP exchange). Also the replies to bartenders and Guild House
/// clients, and the building panel's "Define guild building". Interface thread only; the world changes through queued commands.
/// </summary>
sealed class GuildWindow(ModContext context) : IUiExtension
{
    const float Width = 700, Line = 20, Lines = 16;
    static readonly string[] Tabs = ["Board", "Contracts", "History", "Guild"];

    static volatile bool s_open;
    static volatile int s_tab;
    CharacterId _bartender, _player;
    int _offer = -1, _percent;

    public string Id => "dxseba.mercenarie.guild";

    public static void Toggle() => s_open = !s_open;

    readonly record struct Styles(string Panel, string Text, string Button);

    readonly record struct Actions(string Tab, string Accept, string Cancel, string Close, string Pick, string Demand, string Propose,
        string Exchange);

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.09f, .08f, .07f, .95f), Text = new(.92f, .87f, .75f, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.92f, .87f, .75f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.22f, .16f, .10f, .95f),
                Hover = new(.34f, .24f, .15f, 1),
                Disabled = new(.14f, .12f, .10f, .8f),
                TextPadding = new(6, 2, 6, 2),
            }));
        var actions = RegisterActions(registration);
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 61);
        registration.AddDialogueReplies(Replies);
        registration.AddBuildingActions(BuildingActions);
    }

    Actions RegisterActions(UiRegistration r) => new(
        r.RegisterAction("tab", a => s_tab = int.Parse(a.Argument)),
        r.RegisterAction("accept", a => Send(new AcceptOffer(int.Parse(a.Argument), _bartender, _player) { Tick = context.Tick })),
        r.RegisterAction("cancel", a => Send(new CancelContract(int.Parse(a.Argument)) { Tick = context.Tick })),
        r.RegisterAction("close", _ => s_open = false),
        r.RegisterAction("pick", a => (_offer, _percent) = (int.Parse(a.Argument), 0)),
        r.RegisterAction("demand", a => _percent = Math.Clamp(_percent + int.Parse(a.Argument), -20, 25)),
        r.RegisterAction("propose", _ => Send(new NegotiateOffer(_offer, _percent) { Tick = context.Tick })),
        r.RegisterAction("exchange", _ => Send(new ExchangeCats(1) { Tick = context.Tick })));

    void Send(SimCommand command) => context.Enqueue(command);

    #region Replies and the building panel

    IEnumerable<UiDialogueReply> Replies(UiDialogueContext c)
    {
        var view = GuildSystem.Running?.View;
        if (c.Npc is not { } npc || !GuildSystem.IsBartenderName(npc.Name) && view?.Clients.Contains(npc.Id) != true) yield break;

        var (giver, player) = (npc.Id, c.Conversation.Player);
        Action<SimCommand> enqueue = c.Enqueue;
        yield return new UiDialogueReply(context.Tr("I am looking for a contract."), () =>
        {
            (_bartender, _player, _offer) = (giver, player, -1);
            enqueue(new AskBoard(giver, player) { Tick = c.Tick });
            (s_open, s_tab) = (true, 0);
            c.Leave();
        });

        bool report = view?.Active.Any(a => a.Kind == ContractKind.Mail || a is { Kind: ContractKind.Bounty, Stage: 3 }) == true;
        if (report)
        {
            yield return new UiDialogueReply(context.Tr("I have come to report a contract."),
                () => enqueue(new ReportToBartender(giver, player) { Tick = c.Tick }));
        }
    }

    IEnumerable<UiBuildingAction> BuildingActions(UiBuildingContext b)
    {
        if (GuildSystem.Running?.View is not { } view) yield break;

        string key = b.Building.Instance;
        Action<SimCommand> enqueue = b.Enqueue;
        var lease = view.Leases.FirstOrDefault(l => l.Building == key);
        if (lease is not null)
        {
            yield return new UiBuildingAction("END LEASE", "End this lease immediately, without a refund? Existing debt remains due.",
                () => enqueue(new LeaseBuilding(key, End: true) { Tick = b.Tick }));
            yield return new UiBuildingAction($"BUY OUT ({lease.Weekly * 10 + lease.Debt} Cats)", null,
                () => enqueue(new BuyOutLease(key) { Tick = b.Tick }));
        }
        else if (!b.Building.Owned && b.Building.SiteId is null)
        {
            yield return new UiBuildingAction("RENT THIS BUILDING", "Weekly rent: a tenth of its price. The first week is paid at signing.",
                () => enqueue(new LeaseBuilding(key, End: false) { Tick = b.Tick }));
        }

        if (!b.Building.Owned) yield break;

        bool house = view.Houses.Any(h => h.Building == key);
        string caption = house ? "ABANDON GUILD HOUSE" : "DEFINE GUILD BUILDING";
        yield return new UiBuildingAction(caption, house ? null : "Guild level 3: one Guild House, level 4: three; one per town.",
            () => enqueue(new DefineGuildHouse(key, Abandon: house) { Tick = b.Tick }));
    }

    #endregion

    #region The window

    UiNode Build(Styles styles, Actions actions)
    {
        if (!s_open) return new UiNode("merc-hidden") { Visible = false };

        var body = new UiNode("merc-guild")
        {
            Placement = new(Width, 16 + (Lines + 6) * (Line + 5), Left: 160, Top: 80),
            Style = styles.Panel,
            Flow = UiFlow.Column,
            Padding = 4,
            Gap = 1,
        };
        var view = GuildSystem.Running?.View;
        body.Children.Add(Label("merc-title", Title(view), styles.Text));
        body.Children.Add(TabRow(styles, actions));
        if (view is not null) AddTab(body, view, styles, actions);

        body.Children.Add(Button("merc-close", context.Tr("Close"), actions.Close, "", styles.Button, 120));
        var root = new UiNode("merc-root") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(body);
        return root;
    }

    string Title(GuildView? v)
    {
        if (v is null) return "THE MERCENARIE";

        string next = v.Level < 10 ? $"{v.Xp} / {Pricing.XpFor(v.Level + 1)} XP" : $"{context.Tr("Prestige")}: {v.Prestige}";
        return $"THE MERCENARIE  |  {context.Tr("GUILD LEVEL")} {v.Level}  |  {next}  |  {context.Tr("day")} {v.Day}";
    }

    void AddTab(UiNode body, GuildView v, Styles styles, Actions actions)
    {
        switch (s_tab)
        {
            case 0: AddBoard(body, v, styles, actions); break;
            case 1: AddActive(body, v, styles, actions); break;
            case 2: AddLines(body, v.History.AsEnumerable().Reverse().Select(History), styles); break;
            default: AddGuild(body, v, styles, actions); break;
        }
    }

    void AddBoard(UiNode body, GuildView v, Styles styles, Actions actions)
    {
        if (v.Board is not { Count: > 0 } board)
        {
            string none = context.Tr("No active contract. Visit a bartender or a Guild House to obtain a mission.");
            body.Children.Add(Label("merc-noboard", none, styles.Text));
            return;
        }

        body.Children.Add(Label("merc-boardtown", $"{context.Tr("CONTRACTS")} - {v.BoardTown}", styles.Text));
        for (int i = 0; i < board.Count; i++)
        {
            string text = Describe(board[i], v.Day, v.Negotiation is { } n && n.Index == i ? n.Offered : board[i].Reward);
            body.Children.Add(Row($"merc-offer{i}", text, context.Tr("ACCEPT THE CONTRACT"), actions.Accept, i, styles));
        }

        AddNegotiation(body, v, board, styles, actions);
    }

    void AddNegotiation(UiNode body, GuildView v, IReadOnlyList<Contract> board, Styles styles, Actions actions)
    {
        if (v.Level < 2)
        {
            body.Children.Add(Label("merc-neg-locked", context.Tr("Negotiation locked up to level 2."), styles.Text));
            return;
        }

        var line = new UiNode("merc-neg") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        for (int i = 0; i < board.Count; i++)
            line.Children.Add(Button($"merc-pick{i}", $"#{i + 1}", actions.Pick, i.ToString(), styles.Button, 40, i != _offer));

        bool picked = (uint)_offer < (uint)board.Count;
        line.Children.Add(Button("merc-minus", "-5%", actions.Demand, "-5", styles.Button, 60, picked));
        line.Children.Add(Label("merc-demand", $"{_percent:+0;-0;0}%", styles.Text, 60));
        line.Children.Add(Button("merc-plus", "+5%", actions.Demand, "5", styles.Button, 60, picked));
        line.Children.Add(Button("merc-propose", context.Tr("Propose"), actions.Propose, "", styles.Button, 120, picked));
        body.Children.Add(line);

        if (v.Negotiation is { } n)
            body.Children.Add(Label("merc-answer", $"{n.Answer}  ({context.Tr("Attempts used")}: {n.Attempts}/2)", styles.Text));
    }

    void AddActive(UiNode body, GuildView v, Styles styles, Actions actions)
    {
        if (v.Active.Count == 0) body.Children.Add(Label("merc-noactive", context.Tr("No active contract."), styles.Text));

        for (int i = 0; i < v.Active.Count; i++)
        {
            var c = v.Active[i];
            string text = $"{Describe(c, v.Day, c.Reward)} - {Stage(c)}";
            body.Children.Add(Row($"merc-active{i}", text, context.Tr("CANCEL CONTRACT"), actions.Cancel, i, styles));
        }
    }

    string Stage(Contract c) => context.Tr(c switch
    {
        { Kind: ContractKind.Mail } => "deliver the letter",
        { Kind: ContractKind.Bounty, Stage: 3 } => "report to the bartender",
        { Kind: ContractKind.Bounty } => "hunt them down",
        { Squad: < 0 } => "the client is on the way",
        { Stage: 1 } => "research at the ruin",
        { Stage: 2 } => "return leg",
        _ => "outbound",
    });

    void AddGuild(UiNode body, GuildView v, Styles styles, Actions actions)
    {
        AddLines(body, Guild(v), styles);
        body.Children.Add(Button("merc-exchange", context.Tr("CATS > XP EXCHANGE (10,000 Cats: 120 XP)"), actions.Exchange, "", styles.Button, 360));
    }

    IEnumerable<string> Guild(GuildView v)
    {
        yield return $"{context.Tr("Guild XP")}: {v.Xp}    {context.Tr("Prestige")}: {v.Prestige}";
        yield return context.Tr("YOUR GUILD HOUSES") + (v.Houses.Count == 0 ? ": " + context.Tr("No active Guild House.") : "");
        foreach (var h in v.Houses) yield return $"  {h.Name}";

        foreach (var l in v.Leases)
        {
            yield return $"  {context.Tr("Lease")}: {l.Weekly} Cats / {context.Tr("week")}, {context.Tr("next")} {context.Tr("day")} {l.NextDue}, "
                + $"{context.Tr("debt")} {l.Debt}";
        }

        yield return context.Tr("AREAS AND REPUTATIONS");
        foreach (var (town, rep) in v.Reputation.OrderByDescending(r => r.Rep).Take(8)) yield return $"  {town}: {rep:+0;-0;0}";
    }

    void AddLines(UiNode body, IEnumerable<string> lines, Styles styles)
    {
        int i = 0;
        foreach (string line in lines.Take((int)Lines)) body.Children.Add(Label($"merc-line{i++}", line, styles.Text));
    }

    string Describe(Contract c, int day, int reward) =>
        $"{Kind(c.Kind)}: {c.FromName} > {c.ToName}, {c.Km:0.0} km, {context.Tr("danger")} {c.Danger}/5, {reward} Cats, "
        + $"{Math.Max(0, c.Deadline - day)} {context.Tr("day(s)")}";

    string History(ClosedContract h)
    {
        string result = h.Success ? $"{context.Tr("Completed")}, {h.Reward} Cats, +{h.Xp} XP" : context.Tr("Failed");
        return $"{context.Tr("day")} {h.Day}: {Kind(h.Kind)} {h.FromName} > {h.ToName} - {result}";
    }

    string Kind(ContractKind k) => GuildSystem.Running?.Name(k) ?? k.ToString();

    UiNode TabRow(Styles styles, Actions actions)
    {
        var line = new UiNode("merc-tabs") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        for (int i = 0; i < Tabs.Length; i++)
            line.Children.Add(Button($"merc-tab{i}", context.Tr(Tabs[i]), actions.Tab, i.ToString(), styles.Button, 150, i != s_tab));

        return line;
    }

    UiNode Row(string id, string text, string caption, string action, int index, Styles styles)
    {
        var line = new UiNode(id) { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        line.Children.Add(Label(id + "-t", text, styles.Text, Width - 200));
        line.Children.Add(Button(id + "-b", caption, action, index.ToString(), styles.Button, 180));
        return line;
    }

    static UiNode Label(string id, string text, string style, float width = 0) => new(id, UiNodeKind.Label)
    {
        Placement = width > 0 ? new(width, Line) : new(UiLength.Fill, Line),
        Text = text,
        Style = style,
    };

    static UiNode Button(string id, string text, string action, string argument, string style, float width, bool enabled = true) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line + 4),
            Text = text,
            Action = action,
            Argument = argument,
            Style = style,
            Enabled = enabled,
        };

    #endregion
}
