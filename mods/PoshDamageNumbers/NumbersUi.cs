using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.PoshDamageNumbers;

/// <summary>
/// Draws the laid-out floaters (<see cref="Floaters.Drawn"/>) in the overlay, laid out every frame (<see cref="UiNode.Animated"/>) on the layer
/// the <c>Layer</c> setting names (<see cref="UiNode.Layer"/>: Back under the game's windows, Popup over them). The base height is the
/// original's clamp(character height on screen x Number size, Smallest x res, Largest x res) x the floater's size (res = screen height /
/// 1080), times its pop. With the original's atlas (<see cref="Atlas"/>) each glyph is an image cell of it, tinted with the floater's colour
/// and faded by its opacity, as the original's image boxes; without it, a text label stands in.
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
        var s = PoshDamageNumbersMod.Settings;
        var layer = s.Layer == "Popup" ? UiOverlayLayer.Popup : UiOverlayLayer.Back;
        var root = new UiNode("posh-dmg") { Placement = UiPlacement.Fill, Pickable = false, Animated = true, Layer = layer };
        float res = frame.Viewport.Height / 1080;
        var atlas = PoshDamageNumbersMod.Atlas;
        int i = 0;
        foreach (var f in Floaters.Drawn)
        {
            float px = Math.Clamp(f.Body * s.NumberSize, s.MinNumberPx * res, MathF.Max(s.MinNumberPx, s.MaxNumberPx) * res) * f.Size;
            var centre = new Vector2(f.At.X + f.Offset.X * px + f.Shake.X * res, f.At.Y + f.Offset.Y * px + f.Shake.Y * res);
            if (atlas is not null) Glyphs(root, atlas, f, centre, px * f.Pop, i++);
            else root.Children.Add(Label(f, centre, px * f.Pop, res, style, i++));
        }

        return root;
    }

    /// <summary>
    /// The floater's glyphs in a row centred on <paramref name="centre"/> (<b>Verified</b>, <c>damage_numbers.lua</c> <c>animate</c>): scaled
    /// so a digit is <paramref name="h"/> high, each glyph centred vertically, the next one moved back by its overlap.
    /// </summary>
    static void Glyphs(UiNode root, Atlas atlas, DrawnFloater f, Vector2 centre, float h, int n)
    {
        var keys = Keys(f);
        float scale = h / atlas.DigitHeight, total = 0;
        for (int k = 0; k < keys.Count; k++)
        {
            var g = atlas.Glyphs[keys[k]];
            total += g.W * scale - (k < keys.Count - 1 ? g.Overlap * scale : 0);
        }

        float x = centre.X - total * 0.5f, y = centre.Y - h * 0.5f;
        var tint = new Vector4(f.Colour, 1);
        for (int k = 0; k < keys.Count; k++)
        {
            var g = atlas.Glyphs[keys[k]];
            root.Children.Add(new($"posh-dmg-{n}-{k}", UiNodeKind.Image)
            {
                Placement = new UiPlacement(MathF.Round(g.W * scale), MathF.Round(g.H * scale), Left: MathF.Round(x),
                    Top: MathF.Round(y + (h - g.H * scale) * 0.5f)),
                Image = atlas.Image, ImageCrop = UiImageCrop.Pixels(g.X, g.Y, g.W, g.H), Tint = tint, Opacity = f.Alpha, Pickable = false,
            });
            x += (g.W - g.Overlap) * scale;
        }
    }

    /// <summary>A word's one glyph (Russian or English by <c>WordsLanguage</c>), else the sign and digits of the number's text.</summary>
    static IReadOnlyList<string> Keys(DrawnFloater f)
    {
        if (f.Word is { } word)
        {
            string key = word switch { Word.Knockout => "KO", Word.Severed => "LIMB", _ => "DEAD" };
            return [PoshDamageNumbersMod.RussianWords ? key : key + "_EN"];
        }

        return [.. f.Text.Select(c => c.ToString())];
    }

    /// <summary>The text stand-in without the atlas: a centred label of that height in the floater's colour.</summary>
    static UiNode Label(DrawnFloater f, Vector2 centre, float h, float res, string style, int n)
    {
        float width = MathF.Max(40, h * 0.7f * f.Text.Length + 8);
        float scale = Math.Clamp(h / (FontPx * res), 0.1f, UiHost.MaxTextScale);
        return new("posh-dmg-" + n, UiNodeKind.Label)
        {
            Placement = new UiPlacement(width, h * 1.2f, Left: centre.X - width / 2, Top: centre.Y - h * 0.6f), Text = f.Text, Style = style,
            Pickable = false, TextScale = scale, TextColour = new(f.Colour, 1), Opacity = f.Alpha,
        };
    }
}
