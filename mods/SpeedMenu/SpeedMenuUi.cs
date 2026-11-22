using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.SpeedMenu;

/// <summary>
/// The toggle button (the original's yellow diamond when its picture is found, else a text button) and the SELECTED CHARACTER SPEEDS panel
/// under the toolbar's right side: a row per character with its name and speeds.
/// </summary>
public sealed class SpeedMenuUi(string? diamond) : IUiExtension
{
    /// <summary>
    /// The panel's width, a row's height and the height above the rows (title and padding).
    /// </summary>
    const float PanelWidth = 300, RowHeight = 38, HeadHeight = 30;

    /// <summary>
    /// The panel's left and top edges, and the toggle's.
    /// </summary>
    const float Left = 340, Top = 72, ToggleTop = 44;

    /// <summary>
    /// Whether the panel is open.
    /// </summary>
    bool _open;

    public string Id => "dxseba.speedmenu.ui";

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .94f), Text = Ink }),
            registration.RegisterStyle("text", new() { Text = Ink, TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("small", new() { Text = Ink, TextPadding = new(6, 0, 6, 1), TextScale = .85f }),
            registration.RegisterStyle("button", new() { Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1),
                TextPadding = new(6, 2, 6, 2) }));
        string toggle = registration.RegisterAction("toggle", _ => _open = !_open);
        registration.Contribute(UiSlots.Overlay, frame => Build(frame, styles, toggle), order: 50);
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
    /// The button, and the panel while open; nothing until the HUD adapter has bound a count.
    /// </summary>
    UiNode Build(UiFrameContext frame, Styles styles, string toggle)
    {
        if (!int.TryParse(frame.Text(HudAdapter.CountKey), CultureInfo.InvariantCulture, out int count))
            return new("speedmenu-hidden") { Visible = false };

        var root = new UiNode("speedmenu") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(Toggle(styles, toggle));
        if (_open) root.Children.Add(Panel(frame, styles, count));
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
                Placement = new(70, 26, Left: Left, Top: ToggleTop), Text = "Speeds", Action = toggle, Style = styles.Button,
            };
        }

        var button = new UiNode("speedmenu-toggle", UiNodeKind.Button) { Placement = new(26, 26, Left: Left, Top: ToggleTop), Action = toggle };
        button.Children.Add(new("speedmenu-diamond", UiNodeKind.Image) { Placement = UiPlacement.Fill, Image = diamond });
        return button;
    }

    /// <summary>
    /// The panel: the title, then the rows that fit the screen (the original scrolls; a last line counts the rest).
    /// </summary>
    static UiNode Panel(UiFrameContext frame, Styles styles, int count)
    {
        int fits = Math.Max(1, (int)((frame.Viewport.Height - Top - HeadHeight - 40) / RowHeight));
        int shown = Math.Min(count, fits);
        float height = HeadHeight + 8 + RowHeight * Math.Max(1, shown) + (count > shown ? 22 : 0);
        var body = new UiNode("speedmenu-list")
        {
            Placement = new(PanelWidth, height, Left: Left, Top: Top), Style = styles.Panel, Flow = UiFlow.Column, Padding = 4, Gap = 0,
        };
        body.Children.Add(Label("speedmenu-head", "SELECTED CHARACTER SPEEDS", 24, styles.Text));
        if (count == 0) body.Children.Add(Label("speedmenu-none", "Select characters to see their speeds.", 22, styles.Text));
        for (int i = 0; i < shown; i++) body.Children.Add(Row(frame, styles, i));
        if (count > shown) body.Children.Add(Label("speedmenu-more", $"+{count - shown}", 20, styles.Small));
        return body;
    }

    /// <summary>
    /// One character: its name and "current mph (max max)", then its current speed.
    /// </summary>
    static UiNode Row(UiFrameContext frame, Styles styles, int i)
    {
        string index = i.ToString(CultureInfo.InvariantCulture);
        var row = new UiNode($"speedmenu-row-{index}") { Placement = new(UiLength.Fill, RowHeight), Flow = UiFlow.Column, Pickable = false };
        var line = new UiNode($"speedmenu-line-{index}") { Placement = new(UiLength.Fill, 20), Flow = UiFlow.Row, Pickable = false };
        line.Children.Add(new($"speedmenu-name-{index}", UiNodeKind.Label)
        {
            Placement = new(130, 20), Text = frame.Text(HudAdapter.NamePrefix + index), Style = styles.Text,
        });
        line.Children.Add(new($"speedmenu-speed-{index}", UiNodeKind.Label)
        {
            Placement = new(UiLength.Fill, 20), Text = frame.Text(HudAdapter.SpeedPrefix + index), Style = styles.Text,
        });
        row.Children.Add(line);
        row.Children.Add(Label($"speedmenu-current-{index}", frame.Text(HudAdapter.CurrentPrefix + index), 16, styles.Small));
        return row;
    }

    /// <summary>
    /// A full-width label.
    /// </summary>
    static UiNode Label(string id, string text, float height, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, height), Text = text, Style = style };
}
