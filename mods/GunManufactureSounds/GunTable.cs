using System.Buffers.Binary;
using System.Text;

namespace Meitou.GunManufactureSounds;

/// <summary>
/// The original's gun table (README "Facts"): GunManufacture+ CROSSBOW record numbers to the GunSounds.bnk event each fires, read at start from
/// the installed original's <c>GunManufactureRealFireSounds.dll</c> as bytes (never loaded or run). The table is rows of 16 bytes in a data
/// section: a 32-bit record number, 4 zero bytes and the address of the event's name (lower-case letters, digits, <c>_</c>). The reader takes the
/// longest run of such rows (at least <see cref="MinRows"/>); anything else is no table.
/// </summary>
static class GunTable
{
    const int MinRows = 20;
    const uint CodeFlag = 0x20;

    sealed record Section(uint Rva, uint VirtualSize, int RawOffset, int RawSize, uint Flags);

    /// <summary>Record number to event name; empty when the file holds no table.</summary>
    public static Dictionary<int, string> Read(byte[] image)
    {
        var (imageBase, sections) = Sections(image);
        var best = new List<(int, string)>();
        foreach (var s in sections.Where(s => (s.Flags & CodeFlag) == 0))
        {
            var run = new List<(int, string)>();
            for (int o = s.RawOffset; o + 16 <= s.RawOffset + s.RawSize; o += 8)
            {
                if (Row(image, o, imageBase, sections) is { } row)
                {
                    run.Add(row);
                    o += 8;
                    continue;
                }

                if (run.Count > best.Count) best = run;
                run = [];
            }

            if (run.Count > best.Count) best = run;
        }

        return best.Count < MinRows ? [] : best.GroupBy(r => r.Item1).ToDictionary(g => g.Key, g => g.First().Item2);
    }

    static (int Id, string Name)? Row(byte[] image, int o, ulong imageBase, List<Section> sections)
    {
        int id = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(o));
        if (id <= 0 || BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(o + 4)) != 0) return null;

        ulong va = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(o + 8));
        return va > imageBase && Offset(va - imageBase, sections) is int at && Name(image, at) is { } name ? (id, name) : null;
    }

    /// <summary>The event name at a file offset: 1 to 24 lower-case letters, digits or <c>_</c> and a terminator; null otherwise.</summary>
    static string? Name(byte[] image, int at)
    {
        int end = at;
        while (end < image.Length && end - at <= 24 && image[end] != 0)
        {
            byte b = image[end];
            if (!(b is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'_')) return null;
            end++;
        }

        return end > at && end < image.Length && image[end] == 0 ? Encoding.ASCII.GetString(image, at, end - at) : null;
    }

    static int? Offset(ulong rva, List<Section> sections)
    {
        foreach (var s in sections)
        {
            if (rva >= s.Rva && rva < s.Rva + (ulong)s.RawSize) return s.RawOffset + (int)(rva - s.Rva);
        }

        return null;
    }

    static (ulong ImageBase, List<Section> Sections) Sections(byte[] image)
    {
        if (image.Length < 0x40) throw new FormatException("not a PE file");

        int pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        if (pe < 0 || pe + 24 > image.Length || BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(pe)) != 0x4550)
            throw new FormatException("not a PE file");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 6));
        int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 20));
        int optional = pe + 24;
        if (BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optional)) != 0x20B) throw new FormatException("not a 64-bit image");

        ulong imageBase = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(optional + 24));
        var sections = new List<Section>();
        for (int i = 0, h = optional + optionalSize; i < count; i++, h += 40)
        {
            if (h + 40 > image.Length) throw new FormatException("section table cut short");

            var span = image.AsSpan(h);
            int raw = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
            if (raw < 0 || size < 0 || (long)raw + size > image.Length) throw new FormatException("section outside the file");

            sections.Add(new Section(BinaryPrimitives.ReadUInt32LittleEndian(span[12..]), BinaryPrimitives.ReadUInt32LittleEndian(span[8..]), raw,
                size, BinaryPrimitives.ReadUInt32LittleEndian(span[36..])));
        }

        return (imageBase, sections);
    }
}
