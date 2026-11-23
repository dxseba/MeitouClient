using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.SpeedMenu;

/// <summary>
/// The toggle button (the original's yellow diamond when its picture is found, else a text button) beside the HUD's time controls and the
/// SELECTED CHARACTER SPEEDS panel above them (<c>UiRegistration.AddScreenControls(UiScreens.Hud, "TimeMoneyPanel", Left, ...)</c>): a scroll view
/// of a row per character with its name and speeds.
/// </summary>
public sealed class SpeedMenuUi(string? diamond) : IUiExtension
{
    /// <summary>
    /// The time controls' widget the original places against (<c>MainBarGUI::update</c>; here the HUD's time and money panel).
    /// </summary>
    const string Anchor = "TimeMoneyPanel";

    /// <summary>
    /// The original's button: 30 pixels square, 39 left of and 52 below the anchor's top-left corner.
    /// </summary>
    const float ToggleSize = 30, ToggleLeft = 39, ToggleDown = 52;

    /// <summary>
    /// The original's panel: 330 by 450, 365 left of and 454 above the anchor's top-left corner, kept at or right of and below the screen's
    /// top-left corner.
    /// </summary>
    const float PanelWidth = 330, PanelHeight = 450, PanelLeft = 365, PanelUp = 454;

    /// <summary>
    /// A row's height and the height above the rows (title and padding).
    /// </summary>
    const float RowHeight = 38, HeadHeight = 30;

    /// <summary>
    /// Whether the panel is open.
    /// </summary>
    bool _open;

    public string Id => "dxseba.speedmenu.ui";

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxSkin", Text = Ink }),
            registration.RegisterStyle("text", new() { Text = Ink, TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("small", new() { Text = Ink, TextPadding = new(6, 0, 6, 1), TextScale = .85f }),
            registration.RegisterStyle("button", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" }));
        string toggle = registration.RegisterAction("toggle", _ => _open = !_open);
        registration.AddScreenControls(UiScreens.Hud, Anchor, UiAnchorPlacement.Left, context => Build(context, styles, toggle));
    }

    /// <summary>
    /// The original's text colour.
    /// </summary>
    static Vector4 Ink => new(.82f, .79f, .70f, 1);

    /// <summary>
    /// The registered style keys.
    /// </summary>
    sealed record Styles(string Panel, string Text, string Small, string Button);

    /// <summary>
    /// The button, and the panel while open; nothing before the first sorted frame of a world. The node is laid out in the rectangle left of the
    /// anchor, so the anchor's left edge is the parent's right edge.
    /// </summary>
    UiNode? Build(UiScreenContext context, Styles styles, string toggle)
    {
        if (SpeedRows.Rows is not { } rows) return null;

        var root = new UiNode("speedmenu") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(Toggle(styles, toggle));
        if (_open) root.Children.Add(Panel(context.Anchor, styles, rows));
        return root;
    }

    /// <summary>
    /// The toggle: the original's diamond picture as a button, or a text button without it.
    /// </summary>
    UiNode Toggle(Styles styles, string toggle)
    {
        if (diamond is null)
        {
            return new("speedmenu-toggle", UiNodeKind.Button)
            {
                Placement = new(70, ToggleSize, Top: ToggleDown, Right: ToggleLeft - 70), Text = "Speeds", Action = toggle, Style = styles.Button,
            };
        }

        var button = new UiNode("speedmenu-toggle", UiNodeKind.Button)
        {
            Placement = new(ToggleSize, ToggleSize, Top: ToggleDown, Right: ToggleLeft - ToggleSize), Action = toggle,
        };
        button.Children.Add(new("speedmenu-diamond", UiNodeKind.Image) { Placement = UiPlacement.Fill, Image = diamond });
        return button;
    }

    /// <summary>
    /// The panel: the title, then a scroll view of the rows (the original's <c>Kenshi_ScrollViewEmpty</c>).
    /// </summary>
    static UiNode Panel(UiRect anchor, Styles styles, IReadOnlyList<SpeedRow> rows)
    {
        float origin = anchor.X - anchor.Width;
        float left = Math.Max(anchor.X - PanelLeft, 0) - origin, top = Math.Max(anchor.Y - PanelUp, 0) - anchor.Y;
        var body = new UiNode("speedmenu-panel")
        {
            Placement = new(PanelWidth, PanelHeight, Left: left, Top: top), Style = styles.Panel, Flow = UiFlow.Column, Padding = 4, Gap = 0,
        };
        body.Children.Add(Label("speedmenu-head", "SELECTED CHARACTER SPEEDS", 24, styles.Text));

        var list = new UiNode("speedmenu-list", UiNodeKind.ScrollView)
        {
            Placement = new(UiLength.Fill, PanelHeight - HeadHeight - 8), Flow = UiFlow.Column, ScrollStep = RowHeight,
        };
        if (rows.Count == 0) list.Children.Add(Label("speedmenu-none", "Select characters to see their speeds.", 22, styles.Text));
        for (int i = 0; i < rows.Count; i++) list.Children.Add(Row(styles, rows[i], i));
        body.Children.Add(list);
        return body;
    }

    /// <summary>
    /// One character: its name and "current mph (max max)", then its current speed.
    /// </summary>
    static UiNode Row(Styles styles, SpeedRow row, int i)
    {
        var node = new UiNode($"speedmenu-row-{i}") { Placement = new(UiLength.Fill, RowHeight), Flow = UiFlow.Column, Pickable = false };
        var line = new UiNode($"speedmenu-line-{i}") { Placement = new(UiLength.Fill, 20), Flow = UiFlow.Row, Pickable = false };
        line.Children.Add(new($"speedmenu-name-{i}", UiNodeKind.Label) { Placement = new(130, 20), Text = row.Name, Style = styles.Text });
        line.Children.Add(new($"speedmenu-speed-{i}", UiNodeKind.Label)
        {
            Placement = new(UiLength.Fill, 20), Text = row.Speeds, Style = styles.Text,
        });
        node.Children.Add(line);
        node.Children.Add(Label($"speedmenu-current-{i}", row.Current, 16, styles.Small));
        return node;
    }

    /// <summary>
    /// A full-width label.
    /// </summary>
    static UiNode Label(string id, string text, float height, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, height), Text = text, Style = style };
}
