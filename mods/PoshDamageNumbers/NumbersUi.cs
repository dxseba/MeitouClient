using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.PoshDamageNumbers;

/// <summary>
/// Draws the laid-out floaters (<see cref="Floaters.Drawn"/>) in the HUD overlay, laid out every frame (<see cref="UiNode.Animated"/>): the base
/// height is the original's clamp(character height on screen x Number size, Smallest x res, Largest x res) x the floater's size (res = screen
/// height / 1080), times its pop; the label's text scale gives that height, its colour and opacity are the floater's.
/// </summary>
public sealed class NumbersUi : IUiExtension
{
    /// <summary>The label font's height at text scale 1 at 1080p (<b>Unknown</b>, an estimate of Kenshi_StandardFont_Medium).</summary>
    const float FontPx = 18;

    public string Id => "dxseba.poshdamagenumbers.ui";

    public void Register(UiRegistration registration)
    {
        string style = registration.RegisterStyle("number", new() { Text = Vector4.One, TextPadding = new(0, 0, 0, 0), TextAlign = "Center" });
        registration.Contribute(UiSlots.Overlay, frame => Build(frame, style), order: 40);
    }

    static UiNode Build(UiFrameContext frame, string style)
    {
        var root = new UiNode("posh-dmg") { Placement = UiPlacement.Fill, Pickable = false, Animated = true };
        float res = frame.Viewport.Height / 1080;
        var s = PoshDamageNumbersMod.Settings;
        int i = 0;
        foreach (var f in Floaters.Drawn)
        {
            float px = Math.Clamp(f.Body * s.NumberSize, s.MinNumberPx * res, MathF.Max(s.MinNumberPx, s.MaxNumberPx) * res) * f.Size;
            float h = px * f.Pop, width = MathF.Max(40, h * 0.7f * f.Text.Length + 8);
            float cx = f.At.X + f.Offset.X * px + f.Shake.X * res, cy = f.At.Y + f.Offset.Y * px + f.Shake.Y * res;
            float scale = Math.Clamp(h / (FontPx * res), 0.1f, UiHost.MaxTextScale);
            root.Children.Add(new("posh-dmg-" + i++, UiNodeKind.Label)
            {
                Placement = new UiPlacement(width, h * 1.2f, Left: cx - width / 2, Top: cy - h * 0.6f), Text = f.Text, Style = style,
                Pickable = false, TextScale = scale, TextColour = new(f.Colour, 1), Opacity = f.Alpha,
            });
        }

        return root;
    }
}
