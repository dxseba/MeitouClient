using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.BuildModeGizmo;

/// <summary>
/// The window's and the arrows' nodes, built each frame.
/// </summary>
sealed partial class GizmoWindow
{
    const float Width = 330, Line = 22, Handle = 22;

    /// <summary>
    /// Radians the rotation handle turns per pixel dragged (engine choice).
    /// </summary>
    const float TurnPerPixel = 0.01f;

    string _x = "", _y = "", _z = "", _yawText = "";

    readonly record struct Styles(string Panel, string Text, string Button, string ArrowX, string ArrowY, string ArrowZ, string Turn);

    readonly record struct Actions(string Field, string Step, string Apply, string Reset, string Mode, string Close, string Drag);

    static Styles RegisterStyles(UiRegistration r)
    {
        UiStyle Arrow(Vector4 c) => new() { Background = c, Hover = c * 1.25f, Active = c * 1.4f, TextAlign = "Center" };
        return new(
            r.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .94f), Text = new(.91f, .86f, .74f, 1) }),
            r.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) }),
            r.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f),
                Hover = new(.32f, .22f, .16f, 1),
                Active = new(.40f, .28f, .18f, 1),
                TextPadding = new(4, 2, 4, 2),
                TextAlign = "Center",
            }),
            r.RegisterStyle("arrow-x", Arrow(new(.85f, .2f, .2f, .9f))),
            r.RegisterStyle("arrow-y", Arrow(new(.25f, .8f, .25f, .9f))),
            r.RegisterStyle("arrow-z", Arrow(new(.25f, .4f, .9f, .9f))),
            r.RegisterStyle("turn", Arrow(new(.9f, .75f, .2f, .9f))));
    }

    Actions RegisterActions(UiRegistration r) => new(
        r.RegisterAction("field", a => Field(a.Argument, a.Text)),
        r.RegisterAction("step", a => Step(a.Argument)),
        r.RegisterAction("apply", _ => ApplyFields()),
        r.RegisterAction("reset", _ => Reset()),
        r.RegisterAction("mode", _ => _rotate = !_rotate),
        r.RegisterAction("close", _ => Close()),
        r.RegisterAction("drag", Drag));

    UiNode Build(Styles s, Actions a)
    {
        var root = new UiNode("gizmo-root") { Placement = UiPlacement.Fill, Pickable = false, Animated = _site is not null };
        if (_site is null || !settings.Enabled) return root;

        if (_screen is { } screen) AddArrows(root, screen, s, a);
        root.Children.Add(Window(s, a));
        return root;
    }

    UiNode Window(Styles s, Actions a)
    {
        var body = new UiNode("gizmo-window")
        {
            Placement = new(Width, 214, Left: 20, Top: 120),
            Style = s.Panel,
            Flow = UiFlow.Column,
            Padding = 6,
            Gap = 2,
        };

        var title = Row("gizmo-title");
        title.Children.Add(Label("gizmo-name", _name, s.Text, Width - 52));
        title.Children.Add(Button("gizmo-close", "X", a.Close, "", s.Button, 26));
        body.Children.Add(title);
        body.Children.Add(FieldRow("x", "X", _x, s, a));
        body.Children.Add(FieldRow("y", "Y", _y, s, a));
        body.Children.Add(FieldRow("z", "Z", _z, s, a));
        body.Children.Add(FieldRow("yaw", context.Tr("Yaw"), _yawText, s, a));

        var bottom = Row("gizmo-bottom");
        bottom.Children.Add(Button("gizmo-mode", context.Tr(_rotate ? "Move" : "Rotate"), a.Mode, "", s.Button, 96));
        bottom.Children.Add(Button("gizmo-apply", context.Tr("Apply"), a.Apply, "", s.Button, 96));
        bottom.Children.Add(Button("gizmo-reset", context.Tr("Reset"), a.Reset, "", s.Button, 96));
        body.Children.Add(bottom);
        return body;
    }

    /// <summary>
    /// A field with its steps: the value typed, and buttons of -10, -1, +1, +10 (degrees: -15, -1, +1, +15).
    /// </summary>
    UiNode FieldRow(string field, string label, string text, Styles s, Actions a)
    {
        var line = Row("gizmo-row-" + field);
        line.Children.Add(Label("gizmo-label-" + field, label, s.Text, 40));
        line.Children.Add(new UiNode("gizmo-field-" + field, UiNodeKind.TextBox)
        {
            Placement = new(90, Line),
            Text = text,
            Action = a.Field,
            Argument = field,
            MaxLength = 12,
        });

        int big = field == "yaw" ? 15 : 10;
        foreach (int step in new[] { -big, -1, 1, big })
        {
            string caption = step > 0 ? "+" + step : step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            line.Children.Add(Button($"gizmo-step-{field}{step}", caption, a.Step, $"{field}:{step}", s.Button, 40));
        }

        return line;
    }

    /// <summary>
    /// The draggable arrow tips (X red, Z blue, up green when the site does not settle) or, in rotation mode, the turning handle beside the site.
    /// </summary>
    void AddArrows(UiNode root, ScreenGizmo screen, Styles s, Actions a)
    {
        if (_rotate)
        {
            root.Children.Add(Tip("gizmo-turn", "R", screen.Centre + new Vector2(40, 0), s.Turn, a.Drag, "r"));
            return;
        }

        if (screen.X is { } x) root.Children.Add(Tip("gizmo-x", "X", x, s.ArrowX, a.Drag, "x"));
        if (screen.Z is { } z) root.Children.Add(Tip("gizmo-z", "Z", z, s.ArrowZ, a.Drag, "z"));
        if (!settings.SnapToGround && screen.Y is { } y) root.Children.Add(Tip("gizmo-y", "Y", y, s.ArrowY, a.Drag, "y"));
    }

    static UiNode Tip(string id, string text, Vector2 at, string style, string action, string axis) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(Handle, Handle, Left: at.X - Handle / 2, Top: at.Y - Handle / 2),
            Text = text,
            Style = style,
            Action = action,
            Argument = axis,
            Pointer = UiPointerEvents.Drag | UiPointerEvents.Release,
        };

    /// <summary>
    /// A drag on an arrow moves the shown place along it (or turns it); the release sends the move.
    /// </summary>
    void Drag(UiActionContext a)
    {
        if (a.Kind == UiEventKind.Release)
        {
            Apply();
            return;
        }

        if (a.Kind != UiEventKind.Drag || _screen is not { } screen) return;

        switch (a.Argument)
        {
            case "x": _shown.X += screen.Along(screen.X, a.Delta); break;
            case "y": _shown.Y += screen.Along(screen.Y, a.Delta); break;
            case "z": _shown.Z += screen.Along(screen.Z, a.Delta); break;
            case "r": _yaw -= a.Delta.X * TurnPerPixel; break;
        }

        SetFields();
    }

    void Field(string field, string text)
    {
        switch (field)
        {
            case "x": _x = text; break;
            case "y": _y = text; break;
            case "z": _z = text; break;
            case "yaw": _yawText = text; break;
        }
    }

    void Step(string argument)
    {
        var parts = argument.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int step)) return;

        switch (parts[0])
        {
            case "x": _shown.X += step; break;
            case "y": _shown.Y += step; break;
            case "z": _shown.Z += step; break;
            case "yaw": _yaw += step * MathF.PI / 180; break;
        }

        Apply();
    }

    /// <summary>
    /// Apply: the typed values (a field that is not a number keeps the shown value).
    /// </summary>
    void ApplyFields()
    {
        _shown = new Vector3(Parse(_x) ?? _shown.X, Parse(_y) ?? _shown.Y, Parse(_z) ?? _shown.Z);
        if (Parse(_yawText) is { } degrees) _yaw = degrees * MathF.PI / 180;

        Apply();
    }

    void SetFields() => (_x, _y, _z, _yawText) = (Number(_shown.X), Number(_shown.Y), Number(_shown.Z), Number(_yaw * 180 / MathF.PI));

    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 3 };

    static UiNode Label(string id, string text, string style, float width) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    static UiNode Button(string id, string text, string action, string argument, string style, float width) =>
        new(id, UiNodeKind.Button) { Placement = new(width, Line + 4), Text = text, Action = action, Argument = argument, Style = style };
}
