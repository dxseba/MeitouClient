using System.Buffers.Binary;
using System.Text;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// A 64-bit PE file read as bytes, never loaded or run: its sections, virtual address to file offset, C strings and byte patterns in its code.
/// Every read is bounds-checked and fails with <see cref="FormatException"/>.
/// </summary>
sealed class PeImage
{
    const ushort Pe32Plus = 0x20B;
    const uint CodeFlag = 0x20;

    readonly byte[] _bytes;
    readonly List<Section> _sections = [];

    public PeImage(byte[] bytes)
    {
        _bytes = bytes;
        int pe = Int32(0x3C);
        if (UInt32(pe) != 0x4550) throw new FormatException("not a PE file");

        int optional = pe + 24;
        if (UInt16(optional) != Pe32Plus) throw new FormatException("not a 64-bit image");

        ImageBase = UInt64(optional + 24);
        ReadSections(pe, optional);
    }

    /// <summary>
    /// The preferred load address absolute pointers in the file are relative to.
    /// </summary>
    public ulong ImageBase { get; }

    /// <summary>
    /// The 32-bit little-endian value at virtual address <paramref name="va"/>.
    /// </summary>
    public int Int32At(ulong va) => Int32(Offset(va, 4));

    /// <summary>
    /// The 64-bit little-endian value at virtual address <paramref name="va"/>.
    /// </summary>
    public ulong UInt64At(ulong va) => UInt64(Offset(va, 8));

    /// <summary>
    /// The ASCII C string at virtual address <paramref name="va"/>, at most <paramref name="max"/> bytes before its terminator.
    /// </summary>
    public string CString(ulong va, int max = 260)
    {
        int start = Offset(va, 1);
        int end = Array.IndexOf(_bytes, (byte)0, start, Math.Min(max + 1, _bytes.Length - start));
        if (end < 0) throw new FormatException($"no string at 0x{va:X}");

        var text = _bytes.AsSpan(start, end - start);
        if (text.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E)) throw new FormatException($"not text at 0x{va:X}");

        return Encoding.ASCII.GetString(text);
    }

    /// <summary>
    /// The virtual address of every place in the code sections matching <paramref name="pattern"/> (hex bytes, <c>??</c> for any byte).
    /// </summary>
    public List<ulong> FindCode(string pattern)
    {
        var (bytes, mask) = Parse(pattern);
        var found = new List<ulong>();
        foreach (var s in _sections.Where(s => (s.Flags & CodeFlag) != 0))
        {
            for (int i = 0; i + bytes.Length <= s.RawSize; i++)
            {
                if (Matches(s.RawOffset + i, bytes, mask)) found.Add(ImageBase + s.Rva + (ulong)i);
            }
        }

        return found;
    }

    /// <summary>
    /// The first place within <paramref name="window"/> bytes after <paramref name="from"/> matching <paramref name="pattern"/>; null if none.
    /// </summary>
    public ulong? FindNear(ulong from, int window, string pattern)
    {
        var (bytes, mask) = Parse(pattern);
        for (int i = 0; i <= window; i++)
        {
            if (TryOffset(from + (ulong)i, bytes.Length) is { } at && Matches(at, bytes, mask)) return from + (ulong)i;
        }

        return null;
    }

    /// <summary>
    /// The target of the RIP-relative 32-bit displacement at <paramref name="va"/>, for an instruction that ends at <paramref name="next"/>.
    /// </summary>
    public ulong RipTarget(ulong va, ulong next) => (ulong)((long)next + Int32At(va));

    /// <summary>
    /// The byte at virtual address <paramref name="va"/>.
    /// </summary>
    public byte ByteAt(ulong va) => _bytes[Offset(va, 1)];

    /// <summary>
    /// The strict UTF-8 C string at virtual address <paramref name="va"/> (at most <paramref name="max"/> bytes, no control character but a line
    /// break); null when there is none or it is not valid text.
    /// </summary>
    public string? Utf8At(ulong va, int max)
    {
        if (TryOffset(va, 1) is not { } start) return null;

        int end = Array.IndexOf(_bytes, (byte)0, start, Math.Min(max + 1, _bytes.Length - start));
        if (end < 0) return null;

        var text = _bytes.AsSpan(start, end - start);
        foreach (byte b in text)
        {
            if (b < 0x20 && b != (byte)'\n') return null;
        }

        try
        {
            return Strict.GetString(text);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// The virtual address of every 8-byte-aligned 64-bit value in the data (not code) sections for which <paramref name="match"/> holds.
    /// </summary>
    public List<ulong> FindDataPointers(Func<ulong, bool> match)
    {
        var found = new List<ulong>();
        foreach (var s in _sections.Where(s => (s.Flags & CodeFlag) == 0))
        {
            for (uint i = 0; i + 8 <= s.RawSize; i += 8)
            {
                if (match(UInt64((int)(s.RawOffset + i)))) found.Add(ImageBase + s.Rva + i);
            }
        }

        return found;
    }

    static readonly UTF8Encoding Strict = new(false, true);

    void ReadSections(int pe, int optional)
    {
        int count = UInt16(pe + 6);
        int table = optional + UInt16(pe + 20);
        for (int i = 0; i < count; i++)
        {
            int at = table + 40 * i;
            var section = new Section(Rva: UInt32(at + 12), RawSize: UInt32(at + 16), RawOffset: UInt32(at + 20), Flags: UInt32(at + 36));
            if ((long)section.RawOffset + section.RawSize > _bytes.Length) throw new FormatException("section past the end of the file");

            _sections.Add(section);
        }
    }

    int Offset(ulong va, int length) => TryOffset(va, length) ?? throw new FormatException($"address 0x{va:X} not in the file");

    int? TryOffset(ulong va, int length)
    {
        if (va < ImageBase) return null;

        ulong rva = va - ImageBase;
        foreach (var s in _sections)
        {
            if (rva >= s.Rva && rva + (ulong)length <= (ulong)s.Rva + s.RawSize) return (int)(s.RawOffset + (rva - s.Rva));
        }

        return null;
    }

    bool Matches(long at, byte[] bytes, bool[] mask)
    {
        for (int k = 0; k < bytes.Length; k++)
        {
            if (mask[k] && _bytes[at + k] != bytes[k]) return false;
        }

        return true;
    }

    static (byte[] Bytes, bool[] Mask) Parse(string pattern)
    {
        string[] parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        var mask = new bool[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            mask[i] = parts[i] != "??";
            if (mask[i]) bytes[i] = Convert.ToByte(parts[i], 16);
        }

        return (bytes, mask);
    }

    int Int32(int at) => BinaryPrimitives.ReadInt32LittleEndian(Span(at, 4));

    uint UInt32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Span(at, 4));

    ushort UInt16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Span(at, 2));

    ulong UInt64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(Span(at, 8));

    ReadOnlySpan<byte> Span(int at, int length) =>
        at >= 0 && at + length <= _bytes.Length ? _bytes.AsSpan(at, length) : throw new FormatException($"offset 0x{at:X} past the end");

    /// <summary>
    /// One section header: where it maps, its bytes in the file, its characteristics.
    /// </summary>
    readonly record struct Section(uint Rva, uint RawSize, uint RawOffset, uint Flags);
}
