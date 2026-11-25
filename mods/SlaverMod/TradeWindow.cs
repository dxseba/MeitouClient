using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SlaverMod;

/// <summary>
/// The original's slave trade window ("BUYING SLAVES - ..." / "SELLING SLAVES - ..."): one row per slave with its sex and price, click to tick,
/// Select all, Clear, the total, your money, BUY or SELL, SHACKLE KIT and CLOSE. Shows the serial step's <see cref="SlaverSystem.Trade"/>.
/// Interface thread only.
/// </summary>
sealed class TradeWindow(ModContext context) : IUiExtension
{
    const float Width = 520, Line = 22, Shown = 14;

    readonly HashSet<CharacterId> _ticked = [];
    long _viewId;
    int _page;

    public string Id => "dxseba.slavermod.trade";

    readonly record struct Styles(string Panel, string Text, string Button, string Ticked);

    readonly record struct Actions(string Tick, string All, string Clear, string Confirm, string Kit, string Close, string Page);

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .08f, .07f, .95f), Text = new(.91f, .86f, .74f, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("button", new() { Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1),
                Disabled = new(.14f, .12f, .10f, .8f), TextPadding = new(6, 2, 6, 2) }),
            registration.RegisterStyle("ticked", new() { Background = new(.38f, .26f, .12f, .95f), Hover = new(.46f, .32f, .16f, 1),
                TextPadding = new(6, 2, 6, 2) }));
        var actions = new Actions(
            registration.RegisterAction("tick", a => Toggle(a.Argument)),
            registration.RegisterAction("all", _ => TickAll(true)),
            registration.RegisterAction("clear", _ => TickAll(false)),
            registration.RegisterAction("confirm", _ => Send(new TradeConfirm([.. _ticked]) { Tick = context.Tick })),
            registration.RegisterAction("kit", _ => Send(new BuyKitOrder { Tick = context.Tick })),
            registration.RegisterAction("close", _ => Send(new CloseTradeWindow { Tick = context.Tick })),
            registration.RegisterAction("page", a => _page += int.Parse(a.Argument)));
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 62);
    }

    void Send(SimCommand command)
    {
        context.Enqueue(command);
        _ticked.Clear();
    }

    void Toggle(string argument)
    {
        if (SlaverSystem.Trade?.Rows.FirstOrDefault(r => Key(r.Character) == argument) is not { } row) return;

        if (!_ticked.Remove(row.Character)) _ticked.Add(row.Character);
    }

    void TickAll(bool on)
    {
        _ticked.Clear();
        if (on && SlaverSystem.Trade is { } view) _ticked.UnionWith(view.Rows.Select(r => r.Character));
    }

    static string Key(CharacterId id) => $"{id.Slot}.{id.Generation}";

    UiNode Build(Styles styles, Actions actions)
    {
        if (SlaverSystem.Trade is not { } view) return new UiNode("sm-trade-hidden") { Visible = false };

        if (view.Id != _viewId) (_viewId, _page) = (view.Id, 0);
        _ticked.IntersectWith(view.Rows.Select(r => r.Character));

        var body = new UiNode("sm-trade")
        {
            Placement = new(Width, 16 + (Shown + 5) * (Line + 1), Left: 160, Top: 100),
            Style = styles.Panel,
            Flow = UiFlow.Column,
            Padding = 4,
            Gap = 1,
        };
        string title = view.Selling ? "SELLING SLAVES - " : "BUYING SLAVES - ";
        body.Children.Add(Label("sm-title", context.Tr(title) + view.SlaverName, styles.Text));
        AddRows(body, view, styles, actions);
        body.Children.Add(Label("sm-total", Total(view), styles.Text));
        body.Children.Add(Buttons(view, styles, actions));

        var root = new UiNode("sm-trade-root") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(body);
        return root;
    }

    void AddRows(UiNode body, TradeView view, Styles styles, Actions actions)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(view.Rows.Count / Shown));
        _page = Math.Clamp(_page, 0, pages - 1);
        if (view.Rows.Count == 0) body.Children.Add(Label("sm-none", context.Tr("Click the slaves to select them."), styles.Text));

        int first = _page * (int)Shown;
        for (int i = first; i < Math.Min(view.Rows.Count, first + (int)Shown); i++)
        {
            var row = view.Rows[i];
            string sex = context.Tr(row.Female ? " (woman)" : " (man)");
            string style = _ticked.Contains(row.Character) ? styles.Ticked : styles.Button;
            string text = $"{row.Name}{sex}   {context.Tr("price: ")}{row.Price}";
            body.Children.Add(Button($"sm-r{i}", text, actions.Tick, Key(row.Character), style, Width - 12));
        }
    }

    string Total(TradeView view)
    {
        var picked = view.Rows.Where(r => _ticked.Contains(r.Character)).ToList();
        string price = context.Tr(view.Selling ? "you get: " : "price: ");
        return $"{context.Tr("Your money: ")}{view.Money}   {picked.Count}{context.Tr(" selected, ")}{price}{picked.Sum(r => r.Price)}";
    }

    UiNode Buttons(TradeView view, Styles styles, Actions actions)
    {
        var line = new UiNode("sm-buttons") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        line.Children.Add(Button("sm-prev", "<", actions.Page, "-1", styles.Button, 30));
        line.Children.Add(Button("sm-next", ">", actions.Page, "1", styles.Button, 30));
        line.Children.Add(Button("sm-all", context.Tr("SELECT ALL"), actions.All, "", styles.Button, 90));
        line.Children.Add(Button("sm-clear", context.Tr("CLEAR"), actions.Clear, "", styles.Button, 60));
        line.Children.Add(Button("sm-go", context.Tr(view.Selling ? "SELL" : "BUY"), actions.Confirm, "", styles.Button, 60));
        line.Children.Add(Button("sm-kit", $"{context.Tr("SHACKLE KIT ")}{view.KitPrice}", actions.Kit, "", styles.Button, 120));
        line.Children.Add(Button("sm-close", context.Tr("CLOSE"), actions.Close, "", styles.Button, 70));
        return line;
    }

    static UiNode Label(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    static UiNode Button(string id, string text, string action, string argument, string style, float width) =>
        new(id, UiNodeKind.Button) { Placement = new(width, Line), Text = text, Action = action, Argument = argument, Style = style };
}
