using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// Where the map is drawn: the whole map image's bounds on screen (it pans and zooms), the visible viewport it is clipped to, the UI scale and
/// the editor panel (zero size when it is not shown). Sizes are the original's pixels times the UI scale.
/// </summary>
readonly record struct MapLayout(UiRect Image, UiRect Viewport, float Ui, UiRect Editor)
{
    /// <summary>A marker's square side, and the selected one's.</summary>
    const float MarkerSize = 18, SelectedSize = 26;

    /// <summary>The editor's size (the fork's 510 x 198) and its offsets from the viewport's top right.</summary>
    const float EditorWidth = 510, EditorHeight = 198, EditorTop = 48, EditorRight = 18;

    /// <summary>The layout of <paramref name="view"/>, with the editor when a marker is selected.</summary>
    public static MapLayout Of(UiWorldMapView view)
    {
        bool editor = MapState.Active && MapState.Selected is not null;
        return new(view.Image, view.Viewport, view.UiScale, editor ? EditorBox(view.Viewport, view.UiScale) : default);
    }

    static UiRect EditorBox(UiRect viewport, float ui)
    {
        float width = EditorWidth * ui, height = EditorHeight * ui;
        float x = Math.Max(viewport.X, viewport.X + viewport.Width - width - EditorRight * ui);
        float y = Math.Clamp(viewport.Y + EditorTop * ui, viewport.Y, Math.Max(viewport.Y, viewport.Y + viewport.Height - height));
        return new(x, y, width, height);
    }

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
