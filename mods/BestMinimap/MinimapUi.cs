using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.BestMinimap;

/// <summary>
/// The minimap on the interface's overlay (HUD layer, under the windows), rebuilt each time the overlay is laid out (at least 20 times a second,
/// every frame with <c>UpdateFps</c> above 20): the map picture's crop, the places, the camera's reach, the other mods' marks, the characters, the
/// alerts, the camera arrow, the frame, the information strip and the tooltip. Hidden while the overview (the big map) is open or the interface is
/// hidden, as in the original.
/// </summary>
sealed partial class MinimapUi(MapPicture picture, OtherMods others, Func<string, string> tr) : IUiExtension
{
    const float StripHeight = 3;

    string _pointer = "", _panel = "", _label = "", _tip = "";
    int _serial;

    public string Id => "dxseba.bestminimap.ui";

    public void Register(UiRegistration registration)
    {
        _pointer = registration.RegisterAction("pointer", MinimapInput.Pointer);
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_FloatingPanelSkin" });
        _label = registration.RegisterStyle("label", new() { TextAlign = "HCenter Top", Text = Vector4.One });
        _tip = registration.RegisterStyle("tip", new()
        {
            Background = new(.08f, .07f, .06f, .9f), Text = new(.92f, .9f, .82f, 1), TextAlign = "Left Top", TextPadding = new UiInsets(6, 4, 6, 4),
        });
        registration.Contribute(UiSlots.Overlay, Build, order: 40);
    }

    UiNode Build(UiFrameContext frame)
    {
        _serial = 0;
        if (!Minimap.Live || !Minimap.Shown) return new UiNode("bm-hidden") { Visible = false };

        var v = Minimap.View;
        var root = new UiNode("bm") { Placement = UiPlacement.Fill, Pickable = false, Animated = Minimap.Settings.UpdateFps > 20 };
        AddGamePanel(root, v);
        var body = new UiNode("bm-body")
        {
            Placement = At(v.Rect), Pickable = true, Action = _pointer,
            Pointer = UiPointerEvents.Release | UiPointerEvents.Drag | UiPointerEvents.Wheel | UiPointerEvents.Buttons,
        };
        root.Children.Add(body);
        AddMap(body, v);
        AddContent(body, v, frame);
        AddFrameLine(root, v);
        AddInfo(root, v, frame);
        AddTooltip(root, v, frame);
        return root;
    }

    /// <summary>Everything drawn over the map picture, in the original's order.</summary>
    void AddContent(UiNode body, Projection v, UiFrameContext frame)
    {
        var s = Minimap.Settings;
        var origin = new Vector2(v.Rect.X, v.Rect.Y);
        if (s.ShowTowns) AddPlaces(body, v, origin, frame);
        if (s.ShowCameraRange) AddCameraReach(body, v, origin);
        if (s.MapMarkers) AddMarks(body, v, origin, others.MapMarkers(), frame);
        if (s.WantedMarkers) AddMarks(body, v, origin, others.Wanted(), frame);
        AddMoveFlag(body, v, origin);
        AddCharacters(body, v, origin, frame);
        if (s.AttackAlert) AddAlerts(body, v, origin);
        if (s.ShowView) AddArrow(body, v, origin);
    }

    /// <summary>A placement at a screen rectangle (the overlay's root fills the screen).</summary>
    static UiPlacement At(UiRect r) => new(r.Width, r.Height, Left: r.X, Top: r.Y);

    /// <summary>A placement at a rectangle relative to <paramref name="origin"/> (a child of a node there).</summary>
    static UiPlacement Within(UiRect r, Vector2 origin) => new(r.Width, r.Height, Left: r.X - origin.X, Top: r.Y - origin.Y);

    /// <summary>A white sprite tinted <paramref name="colour"/>, centred on a screen point.</summary>
    UiNode Sprite(string image, Vector2 centre, Vector2 size, Vector4 colour, Vector2 origin, float angle = 0)
    {
        var node = new UiNode($"bm-s{_serial++}", UiNodeKind.Image)
        {
            Image = image, Tint = colour, Pickable = false,
            Placement = Within(new(centre.X - size.X / 2, centre.Y - size.Y / 2, size.X, size.Y), origin),
        };
        return ImagePatch.Turned(node, angle);
    }

    /// <summary>A caption centred under (or over) a screen point.</summary>
    UiNode Label(string text, Vector2 topCentre, Vector4 colour, float scale, Vector2 origin)
    {
        float width = 260, height = 24 * scale;
        return new UiNode($"bm-l{_serial++}", UiNodeKind.Label)
        {
            Text = text, Style = _label, TextColour = colour, TextScale = scale, Pickable = false,
            Placement = Within(new(topCentre.X - width / 2, topCentre.Y, width, height), origin),
        };
    }

    /// <summary>The labels' text scale: the interface's, a little smaller (port choice).</summary>
    static float LabelScale(UiFrameContext frame) => 0.8f;
}
