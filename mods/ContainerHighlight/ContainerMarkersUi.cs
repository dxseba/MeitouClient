using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.ContainerHighlight;

/// <summary>
/// Draws a marker over each highlighted container (<b>Verified</b>, DLL decompile, its marker widgets): the icon (18 px, in the marker colour) and
/// the marker text (a 45 by 18 box one pixel to its right, in the marker colour at the text size 18, with eight dark copies one pixel round it as
/// its outline), as one row centred on the projected point. Icon and text each show only with their setting on.
/// </summary>
sealed class ContainerMarkersUi(ContainerScan scan, string? icon) : IUiExtension
{
    const float Icon = 18, TextWidth = 45, Row = 18, Gap = 1;

    /// <summary>
    /// The DLL's text size (<c>marker_text_size_px</c>, 18) over the font's pixel height at text scale 1 (<c>Kenshi_StandardFont_Medium</c>, 20).
    /// </summary>
    const float TextScale = 18f / 20f;

    static readonly Vector2[] OutlineSteps = [new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)];

    string _style = "";

    public string Id => "dxseba.containerhighlight.markers";

    public void Register(UiRegistration registration)
    {
        var style = new UiStyle { TextAlign = "Center", FontName = "Kenshi_StandardFont_Medium", TextPadding = new(0, 0, 0, 0) };
        _style = registration.RegisterStyle("marker", style);
        registration.Contribute(UiSlots.Overlay, _ => Build(), order: 40);
    }

    UiNode Build()
    {
        var settings = ContainerHighlightMod.Settings;
        bool showIcon = settings.ShowIcons && icon is not null, showText = settings.ShowText && settings.MarkerText.Length > 0;
        var points = showIcon || showText ? scan.Markers : [];

        // The markers follow the camera, so the overlay is laid out every frame while any shows.
        var root = new UiNode("containerhighlight-markers") { Placement = UiPlacement.Fill, Pickable = false, Animated = points.Count > 0 };
        for (int i = 0; i < points.Count; i++) AddMarker(root, i, points[i], showIcon, showText);

        return root;
    }

    void AddMarker(UiNode root, int i, Vector2 at, bool showIcon, bool showText)
    {
        var colour = ContainerHighlightMod.Settings.Marker;
        float width = (showIcon ? Icon : 0) + (showIcon && showText ? Gap : 0) + (showText ? TextWidth : 0);
        float left = at.X - width / 2, top = at.Y - Row / 2;
        if (showIcon && icon is not null)
        {
            var place = new UiPlacement(Icon, Icon, Left: left, Top: top);
            var node = new UiNode("containerhighlight-icon-" + i, UiNodeKind.Image) { Placement = place, Image = icon, Tint = colour, Pickable = false };
            root.Children.Add(node);
            left += Icon + Gap;
        }

        if (showText) AddText(root, "containerhighlight-text-" + i, new UiRect(left, top, TextWidth, Row), colour);
    }

    void AddText(UiNode root, string id, UiRect box, Vector4 colour)
    {
        string text = ContainerHighlightMod.Settings.MarkerText.Replace("#", "##", StringComparison.Ordinal);
        var dark = new Vector4(0, 0, 0, colour.W);
        for (int k = 0; k < OutlineSteps.Length; k++)
        {
            var moved = box with { X = box.X + OutlineSteps[k].X, Y = box.Y + OutlineSteps[k].Y };
            root.Children.Add(Label($"{id}-outline-{k}", moved, text, dark));
        }

        root.Children.Add(Label(id, box, text, colour));
    }

    UiNode Label(string id, UiRect box, string text, Vector4 colour) =>
        new(id, UiNodeKind.Label)
        {
            Placement = new UiPlacement(box.Width, box.Height, Left: box.X, Top: box.Y),
            Text = text,
            Style = _style,
            TextColour = colour,
            Pickable = false,
            TextScale = TextScale,
        };
}
