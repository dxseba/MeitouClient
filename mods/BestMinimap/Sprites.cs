using Meitou.Data.Textures;

namespace Meitou.BestMinimap;

/// <summary>
/// The port's own white shapes (dot, ring, diamond, arrow, square), drawn once into PNG files in the port's cache folder and tinted by the image
/// nodes (<c>UiNode.Tint</c>). The original draws its shapes into its own texture with pixel code; none of its art is used.
/// </summary>
static class Sprites
{
    static readonly Dictionary<string, string> Made = new(StringComparer.Ordinal);

    static string? s_folder;

    public static void Init(string cacheFolder) => s_folder = cacheFolder;

    /// <summary>A filled disc.</summary>
    public static string Dot => Get("dot", 32, (x, y, s) => Disc(x, y, s, s / 2f));

    /// <summary>A ring a twelfth of its size thick.</summary>
    public static string Ring => Get("ring", 64, (x, y, s) => Disc(x, y, s, s / 2f) - Disc(x, y, s, s / 2f - 5));

    /// <summary>A thin ring (town radius, large circles).</summary>
    public static string ThinRing => Get("thinring", 256, (x, y, s) => Disc(x, y, s, s / 2f) - Disc(x, y, s, s / 2f - 3));

    /// <summary>A ring of <paramref name="size"/> pixels and a stroke of <paramref name="stroke"/> pixels (the circle's frame).</summary>
    public static string FrameRing(int size, int stroke) =>
        Get($"frame{size}-{stroke}", size, (x, y, s) => Disc(x, y, s, s / 2f) - Disc(x, y, s, s / 2f - stroke));

    /// <summary>A diamond (a place without a game icon).</summary>
    public static string Diamond => Get("diamond", 32, DiamondCoverage);

    /// <summary>An arrow pointing up (the camera's view, a pinned marker's way).</summary>
    public static string Arrow => Get("arrow", 32, ArrowCoverage);

    /// <summary>A white square.</summary>
    public static string Square => Get("square", 4, (_, _, _) => 1);

    static string Get(string name, int size, Func<int, int, int, float> coverage)
    {
        if (Made.TryGetValue(name, out string? path)) return path;

        path = Path.Combine(s_folder ?? Path.GetTempPath(), $"sprite-{name}.png");
        if (!File.Exists(path)) Write(path, size, coverage);
        Made[name] = path;
        return path;
    }

    static void Write(string path, int size, Func<int, int, int, float> coverage)
    {
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
                pixels[i + 3] = (byte)(Math.Clamp(coverage(x, y, size), 0, 1) * 255);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        PngWriter.Write(temp, size, size, pixels);
        File.Move(temp, path, overwrite: true);
    }

    static float Disc(int x, int y, int size, float radius)
    {
        float dx = x + .5f - size / 2f, dy = y + .5f - size / 2f;
        return Math.Clamp(radius - MathF.Sqrt(dx * dx + dy * dy) + .5f, 0, 1);
    }

    static float DiamondCoverage(int x, int y, int size) =>
        Math.Clamp(size / 2f - (MathF.Abs(x + .5f - size / 2f) + MathF.Abs(y + .5f - size / 2f)), 0, 1);

    /// <summary>A triangle from the top centre to the bottom corners, with a notch at the bottom.</summary>
    static float ArrowCoverage(int x, int y, int size)
    {
        float u = (x + .5f) / size - .5f, v = (y + .5f) / size;
        float half = v * .45f;
        bool inside = MathF.Abs(u) <= half && v <= 1 - MathF.Abs(u) * .9f && v >= .05f;
        return inside ? 1 : 0;
    }
}
