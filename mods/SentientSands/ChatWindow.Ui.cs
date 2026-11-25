using Meitou.Game.Ui;

namespace Meitou.SentientSands;

/// <summary>The chat window's layout: the lines, the text box, the buttons, the barter table and the offer popup.</summary>
sealed partial class ChatWindow
{
    const float Width = 620, Line = 20, Shown = 12, Goods = 8;

    readonly record struct Styles(string Panel, string Text, string Button, string Box);

    readonly record struct Actions(string Type, string Send, string Barter, string Trade, string Close, string Give, string Take, string Clear,
        string GiveCats, string TakeCats, string Accept, string Decline);

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.08f, .08f, .07f, .95f), Text = new(.92f, .88f, .78f, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.92f, .88f, .78f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("button", new() { Background = new(.2f, .16f, .12f, .95f), Hover = new(.32f, .24f, .17f, 1),
                TextPadding = new(6, 2, 6, 2) }),
            registration.RegisterStyle("box", new() { Text = new(.95f, .95f, .9f, 1), TextPadding = new(6, 2, 6, 2) }));
        var actions = new Actions(
            registration.RegisterAction("type", Type),
            registration.RegisterAction("send", _ => Send()),
            registration.RegisterAction("barter", _ => ToggleBarter()),
            registration.RegisterAction("trade", _ => context.Enqueue(new ChatTalk(_npc, _player, Hold: true, Trade: true) { Tick = context.Tick })),
            registration.RegisterAction("close", _ => Close()),
            registration.RegisterAction("give", a => Put(true, a.Argument, "npc")),
            registration.RegisterAction("take", a => Put(false, a.Argument, ServerMessage.Key(_player))),
            registration.RegisterAction("clear", _ => ClearDeal()),
            registration.RegisterAction("givecats", a => _giveCats = SentientSystem.Cats(a.Text)),
            registration.RegisterAction("takecats", a => _takeCats = SentientSystem.Cats(a.Text)),
            registration.RegisterAction("accept", _ => Answer(true)),
            registration.RegisterAction("decline", _ => Answer(false)));
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 61);
    }

    void Type(UiActionContext ui)
    {
        if (ui.Kind == UiEventKind.Submit) Send();
        else _text = ui.Text;
    }

    UiNode Build(Styles styles, Actions actions)
    {
        var root = new UiNode("ss-chat-root") { Placement = UiPlacement.Fill, Pickable = false };
        if (_open) root.Children.Add(Chat(styles, actions));
        if (_offer is { } offer) root.Children.Add(Popup(offer, styles, actions));

        return root;
    }

    UiNode Chat(Styles styles, Actions actions)
    {
        float height = 16 + (Shown + 3) * (Line + 2) + (_barter ? (Goods + 3) * (Line + 2) : 0);
        var body = new UiNode("ss-chat") { Placement = new(Width, height, Left: 160, Top: 120), Style = styles.Panel, Flow = UiFlow.Column,
            Padding = 4, Gap = 2 };
        body.Children.Add(Label("ss-title", $"{context.Tr("Talking to")} {_npcName}", styles.Text));
        for (int i = Math.Max(0, _log.Count - (int)Shown); i < _log.Count; i++) body.Children.Add(Label($"ss-l{i}", _log[i], styles.Text));

        body.Children.Add(new UiNode("ss-box", UiNodeKind.TextBox)
        {
            Placement = new(UiLength.Fill, Line + 4), Text = _text, Action = actions.Type, MaxLength = 400, Focus = true, Style = styles.Box,
        });
        body.Children.Add(Row("ss-buttons", [("Send", actions.Send), (_barter ? "Close table" : "Barter", actions.Barter), ("Trade", actions.Trade),
            ("Close", actions.Close)], styles));
        if (_barter) body.Children.Add(Table(styles, actions));

        return body;
    }

    UiNode Table(Styles styles, Actions actions)
    {
        var facts = SentientSystem.Facts;
        var table = new UiNode("ss-table") { Placement = new(UiLength.Fill, (Goods + 3) * (Line + 2)), Flow = UiFlow.Column, Gap = 2 };
        var columns = new UiNode("ss-cols") { Placement = new(UiLength.Fill, Goods * (Line + 2)), Flow = UiFlow.Row, Gap = 6 };
        columns.Children.Add(GoodsColumn("ss-npc", facts.Of(_npc)?.Inventory ?? [], actions.Give, styles));
        columns.Children.Add(GoodsColumn("ss-mine", facts.Of(_player)?.Inventory ?? [], actions.Take, styles));
        table.Children.Add(columns);

        string get = string.Join(", ", _deal.Where(d => d.Give).Select(d => $"{d.Count} {d.Name}"));
        string give = string.Join(", ", _deal.Where(d => !d.Give).Select(d => $"{d.Count} {d.Name}"));
        table.Children.Add(Label("ss-deal", $"{context.Tr("You get")}: {get} {_giveCats} c.  {context.Tr("You give")}: {give} {_takeCats} c.",
            styles.Text));

        var cats = new UiNode("ss-cats") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        cats.Children.Add(Box("ss-givecats", _giveCats.ToString(), actions.GiveCats, styles.Box, 160));
        cats.Children.Add(Box("ss-takecats", _takeCats.ToString(), actions.TakeCats, styles.Box, 160));
        cats.Children.Add(Button("ss-clear", context.Tr("Clear"), actions.Clear, "", styles.Button, 120));
        table.Children.Add(cats);
        return table;
    }

    UiNode GoodsColumn(string id, IReadOnlyList<CarriedItem> items, string action, Styles styles)
    {
        var column = new UiNode(id) { Placement = new((Width - 20) / 2, UiLength.Fill), Flow = UiFlow.Column, Gap = 1 };
        foreach (var (item, i) in items.Take((int)Goods).Select((x, i) => (x, i)))
            column.Children.Add(Button($"{id}{i}", $"{item.Name} x{item.Count} ({item.Price} c.)", action, item.Name, styles.Button,
                (Width - 20) / 2));

        return column;
    }

    UiNode Popup(OfferView offer, Styles styles, Actions actions)
    {
        int lines = 4 + offer.PlayerBox.Count + offer.NpcBox.Count;
        var popup = new UiNode("ss-offer")
        {
            Placement = new(480, 16 + lines * (Line + 2), Left: 300, Top: 220), Style = styles.Panel, Flow = UiFlow.Column, Padding = 6, Gap = 2,
        };
        popup.Children.Add(Label("ss-offer-npc", offer.Npc, styles.Text));
        popup.Children.Add(Label("ss-offer-text", offer.Text, styles.Text));
        foreach (var (b, i) in offer.PlayerBox.Select((b, i) => (b, i))) popup.Children.Add(Label($"ss-offer-p{i}",
            $"{context.Tr("You give")}: {b}", styles.Text));
        foreach (var (b, i) in offer.NpcBox.Select((b, i) => (b, i))) popup.Children.Add(Label($"ss-offer-n{i}", $"{context.Tr("You get")}: {b}",
            styles.Text));

        popup.Children.Add(Row("ss-offer-buttons", [("Accept", actions.Accept), ("Decline", actions.Decline)], styles));
        return popup;
    }

    UiNode Row(string id, IEnumerable<(string Caption, string Action)> buttons, Styles styles)
    {
        var row = new UiNode(id) { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        foreach (var ((caption, action), i) in buttons.Select((b, i) => (b, i))) row.Children.Add(Button($"{id}{i}", context.Tr(caption), action,
            "", styles.Button, 120));

        return row;
    }

    static UiNode Label(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    static UiNode Box(string id, string text, string action, string style, float width) =>
        new(id, UiNodeKind.TextBox) { Placement = new(width, Line + 4), Text = text, Action = action, MaxLength = 12, Style = style };

    static UiNode Button(string id, string text, string action, string argument, string style, float width) =>
        new(id, UiNodeKind.Button) { Placement = new(width, Line + 2), Text = text, Action = action, Argument = argument, Style = style };
}
