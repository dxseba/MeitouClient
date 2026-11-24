using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.VitalSense;

/// <summary>
/// Draws the frame's markers (<see cref="VitalMarkers.Current"/>) as one row centred on each point: the bounty symbol in its tier's colour, the
/// state icon (18 px, the DLL's) over the bounty glow (24 px), and the state text in the relation's colour at the state's text size; the symbol
/// before the icon or between the icon and the text (<c>bounty_symbol_position</c>). With the outline, each text has eight dark copies one pixel
/// round it (the DLL's outline text boxes).
/// </summary>
public sealed class VitalMarkersUi : IUiExtension
{
    /// <summary>
    /// The DLL's widget sizes: the row and icon 18 px, the glow 24 px, the state text at least 45 wide, the symbol 12 wide (at 18 px), a 1 px gap.
    /// </summary>
    const float Row = 18, Icon = 18, Glow = 24, StateWidth = 45, BountyWidth = 12, Gap = 1;

    /// <summary>
    /// The pixel height of the font at text scale 1 (<c>Kenshi_StandardFont_Medium</c>, size 20).
    /// </summary>
    const float FontPixels = 20;

    static readonly Vector2[] OutlineSteps = [new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)];

    readonly VitalIcons _icons;

    string _style = "";

    internal VitalMarkersUi(VitalIcons icons) => _icons = icons;

    public string Id => "dxseba.vitalsense.markers";

    public void Register(UiRegistration registration)
    {
        var style = new UiStyle { TextAlign = "Center", FontName = "Kenshi_StandardFont_Medium", TextPadding = new(0, 0, 0, 0) };
        _style = registration.RegisterStyle("marker", style);
        registration.Contribute(UiSlots.Overlay, _ => Build(), order: 40);
    }

    UiNode Build()
    {
        var markers = VitalMarkers.Current;

        // The markers follow the camera, so the overlay is laid out every frame while any shows.
        var root = new UiNode("vitalsense-markers") { Placement = UiPlacement.Fill, Pickable = false, Animated = markers.Count > 0 };
        for (int i = 0; i < markers.Count; i++) AddMarker(root, i, markers[i]);

        return root;
    }

    /// <summary>
    /// One marker's pieces, left to right.
    /// </summary>
    void AddMarker(UiNode root, int i, Marker m)
    {
        var settings = VitalSenseMod.Settings;
        string text = settings.ShowText ? settings.Text(m.State) : "";
        var icon = settings.ShowIcons && m.State != VitalState.None ? _icons.Of(m.State) : null;
        int size = settings.TextSize(m.State);
        float bountyWidth = m.Tier >= 0 ? BountyWidth * settings.BountySize / 18f : 0;

        // The state box grows past the DLL's 45 by 18 with a larger text size (a guess: 1.25 widths of the text's height a letter).
        float stateWidth = Math.Max(StateWidth, size * 1.25f * Math.Max(text.Length, 2)), stateHeight = Math.Max(Row, size);
        float width = (bountyWidth > 0 ? bountyWidth + Gap : 0) + (icon is not null ? Icon + Gap : 0) + (text.Length > 0 ? stateWidth : 0);
        float left = m.At.X - width / 2, top = m.At.Y - Row / 2;
        bool symbolFirst = settings.BountyAt == BountyPosition.BeforeStateIcon || icon is null;

        if (symbolFirst && bountyWidth > 0) left = AddSymbol(root, i, m, left, top, bountyWidth);
        if (icon is { } source) left = AddIcon(root, i, m, source, left, top);
        if (!symbolFirst && bountyWidth > 0) left = AddSymbol(root, i, m, left, top, bountyWidth);
        if (text.Length == 0) return;

        var box = new UiRect(left, m.At.Y - stateHeight / 2, stateWidth, stateHeight);
        AddText(root, "vitalsense-state-" + i, box, text, VitalColours.Of(m.Relation), size);
    }

    float AddSymbol(UiNode root, int i, Marker m, float left, float top, float width)
    {
        var settings = VitalSenseMod.Settings;
        var box = new UiRect(left, top - Math.Max(0, settings.BountySize - Row) / 2, width, Math.Max(Row, settings.BountySize));
        AddText(root, "vitalsense-bounty-" + i, box, settings.BountySymbol, VitalColours.Tier(m.Tier), settings.BountySize);
        return left + width + Gap;
    }

    /// <summary>
    /// The icon, over the glow (24 px round its 18, 3 px out) for a character with a bounty; a dying, playing dead or dead atlas cell in the
    /// relation colour (the DLL's <c>setColour</c>).
    /// </summary>
    float AddIcon(UiNode root, int i, Marker m, (string File, UiImageCrop? Crop, bool Atlas) source, float left, float top)
    {
        if (m.Tier >= 0 && VitalSenseMod.Settings.ShowGlow && _icons.Glow() is { } glow)
        {
            var round = new UiPlacement(Glow, Glow, Left: left - (Glow - Icon) / 2, Top: top - (Glow - Icon) / 2);
            root.Children.Add(new UiNode("vitalsense-glow-" + i, UiNodeKind.Image) { Placement = round, Image = glow.File, ImageCrop = glow.Crop });
        }

        var at = new UiPlacement(Icon, Icon, Left: left, Top: top);
        Vector4? tint = source.Atlas && VitalIcons.TintedCell(m.State) ? VitalColours.Of(m.Relation) : null;
        var icon = new UiNode("vitalsense-icon-" + i, UiNodeKind.Image) { Placement = at, Image = source.File, ImageCrop = source.Crop, Tint = tint };
        root.Children.Add(icon);
        return left + Icon + Gap;
    }

    /// <summary>
    /// A text in <paramref name="colour"/>, after its outline when that is on.
    /// </summary>
    void AddText(UiNode root, string id, UiRect box, string text, Vector4 colour, float pixels)
    {
        if (VitalSenseMod.Settings.Outline)
        {
            var dark = new Vector4(0, 0, 0, colour.W);
            for (int k = 0; k < OutlineSteps.Length; k++)
            {
                var step = OutlineSteps[k];
                var moved = box with { X = box.X + step.X, Y = box.Y + step.Y };
                root.Children.Add(Label($"{id}-outline-{k}", moved, text, dark, pixels));
            }
        }

        root.Children.Add(Label(id, box, text, colour, pixels));
    }

    UiNode Label(string id, UiRect box, string text, Vector4 colour, float pixels) =>
        new(id, UiNodeKind.Label)
        {
            Placement = new UiPlacement(box.Width, box.Height, Left: box.X, Top: box.Y),
            Text = Plain(text),
            Style = _style,
            TextColour = colour,
            Pickable = false,
            TextScale = pixels / FontPixels,
        };

    /// <summary>
    /// The text with its #s doubled, so a player's text is never read as an inline colour tag.
    /// </summary>
    static string Plain(string text) => text.Replace("#", "##", StringComparison.Ordinal);
}
