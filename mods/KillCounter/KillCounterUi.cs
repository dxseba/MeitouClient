using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.KillCounter;

/// <summary>
/// The original's three lines of the character stats window ("Kills", "KO's", "Limbs Removed") in its attributes panel, after the four
/// attributes, in the original's colour (RGB 100, 95, 90), for the window's character.
/// </summary>
public sealed class KillCounterUi : IUiExtension
{
    /// <summary>The stats window's attribute rows: four of 20 pixels at the UI scale from the panel's top, values 32 wide at its right.</summary>
    const float RowHeight = 20, ValueWidth = 32, Attributes = 4;

    static readonly Vector4 Ink = new(100 / 255f, 95 / 255f, 90 / 255f, 1);

    /// <summary>The interface's scale, taken each frame.</summary>
    internal static volatile float UiScale = 1;

    string _label = "", _value = "";

    public string Id => "dxseba.killcounter.ui";

    public void Register(UiRegistration registration)
    {
        var face = new UiStyle { FontName = "Kenshi_PaintedTextFont_Medium", TextAlign = "Left VCenter", Text = Ink };
        _label = registration.RegisterStyle("label", face);
        _value = registration.RegisterStyle("value", face with { TextAlign = "Right VCenter" });
        registration.AddScreenControls("stats", "AttributesPanel", UiAnchorPlacement.Inside, Build);
    }

    UiNode? Build(UiScreenContext context)
    {
        if (context.Character is not { } character) return null;

        var id = character.LastingId;
        var tally = id.IsNone || KillCounterSystem.Current is not { } counter ? default : counter.Published.GetValueOrDefault(id.Value);
        var root = new UiNode("kills") { Placement = UiPlacement.Fill, Pickable = false };
        Row(root, 0, "Kills", tally.Kills);
        Row(root, 1, "KO's", tally.Knockouts);
        Row(root, 2, "Limbs Removed", tally.Limbs);
        return root;
    }

    void Row(UiNode root, int line, string label, int count)
    {
        float scale = UiScale, top = (Attributes + line) * RowHeight * scale, h = RowHeight * scale, value = ValueWidth * scale;
        root.Children.Add(new("label-" + line, UiNodeKind.Label)
        {
            Placement = new(UiLength.Fill, h, Top: top), Text = label, Style = _label, TextScale = scale, Pickable = false,
        });
        root.Children.Add(new("value-" + line, UiNodeKind.Label)
        {
            Placement = new(value, h, Top: top, Right: 0), Text = count.ToString(CultureInfo.InvariantCulture), Style = _value, TextScale = scale,
            Pickable = false,
        });
    }
}
