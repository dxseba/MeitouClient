using System.Numerics;
using Meitou.Data.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// Where the map was drawn last frame: the whole map image's bounds on screen (it pans and zooms), the visible viewport it is clipped to, the UI
/// scale and the editor panel (zero size when it is not shown). Sizes are the original's pixels times the UI scale.
/// </summary>
readonly record struct MapLayout(UiRect Image, UiRect Viewport, float Ui, UiRect Editor)
{
    /// <summary>A marker's square side, and the selected one's.</summary>
    const float MarkerSize = 18, SelectedSize = 26;

    /// <summary>The map image point (0..1 each way) under <paramref name="p"/>, clamped to the image.</summary>
    public Vector2 Normalized(Vector2 p) =>
        Vector2.Clamp(new((p.X - Image.X) / Image.Width, (p.Y - Image.Y) / Image.Height), Vector2.Zero, Vector2.One);

    /// <summary>A marker's square, centred on its point and kept inside the map image, as the original places its marker buttons.</summary>
    public UiRect Box(Marker marker, bool selected)
    {
        float size = (selected ? SelectedSize : MarkerSize) * Ui;
        float x = Image.X + marker.At.X * Image.Width - size / 2;
        float y = Image.Y + marker.At.Y * Image.Height - size / 2;
        x = Math.Clamp(x, Image.X, Math.Max(Image.X, Image.X + Image.Width - size));
        y = Math.Clamp(y, Image.Y, Math.Max(Image.Y, Image.Y + Image.Height - size));
        return new(x, y, size, size);
    }

    /// <summary>Whether a marker's square shows: inside the viewport and not under the open editor (the original hides those).</summary>
    public bool Shows(UiRect box) => Overlaps(box, Viewport) && !Overlaps(box, Editor);

    static bool Overlaps(UiRect a, UiRect b)
    {
        var both = a.Intersect(b);
        return both.Width > 0 && both.Height > 0;
    }
}
