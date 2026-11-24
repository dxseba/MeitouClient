using Meitou.Game.Ui;

namespace Meitou.JobManagement;

/// <summary>The window's registered style ids.</summary>
sealed record Styles(string Panel, string Text, string Dim, string Warn, string Button, string Selected);

/// <summary>
/// Small builders for the window's nodes: rows of fixed-width labels and buttons. Sizes are given in the window's own units and drawn times
/// <see cref="Scale"/> (the fork's <c>UiScale</c>), the text too.
/// </summary>
static class UiKit
{
    public const float LineHeight = 22, RowHeight = 26, Gap = 4;

    /// <summary>The window's size factor (<see cref="JobSettings.UiScale"/> / 100), taken when the window opens; host thread only.</summary>
    public static float Scale { get; set; } = 1;

    /// <summary>A size in the window's units, in pixels.</summary>
    public static float S(float units) => units * Scale;

    static UiLength S(UiLength length) => length with { Pixels = S(length.Pixels) };

    /// <summary>A transparent row that places its children side by side (<paramref name="pickable"/>: it is a drop target for the pointer).</summary>
    public static UiNode Row(string id, float height = RowHeight, bool pickable = false) =>
        new(id) { Placement = new(UiLength.Fill, S(height)), Flow = UiFlow.Row, Gap = S(Gap), Pickable = pickable };

    /// <summary>A transparent column of <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static UiNode Column(string id, UiLength width, UiLength height, float gap = 2) =>
        new(id) { Placement = new(S(width), S(height)), Flow = UiFlow.Column, Gap = S(gap), Pickable = false };

    public static UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(S(width), S(LineHeight)), Text = text, Style = style, TextScale = Scale };

    /// <summary>A picture of <paramref name="size"/> units square from <paramref name="path"/>.</summary>
    public static UiNode Icon(string id, string path, float size = LineHeight) =>
        new(id, UiNodeKind.Image) { Placement = new(S(size), S(size)), Image = path, Pickable = false };

    /// <summary>A character's live portrait (as the squad bar draws it), <paramref name="size"/> units square; a click runs the action.</summary>
    public static UiNode Portrait(string id, Meitou.Simulation.CharacterId who, string action, string argument, float size = LineHeight) =>
        new(id, UiNodeKind.Image) { Placement = new(S(size), S(size)), PortraitOf = who, Action = action, Argument = argument };

    public static UiNode Button(string id, string text, float width, string style, string action, string argument, bool enabled = true,
        UiPointerEvents pointer = UiPointerEvents.None) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(S(width), S(LineHeight)),
            Text = text,
            TextScale = Scale,
            Style = style,
            Action = enabled ? action : "",
            Argument = argument,
            Enabled = enabled,
            Pointer = enabled ? pointer : UiPointerEvents.None,
        };

    /// <summary>A list's column: transparent but taking the pointer, so the wheel over it or its gaps runs <paramref name="wheel"/>.</summary>
    public static UiNode List(string id, UiLength width, UiLength height, string wheel, string argument) =>
        new(id)
        {
            Placement = new(S(width), S(height)), Flow = UiFlow.Column, Gap = S(2), Pointer = UiPointerEvents.Wheel, Action = wheel,
            Argument = argument,
        };

    /// <summary>The lines from <paramref name="scroll"/> that fit, the scroll clamped to the list.</summary>
    public static List<UiNode> Page(List<UiNode> lines, ref int scroll, int visible)
    {
        scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - visible));
        return [.. lines.Skip(scroll).Take(visible)];
    }

    /// <summary>How many list lines fit in <paramref name="height"/> pixels.</summary>
    public static int Fit(float height) => Math.Max(1, (int)(height / (LineHeight + 2)));
}
