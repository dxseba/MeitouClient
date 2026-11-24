using System.Globalization;
using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// The mod's interface on the overview's Map tab, through the public hooks: the markers, hover label and editor as screen controls on the map
/// image (<see cref="UiRegistration.AddScreenControls"/>, anchor <c>MapImage</c>), the footer's Markers: On/Off button beside the zoom buttons,
/// and the map's presses and keys (<see cref="UiRegistration.AddMapInput"/>).
/// </summary>
sealed class MapUi : IUiExtension
{
    public string Id => "dxseba.mapmarkers";

    public void Register(UiRegistration registration)
    {
        var styles = new MapStyles(registration);
        var actions = Actions(registration);
        registration.AddScreenControls(MapOverlay.Screen, "MapImage", UiAnchorPlacement.Inside, c => MapOverlay.Build(c, styles, actions));
        registration.AddScreenControls(MapOverlay.Screen, "MapZoomInButton", UiAnchorPlacement.Left, c => MapOverlay.Toggle(c, styles, actions));
        registration.AddMapInput(Input);
    }

    static MapActions Actions(UiRegistration r) => new(
        r.RegisterAction("toggle", _ => MapState.Toggle()),
        r.RegisterAction("type", _ => MapState.CycleType()),
        r.RegisterAction("delete", _ => MapState.DeleteSelected()),
        r.RegisterAction("close", _ => MapState.Deselect()),
        r.RegisterAction("swatch", c => MapState.Colour(int.Parse(c.Argument, NumberStyles.None, CultureInfo.InvariantCulture))),
        r.RegisterAction("label", Label),
        r.RegisterAction("drag", c => MapState.DragEditor(c.Kind, c.Delta)));

    /// <summary>The label box: each change kept at once; Enter commits it and closes the editor, as the original.</summary>
    static void Label(UiActionContext c)
    {
        MapState.Typed(c.Text);
        if (c.Kind == UiEventKind.Submit) MapState.Deselect();
    }

    /// <summary>
    /// The map's presses (the game still pans or orders as without the mod, as the original only watches the buttons), Delete with no box being
    /// typed in (taken when it removes a marker), and the map closing.
    /// </summary>
    static bool Input(UiMapInput input)
    {
        if (input.Kind == UiMapInputKind.Closed)
        {
            MapState.MapClosed();
            return false;
        }

        if (!MapMarkersMod.Settings.Enabled || input.View is not { } view) return false;

        if (input.Kind == UiMapInputKind.Press) MapState.Press(input.Button, input.Point, MapLayout.Of(view));

        return input.Kind == UiMapInputKind.Key && input.Key == Key.Delete && !input.Typing && MapState.DeleteKey();
    }
}

/// <summary>The registered actions' keys.</summary>
sealed record MapActions(string Toggle, string Type, string Delete, string Close, string Swatch, string Label, string Drag);

/// <summary>
/// The registered looks: the markers, the buttons, the editor's panel and texts, the swatches, and the hover label's
/// strip, registered as the settings' colour first asks for one (the settings may change while the game runs).
/// </summary>
sealed class MapStyles
{
    public const string Font = "Kenshi_StandardFont_Medium";

    const string ButtonSkin = "Kenshi_Button1Skin", PanelSkin = "Kenshi_GenericTextBoxFlatSkin";

    public static readonly Vector4 Ink = new(.86f, .82f, .71f, 1);
    static readonly Vector4 HoverInk = new(.32f, .22f, .16f, 1), PanelBack = new(.10f, .09f, .08f, .94f);
    static readonly Vector4 SwatchFrame = new(.05f, .05f, .05f, 1), BoxBack = new(.04f, .04f, .04f, .9f);

    readonly UiRegistration _registration;
    readonly Dictionary<Vector4, string> _strips = [];
    readonly string[] _swatches = new string[MarkerKinds.Swatches.Length];

    public string Button { get; }
    public string Panel { get; }
    public string Text { get; }
    public string Hint { get; }
    public string Frame { get; }
    public string Box { get; }

    /// <summary>
    /// A marker: the original's <c>Kenshi_Button1</c> with its glyph, tinted with the kind's colour on the node (<see cref="UiNode.Tint"/>).
    /// </summary>
    public string Marker { get; }

    public MapStyles(UiRegistration r)
    {
        _registration = r;
        Button = r.RegisterStyle("button", new() { Skin = ButtonSkin, Text = Ink, Hover = HoverInk, TextAlign = "Center", FontName = Font });
        Marker = r.RegisterStyle("marker", new() { Skin = ButtonSkin, Text = Ink, TextAlign = "Center", FontName = Font });
        Panel = r.RegisterStyle("panel", new() { Skin = PanelSkin, Background = PanelBack });
        Text = r.RegisterStyle("text", new() { Text = Ink, FontName = Font });
        Hint = r.RegisterStyle("hint", new() { Text = Ink * new Vector4(1, 1, 1, .8f), FontName = Font, TextAlign = "Left Top" });
        Frame = r.RegisterStyle("swatch-frame", new() { Background = SwatchFrame });
        Box = r.RegisterStyle("label-box", new() { Background = BoxBack, Text = Ink, FontName = Font, Skin = "Kenshi_EditBox" });
        for (int i = 1; i < _swatches.Length; i++)
            _swatches[i] = r.RegisterStyle($"swatch-{i}", new() { Background = MapOverlay.Rgba(MarkerKinds.SwatchRgba(i)) });
    }

    public string Swatch(int i) => _swatches[i];

    /// <summary>The hover label's strip in <paramref name="back"/>, registered the first time it is asked for.</summary>
    public string Strip(Vector4 back)
    {
        if (_strips.TryGetValue(back, out string? key)) return key;

        key = _registration.RegisterStyle($"hover-{_strips.Count}", new() { Background = back });
        _strips[back] = key;
        return key;
    }
}
