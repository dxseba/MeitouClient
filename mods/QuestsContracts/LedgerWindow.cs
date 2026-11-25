using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.QuestsContracts;

/// <summary>
/// The Ledger (J): tabs Contracts, Completed and failed, Relations and Log over the serial step's copy (<see cref="LedgerView"/>). The original
/// also has Bounties and Towns tabs and target portraits; the port lists the posters read in the Log. Interface thread only.
/// </summary>
sealed class LedgerWindow(ModContext context) : IUiExtension
{
    const float Width = 620, Line = 20, Lines = 22;
    static readonly string[] Tabs = ["Contracts", "Done and failed", "Bounties", "Relations", "Log"];

    static volatile bool s_open;
    int _tab, _page;

    public string Id => "dxseba.questscontracts.ledger";

    public static void Toggle() => s_open = !s_open;

    readonly record struct Styles(string Panel, string Text, string Button);

    readonly record struct Actions(string Tab, string Page, string Close);

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .95f), Text = new(.91f, .86f, .74f, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f),
                Hover = new(.32f, .22f, .16f, 1),
                Disabled = new(.14f, .12f, .10f, .8f),
                TextPadding = new(6, 2, 6, 2),
            }));
        var actions = new Actions(
            registration.RegisterAction("tab", a => (_tab, _page) = (int.Parse(a.Argument), 0)),
            registration.RegisterAction("page", a => _page += int.Parse(a.Argument)),
            registration.RegisterAction("close", _ => s_open = false));
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 60);
    }

    UiNode Build(Styles styles, Actions actions)
    {
        if (!s_open) return new UiNode("qc-ledger-hidden") { Visible = false };

        var view = LedgerSystem.Running?.View;
        var rows = view is null
            ? []
            : _tab switch { 0 => view.Contracts, 1 => view.History, 2 => view.Bounties, 3 => view.Reputation, _ => view.Events };
        int pages = Math.Max(1, (int)Math.Ceiling(rows.Count / Lines));
        _page = Math.Clamp(_page, 0, pages - 1);

        var body = new UiNode("qc-ledger")
        {
            Placement = new(Width, 16 + (Lines + 4) * (Line + 1), Left: 140, Top: 90),
            Style = styles.Panel,
            Flow = UiFlow.Column,
            Padding = 4,
            Gap = 1,
        };
        body.Children.Add(Label("qc-title", context.Tr("The Ledger") + (view is null ? "" : $" - {context.Tr("day")} {view.Day}"), styles.Text));
        body.Children.Add(TabRow(styles, actions));
        AddRows(body, rows, styles);
        body.Children.Add(Footer(styles, actions, pages));

        var root = new UiNode("qc-ledger-root") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(body);
        return root;
    }

    void AddRows(UiNode body, IReadOnlyList<string> rows, Styles styles)
    {
        int first = _page * (int)Lines;
        if (rows.Count == 0) body.Children.Add(Label("qc-empty", context.Tr("Nothing yet."), styles.Text));

        for (int i = first; i < Math.Min(rows.Count, first + (int)Lines); i++) body.Children.Add(Label($"qc-row{i}", rows[i], styles.Text));
    }

    UiNode TabRow(Styles styles, Actions actions)
    {
        var line = new UiNode("qc-tabs") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        for (int i = 0; i < Tabs.Length; i++)
            line.Children.Add(Button($"qc-tab{i}", context.Tr(Tabs[i]), actions.Tab, i.ToString(), styles.Button, 118, i != _tab));

        return line;
    }

    UiNode Footer(Styles styles, Actions actions, int pages)
    {
        var line = new UiNode("qc-footer") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        line.Children.Add(Button("qc-prev", "<", actions.Page, "-1", styles.Button, 40, _page > 0));
        line.Children.Add(Label("qc-page", $"{_page + 1} / {pages}", styles.Text, 80));
        line.Children.Add(Button("qc-next", ">", actions.Page, "1", styles.Button, 40, _page < pages - 1));
        line.Children.Add(Button("qc-close", context.Tr("Close"), actions.Close, "", styles.Button, 120, true));
        return line;
    }

    static UiNode Label(string id, string text, string style, float width = 0) => new(id, UiNodeKind.Label)
    {
        Placement = width > 0 ? new(width, Line) : new(UiLength.Fill, Line),
        Text = text,
        Style = style,
    };

    static UiNode Button(string id, string text, string action, string argument, string style, float width, bool enabled) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line),
            Text = text,
            Action = action,
            Argument = argument,
            Style = style,
            Enabled = enabled,
        };
}
