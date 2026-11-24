using Meitou.Game.Rendering.Ui;

namespace Meitou.ItemMarkers;

/// <summary>
/// The original's white mark pictures (<c>gui/gfx/itemmarkers_*.png</c>), read from the installed original's folder and tinted by the marks; none
/// is shipped with the port. A missing file (the original not installed) gives no texture and the marks fall back as <see cref="MarkLayout"/> says.
/// </summary>
sealed class MarkPictures(string? original)
{
    public const string Fill = "itemmarkers_fill.png", Gradient = "itemmarkers_gradient.png", Star = "itemmarkers_star.png";
    public const string GlowTop = "itemmarkers_glow_top.png", GlowBottom = "itemmarkers_glow_bottom.png";
    public const string GlowLeft = "itemmarkers_glow_left.png", GlowRight = "itemmarkers_glow_right.png";
    public const string Bag = "itemmarkers_bag.png", Known = "itemmarkers_known.png";

    readonly Dictionary<string, string?> _textures = new(StringComparer.Ordinal);

    /// <summary>The interface texture name of <paramref name="file"/> (the painter loads it when first drawn); null when the file is missing.</summary>
    public string? Texture(string file)
    {
        if (_textures.TryGetValue(file, out string? known)) return known;

        string? path = original is null ? null : Path.Combine(original, "gui", "gfx", file);
        string? texture = path is not null && File.Exists(path) ? UiPainter.IconTexture(path) : null;
        _textures[file] = texture;
        return texture;
    }
}
