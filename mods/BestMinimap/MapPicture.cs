using Meitou.Data.Textures;

namespace Meitou.BestMinimap;

/// <summary>
/// The game's map picture (<c>GUI_Map.dds</c>, as the big map finds it, so a map replacement is used), read once on a worker, scaled down to the
/// <c>MaxSourceSize</c> limit by whole-pixel averaging (red and blue swapped with <c>SwapRedBlue</c>), and kept as a PNG file in the port's cache
/// folder (made again when the source, the limit or the swap changes). The minimap draws a crop of that file. As in the original's README: read
/// once, kept scaled in memory, with "could not read the map picture" logged when it fails.
/// </summary>
sealed class MapPicture(string cacheFolder, Action<string> log)
{
    Task? _work;

    /// <summary>The PNG to draw; null while it is being made or when it could not be.</summary>
    public volatile string? File;

    /// <summary>The picture's size in pixels once made.</summary>
    public int Width, Height;

    /// <summary>Starts making the file for <paramref name="source"/> once.</summary>
    public void Start(string source, int maxSize, bool swap)
    {
        if (_work is not null) return;

        _work = Task.Run(() => Make(source, maxSize, swap));
    }

    void Make(string source, int maxSize, bool swap)
    {
        try
        {
            var info = new FileInfo(source);
            string key = $"{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{maxSize}|{swap}";
            string path = Path.Combine(cacheFolder, $"map-{(uint)StableHash(key):x8}.png");
            if (!System.IO.File.Exists(path)) Write(source, path, maxSize, swap);

            var image = ReadSize(path);
            (Width, Height) = image;
            File = path;
            log($"map picture '{source}' ready ({Width}x{Height}): {path}");
        }
        catch (Exception e)
        {
            log($"could not read the map picture '{source}': {e.Message}; the minimap shows no terrain");
        }
    }

    static void Write(string source, string path, int maxSize, bool swap)
    {
        var full = TextureLoader.LoadFile(source, allMips: false).Levels[0];
        int factor = Math.Max(1, (Math.Max(full.Width, full.Height) + maxSize - 1) / maxSize);
        int w = Math.Max(1, full.Width / factor), h = Math.Max(1, full.Height / factor);
        var pixels = Downscale(full, factor, w, h, swap);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        PngWriter.Write(temp, w, h, pixels);
        System.IO.File.Move(temp, path, overwrite: true);
    }

    static byte[] Downscale(RgbaImage full, int factor, int w, int h, bool swap)
    {
        var src = full.Pixels;
        var dst = new byte[w * h * 4];
        int n = factor * factor;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int dy = 0; dy < factor; dy++)
                {
                    int row = ((y * factor + dy) * full.Width + x * factor) * 4;
                    for (int dx = 0; dx < factor; dx++, row += 4)
                        (r, g, b) = (r + src[row], g + src[row + 1], b + src[row + 2]);
                }

                int o = (y * w + x) * 4;
                (dst[o], dst[o + 1], dst[o + 2], dst[o + 3]) = ((byte)((swap ? b : r) / n), (byte)(g / n), (byte)((swap ? r : b) / n), 255);
            }
        });
        return dst;
    }

    /// <summary>A PNG's width and height from its header.</summary>
    static (int, int) ReadSize(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = System.IO.File.OpenRead(path);
        stream.ReadExactly(header);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..]));
    }

    static int StableHash(string text)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }
}
