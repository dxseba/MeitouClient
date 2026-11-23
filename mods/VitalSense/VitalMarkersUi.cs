using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.VitalSense;

/// <summary>
/// Draws the frame's markers (<see cref="VitalMarkers.Current"/>) as one row centred on each point: the bounty "$" in its tier's colour (the
/// original's default <c>before_state_icon</c>), the state icon (18 px, the DLL's) and the state letters in the relation's colour at the state's
/// text size. A label's colour is its style's, so each colour is a registered style.
/// </summary>
public sealed class VitalMarkersUi : IUiExtension
{
    /// <summary>
    /// The DLL's widget sizes: the row and icon 18 px, the state text at least 45 wide, a 1 px gap after the icon and the symbol.
    /// </summary>
    const float Row = 18, Icon = 18, StateWidth = 45, BountyWidth = 16, Gap = 1;

    /// <summary>
    /// The pixel height of the font at text scale 1 (<c>Kenshi_StandardFont_Medium</c>, size 20).
    /// </summary>
    const float FontPixels = 20;

    /// <summary>
    /// <c>bounty_symbol_size_px</c>.
    /// </summary>
    const float BountySize = 18;

    static readonly string[] StateText = ["", "ZZ", "RC", "DY", "PD", "DE"];

    readonly VitalIcons _icons;

    string[] _relations = [], _tiers = [];

    internal VitalMarkersUi(VitalIcons icons) => _icons = icons;

    public string Id => "dxseba.vitalsense.markers";

    public void Register(UiRegistration registration)
    {
        _relations = [.. VitalColours.Relations.Select((c, i) => registration.RegisterStyle("relation" + i, Style(c)))];
        _tiers = [.. VitalColours.Tiers.Select((c, i) => registration.RegisterStyle("tier" + i, Style(c)))];
        registration.Contribute(UiSlots.Overlay, _ => Build(), order: 40);
    }

    static UiStyle Style(Vector4 colour) =>
        new() { Text = colour, TextAlign = "Center", FontName = "Kenshi_StandardFont_Medium", TextPadding = new(0, 0, 0, 0) };

    UiNode Build()
    {
        var markers = VitalMarkers.Current;

        // The markers follow the camera, so the overlay is laid out every frame while any shows.
        var root = new UiNode("vitalsense-markers") { Placement = UiPlacement.Fill, Pickable = false, Animated = markers.Count > 0 };
        for (int i = 0; i < markers.Count; i++) AddMarker(root, i, markers[i]);

        return root;
    }

    void AddMarker(UiNode root, int i, Marker m)
    {
        var settings = VitalSenseMod.Settings;
        string text = settings.ShowText ? StateText[(int)m.State] : "";
        var icon = settings.ShowIcons && m.State != VitalState.None ? _icons.Of(m.State) : null;
        int size = settings.TextSize(m.State);

        // The state box grows past the DLL's 45 by 18 with a larger text size (a guess: 2.5 widths of the text's height for two letters).
        float stateWidth = Math.Max(StateWidth, size * 2.5f), stateHeight = Math.Max(Row, size);
        float width = (m.Tier >= 0 ? BountyWidth + Gap : 0) + (icon is not null ? Icon + Gap : 0) + (text.Length > 0 ? stateWidth : 0);
        float left = m.At.X - width / 2, top = m.At.Y - Row / 2;

        if (m.Tier >= 0)
        {
            var at = new UiPlacement(BountyWidth, Row, Left: left, Top: top);
            root.Children.Add(Label("vitalsense-bounty-" + i, at, "$", _tiers[Math.Min(m.Tier, _tiers.Length - 1)], BountySize));
            left += BountyWidth + Gap;
        }

        if (icon is { } source)
        {
            var at = new UiPlacement(Icon, Icon, Left: left, Top: top);
            root.Children.Add(new UiNode("vitalsense-icon-" + i, UiNodeKind.Image) { Placement = at, Image = source.File, ImageCrop = source.Crop });
            left += Icon + Gap;
        }

        if (text.Length == 0) return;

        var placement = new UiPlacement(stateWidth, stateHeight, Left: left, Top: m.At.Y - stateHeight / 2);
        root.Children.Add(Label("vitalsense-state-" + i, placement, text, _relations[(int)m.Relation], size));
    }

    static UiNode Label(string id, UiPlacement placement, string text, string style, float pixels) =>
        new(id, UiNodeKind.Label) { Placement = placement, Text = text, Style = style, Pickable = false, TextScale = pixels / FontPixels };
}
