using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.PoshDamageNumbers;

/// <summary>
/// Draws the bound floaters (<see cref="Floaters"/>) in the HUD overlay: the base height is the original's clamp(character height on screen x
/// Number size, Smallest x res, Largest x res) x the floater's size (res = screen height / 1080), times its pop; the label takes the nearest of
/// the registered text scales and its colour tag.
/// </summary>
public sealed class NumbersUi : IUiExtension
{
    /// <summary>The label font's height at text scale 1 at 1080p (<b>Unknown</b>, an estimate of Kenshi_StandardFont_Medium).</summary>
    const float FontPx = 18;

    const float MinScale = 0.6f, ScaleStep = 0.2f;
    const int Scales = 24;

    public string Id => "dxseba.poshdamagenumbers.ui";

    public void Register(UiRegistration registration)
    {
        var styles = new string[Scales];
        for (int i = 0; i < Scales; i++)
        {
            styles[i] = registration.RegisterStyle("n" + i.ToString(CultureInfo.InvariantCulture), new()
            {
                Text = new(1, 1, 1, 1), TextPadding = new(0, 0, 0, 0), TextAlign = "Center", TextScale = MinScale + i * ScaleStep,
            });
        }

        registration.Contribute(UiSlots.Overlay, frame => Build(frame, styles), order: 40);
    }

    static UiNode Build(UiFrameContext frame, string[] styles)
    {
        var root = new UiNode("posh-dmg") { Placement = UiPlacement.Fill, Pickable = false };
        float res = frame.Viewport.Height / 1080;
        for (int i = 0; i < Floaters.Slots; i++)
        {
            if (Parse(frame.Text(Floaters.Key + i)) is not { } f) continue;

            var s = PoshDamageNumbersMod.Settings;
            float px = Math.Clamp(f.Body * s.NumberSize, s.MinNumberPx * res, MathF.Max(s.MinNumberPx, s.MaxNumberPx) * res) * f.Size;
            float h = px * f.Pop, width = MathF.Max(40, h * 0.7f * f.Text.Length + 8);
            float cx = f.X + f.Dx * px + f.ShakeX * res, cy = f.Y + f.Dy * px + f.ShakeY * res;
            int bucket = Math.Clamp((int)MathF.Round((h / (FontPx * res) - MinScale) / ScaleStep), 0, Scales - 1);
            root.Children.Add(new("posh-dmg-" + i, UiNodeKind.Label)
            {
                Placement = new UiPlacement(width, h * 1.2f, Left: cx - width / 2, Top: cy - h * 0.6f), Text = f.Colour + f.Text,
                Style = styles[bucket], Pickable = false,
            });
        }

        return root;
    }

    sealed record Bound(float X, float Y, float Body, float Size, float Pop, float Dx, float Dy, float ShakeX, float ShakeY, string Colour, string Text);

    static Bound? Parse(string text)
    {
        var p = text.Split('|', 11);
        if (p.Length != 11) return null;

        var n = new float[9];
        for (int i = 0; i < 9; i++)
            if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i])) return null;

        return new(n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8], p[9], p[10]);
    }
}
