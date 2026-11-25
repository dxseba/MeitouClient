using System.Buffers.Binary;
using System.IO.Compression;

namespace Meitou.DynamicWasteland;

/// <summary>
/// The political map's territory blob: a white disc fading out at its rim, written as a PNG into the mod's folder at start (the map tints it in
/// the owner's colour). Drawn by the port, not taken from the original.
/// </summary>
static class TerritoryImage
{
    public const string File = "territory.png";
    const int Size = 128;

    /// <summary>Writes the picture unless it is there; false when the folder cannot be written.</summary>
    public static bool Ensure(string folder)
    {
        string path = Path.Combine(folder, File);
        if (System.IO.File.Exists(path)) return true;

        try
        {
            System.IO.File.WriteAllBytes(path, Png());
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    static byte[] Png()
    {
        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Size);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Size);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", Pixels());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>The rows, each with filter 0, compressed: alpha 1 inside 60 % of the radius, falling smoothly to 0 at the rim.</summary>
    static byte[] Pixels()
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + Size * 4];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size * 2 - 1, dy = (y + 0.5f) / Size * 2 - 1;
                    float t = Math.Clamp((MathF.Sqrt(dx * dx + dy * dy) - 0.6f) / 0.4f, 0, 1);
                    float a = 1 - t * t * (3 - 2 * t);
                    int i = 1 + x * 4;
                    row[i] = row[i + 1] = row[i + 2] = 255;
                    row[i + 3] = (byte)(a * 255);
                }

                z.Write(row);
            }
        }

        return raw.ToArray();
    }

    static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        png.Write(four);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        png.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(four, Crc(body));
        png.Write(four);
    }

    static uint Crc(byte[] bytes)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }

        return c ^ 0xFFFFFFFF;
    }
}
