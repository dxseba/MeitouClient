using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.BestMinimap;

/// <summary>
/// The map picture and the frame. The picture is one image node with a crop of the map file around the centre (clamped to the file, the
/// background colour beyond it, as the original's "where the map picture ends"); turned with the camera it is a square over the minimap's
/// diagonal, turned about the minimap's centre (<see cref="ImagePatch"/>). A round minimap is cut from horizontal strips of
/// <see cref="StripHeight"/> pixels, each as wide as the circle there, each clipping its own copy of the background and picture (the overlay
/// clips a node's children to it); the original masks its own texture instead.
/// </summary>
sealed partial class MinimapUi
{
    /// <summary>Half the world's size on each side of its centre (the big map's picture spans it, Meitou's world map).</summary>
    const float Extent = 147456;

    void AddMap(UiNode body, Projection v)
    {
        var origin = new Vector2(v.Rect.X, v.Rect.Y);
        if (!v.Round)
        {
            AddPicture(body, v, origin);
            return;
        }

        float r = v.Radius, top = v.Rect.Y;
        var c = v.ScreenCentre;
        for (float y = top; y < top + v.Rect.Height; y += StripHeight)
        {
            float mid = MathF.Min(y + StripHeight / 2, top + v.Rect.Height) - c.Y;
            float half = MathF.Sqrt(MathF.Max(0, r * r - mid * mid));
            if (half < 0.5f) continue;

            var strip = new UiRect(c.X - half, y, half * 2, StripHeight);
            var node = new UiNode($"bm-strip{_serial++}") { Placement = Within(strip, origin), Pickable = false };
            AddPicture(node, v, new(strip.X, strip.Y));
            body.Children.Add(node);
        }
    }

    /// <summary>The background and the map crop, placed relative to <paramref name="origin"/> (the parent's corner).</summary>
    void AddPicture(UiNode parent, Projection v, Vector2 origin)
    {
        var s = Minimap.Settings;
        var background = Palette.Parse(s.BackgroundColour, Palette.Rgb(0x1e1c19));
        parent.Children.Add(new UiNode($"bm-bg{_serial++}", UiNodeKind.Image)
        {
            Image = Sprites.Square, Tint = background, Pickable = false, Placement = Within(v.Rect, origin),
        });
        if (picture.File is not { } file || Crop(v) is not var (box, crop)) return;

        var node = new UiNode($"bm-map{_serial++}", UiNodeKind.Image)
        {
            Image = file, ImageCrop = crop, Opacity = Math.Clamp(s.Opacity, 1, 10) / 10f, Pickable = false, Placement = Within(box, origin),
        };
        parent.Children.Add(ImagePatch.Turned(node, v.Angle));
    }

    /// <summary>
    /// The picture's rectangle on screen (before its turn) and its crop of the map file; null when the view lies wholly off the map.
    /// </summary>
    static (UiRect Box, UiImageCrop Crop)? Crop(Projection v)
    {
        float diagonal = MathF.Sqrt(v.Rect.Width * v.Rect.Width + v.Rect.Height * v.Rect.Height);
        var size = v.Angle != 0 ? new Vector2(diagonal) : new(v.Rect.Width, v.Rect.Height);
        var half = size / 2 / v.Scale;
        var lo = Vector2.Clamp(v.Centre - half, new(-Extent), new(Extent));
        var hi = Vector2.Clamp(v.Centre + half, new(-Extent), new(Extent));
        if (hi.X - lo.X < 1 || hi.Y - lo.Y < 1) return null;

        // The clamped part's centre, in the unturned picture's pixels from the minimap's centre, turned as the picture turns.
        var offset = ((lo + hi) / 2 - v.Centre) * v.Scale;
        var centre = v.ScreenCentre + Projection.Turn(offset, v.Angle);
        var pixels = (hi - lo) * v.Scale;
        var box = new UiRect(centre.X - pixels.X / 2, centre.Y - pixels.Y / 2, pixels.X, pixels.Y);
        var u0 = (lo + new Vector2(Extent)) / (2 * Extent);
        var u1 = (hi + new Vector2(Extent)) / (2 * Extent);
        return (box, UiImageCrop.Uv(u0.X, u0.Y, u1.X, u1.Y));
    }

    /// <summary>The game panel frame of a square or rectangle: the game's floating panel skin a little larger than the map, under it.</summary>
    void AddGamePanel(UiNode root, Projection v)
    {
        if (v.Round || Minimap.Settings.Frame != MinimapSettings.Frames[2]) return;

        const float pad = 6;
        var r = v.Rect;
        var box = new UiRect(r.X - pad, r.Y - pad, r.Width + pad * 2, r.Height + pad * 2);
        root.Children.Add(new UiNode("bm-panel") { Style = _panel, Pickable = false, Placement = At(box) });
    }

    /// <summary>The frame line (a circle's frame in both frame styles), flashing in the alert colour while someone is attacked.</summary>
    void AddFrameLine(UiNode root, Projection v)
    {
        var s = Minimap.Settings;
        if (s.Frame == MinimapSettings.Frames[0] || !v.Round && s.Frame == MinimapSettings.Frames[2]) return;

        var colour = Palette.Parse(s.FrameColour, Palette.Frame);
        if (s.AlertFlashFrame && s.AttackAlert && Alerts.Any && (int)(Minimap.Now * 4) % 2 == 0) colour = Palette.Parse(s.AlertColour, Palette.Enemy);

        var r = v.Rect;
        if (v.Round)
        {
            int size = (int)MathF.Round(r.Width);
            root.Children.Add(Sprite(Sprites.FrameRing(size, 2), v.ScreenCentre, new(size), colour, Vector2.Zero));
            return;
        }

        const float w = 2;
        root.Children.Add(Bar(new(r.X, r.Y, r.Width, w), colour));
        root.Children.Add(Bar(new(r.X, r.Y + r.Height - w, r.Width, w), colour));
        root.Children.Add(Bar(new(r.X, r.Y, w, r.Height), colour));
        root.Children.Add(Bar(new(r.X + r.Width - w, r.Y, w, r.Height), colour));
    }

    UiNode Bar(UiRect r, Vector4 colour) =>
        new($"bm-bar{_serial++}", UiNodeKind.Image) { Image = Sprites.Square, Tint = colour, Pickable = false, Placement = At(r) };
}
