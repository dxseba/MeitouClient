using Meitou.Game.Ui;

namespace Meitou.CheatMenu;

/// <summary>The window's registered style ids.</summary>
sealed record Styles(string Window, string Text, string Head, string Button, string List);

/// <summary>Small builders for the window's nodes, sized as the original's (a 34-pixel line, buttons in one column).</summary>
static class Kit
{
    public const float Width = 460, Height = 640, Line = 26, Gap = 4;

    public static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = Gap, Pickable = false };

    public static UiNode Label(string id, string text, string style, float width = 0) =>
        new(id, UiNodeKind.Label) { Placement = new(width > 0 ? width : UiLength.Fill, Line), Text = text, Style = style };

    public static UiNode Button(string id, string text, string style, string action, string argument = "", float width = 0, bool enabled = true) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width > 0 ? width : UiLength.Fill, Line),
            Text = text,
            Style = style,
            Action = enabled ? action : "",
            Argument = argument,
            Enabled = enabled,
        };

    public static UiNode TextBox(string id, string text, string action, float width, int maxLength) =>
        new(id, UiNodeKind.TextBox) { Placement = new(width > 0 ? width : UiLength.Fill, Line), Text = text, Action = action, MaxLength = maxLength };

    public static UiNode Scroll(string id, float height, string style) => new(id, UiNodeKind.ScrollView)
    {
        Placement = new(UiLength.Fill, height),
        Style = style,
        Flow = UiFlow.Column,
        Padding = 2,
        Gap = 2,
    };
}
