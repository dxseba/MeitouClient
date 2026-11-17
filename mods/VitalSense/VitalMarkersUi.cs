using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.VitalSense;

/// <summary>
/// Draws the bound markers: the state letters centred on the point in the relation's colour, and the "$" to their left in the bounty tier's colour.
/// A label's colour is its style's, so each colour is a registered style.
/// </summary>
public sealed class VitalMarkersUi : IUiExtension
{
    const float StateWidth = 36, BountyWidth = 16, Height = 22;
    const int MaxSlots = 256;

    static readonly string[] StateText = ["", "ZZ", "RC", "DY", "PD", "DE"];

    /// <summary>Neutral, ally, enemy, squad (<see cref="Relation"/> order): the original's #DEE85A, #FF3333, #40FF40, and grey.</summary>
    static readonly Vector4[] RelationColours = [Hex(0xB4B4B4), Hex(0xDEE85A), Hex(0xFF3333), Hex(0x40FF40)];

    /// <summary>The original's tier colours, trivial to legendary (alpha left out: a label is opaque).</summary>
    static readonly Vector4[] TierColours =
        [Hex(0xB0B0B0), Hex(0xC9A57B), Hex(0x9FBD69), Hex(0x6FA6D9), Hex(0xFFC247), Hex(0xFF8A2B), Hex(0xE03131)];

    public string Id => "dxseba.vitalsense.markers";

    public void Register(UiRegistration registration)
    {
        var relations = RelationColours.Select((c, i) => registration.RegisterStyle("relation" + i, Style(c))).ToArray();
        var tiers = TierColours.Select((c, i) => registration.RegisterStyle("tier" + i, Style(c))).ToArray();
        registration.Contribute(UiSlots.Overlay, frame => Build(frame, relations, tiers), order: 40);
    }

    static UiStyle Style(Vector4 colour) =>
        new() { Text = colour, TextAlign = "Center", FontName = "Kenshi_StandardFont_Large", TextPadding = new(0, 0, 0, 0) };

    static Vector4 Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1);

    static UiNode Build(UiFrameContext frame, string[] relations, string[] tiers)
    {
        var root = new UiNode("vitalsense-markers") { Placement = UiPlacement.Fill, Pickable = false };
        for (int i = 0; i < MaxSlots; i++)
        {
            if (Parse(frame.Text(VitalMarkers.Key + i)) is not { } marker) break;

            AddMarker(root, i, marker, relations, tiers);
        }

        return root;
    }

    static void AddMarker(UiNode root, int i, Marker m, string[] relations, string[] tiers)
    {
        string text = StateText[Math.Clamp(m.State, 0, StateText.Length - 1)];
        float top = m.Y - Height / 2;
        if (text.Length > 0)
        {
            var placement = new UiPlacement(StateWidth, Height, Left: m.X - StateWidth / 2, Top: top);
            root.Children.Add(Label("vitalsense-state-" + i, placement, text, relations[Math.Clamp(m.Relation, 0, relations.Length - 1)]));
        }

        if (m.Tier < 0) return;

        float left = text.Length > 0 ? m.X - StateWidth / 2 - BountyWidth : m.X - BountyWidth / 2;
        var at = new UiPlacement(BountyWidth, Height, Left: left, Top: top);
        root.Children.Add(Label("vitalsense-bounty-" + i, at, "$", tiers[Math.Min(m.Tier, tiers.Length - 1)]));
    }

    static UiNode Label(string id, UiPlacement placement, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = placement, Text = text, Style = style, Pickable = false };

    readonly record struct Marker(float X, float Y, int State, int Relation, int Tier);

    static Marker? Parse(string bound)
    {
        var parts = bound.Split('|');
        if (parts.Length != 5) return null;

        var c = CultureInfo.InvariantCulture;
        bool ok = float.TryParse(parts[0], c, out float x) & float.TryParse(parts[1], c, out float y)
            & int.TryParse(parts[2], c, out int state) & int.TryParse(parts[3], c, out int relation) & int.TryParse(parts[4], c, out int tier);
        return ok ? new Marker(x, y, state, relation, tier) : null;
    }
}
