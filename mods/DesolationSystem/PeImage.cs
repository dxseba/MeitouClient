using System.Buffers.Binary;
using System.Text;

namespace Meitou.DesolationSystem;

/// <summary>One PE section: its name, where it is mapped (RVA, size) and where its bytes are in the file.</summary>
readonly record struct PeSection(string Name, uint Rva, uint VirtualSize, int RawOffset, int RawSize)
{
    public int RawEnd => RawOffset + RawSize;
}

/// <summary>
/// A 64-bit PE file read as bytes (no loading): its sections, virtual address -> file offset, and bounds-checked reads. Any malformed header
/// throws <see cref="InvalidDataException"/>. (The Disarm port's reader.)
/// </summary>
sealed class PeImage
{
    const int MaxFileSize = 64 << 20;
    const ushort Amd64 = 0x8664;
    const ushort Pe32Plus = 0x20b;

    readonly byte[] _bytes;

    PeImage(byte[] bytes, ulong imageBase, IReadOnlyList<PeSection> sections)
    {
        _bytes = bytes;
        ImageBase = imageBase;
        Sections = sections;
    }

    public ulong ImageBase { get; }

    public IReadOnlyList<PeSection> Sections { get; }

    public static PeImage Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileSize) throw new InvalidDataException("file too large");

        byte[] bytes = File.ReadAllBytes(path);
        int pe = Header(bytes);
        int optional = pe + 24;
        ulong imageBase = Need64(bytes, optional + 24);

        return new PeImage(bytes, imageBase, ReadSections(bytes, pe));
    }

    public PeSection? Section(string name) => Sections.FirstOrDefault(s => s.Name == name) is { RawSize: > 0 } s ? s : null;

    /// <summary>The file offset of virtual address <paramref name="va"/> inside a section's raw bytes; -1 when it is outside all of them.</summary>
    public int Offset(ulong va)
    {
        if (va < ImageBase || va - ImageBase > uint.MaxValue) return -1;

        uint rva = (uint)(va - ImageBase);
        foreach (var s in Sections)
        {
            if (rva >= s.Rva && rva - s.Rva < (uint)s.RawSize) return s.RawOffset + (int)(rva - s.Rva);
        }

        return -1;
    }

    public ulong Va(PeSection section, int offset) => ImageBase + section.Rva + (ulong)(offset - section.RawOffset);

    /// <summary>The byte at file offset <paramref name="offset"/>; 0 outside the file.</summary>
    public byte U8(int offset) => In(offset, 1) ? _bytes[offset] : (byte)0;

    public ulong U64(int offset) => In(offset, 8) ? BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(offset)) : 0;

    public int I32(int offset) => In(offset, 4) ? BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset)) : 0;

    /// <summary>
    /// The NUL-terminated UTF-8 string at <paramref name="va"/>: valid UTF-8 without control characters, 1 to <paramref name="max"/> bytes;
    /// null otherwise.
    /// </summary>
    public string? Utf8(ulong va, int max)
    {
        int start = Offset(va);
        if (start < 0) return null;

        int end = Array.IndexOf(_bytes, (byte)0, start, Math.Min(max + 1, _bytes.Length - start));
        if (end <= start) return null;

        try
        {
            string text = Strict.GetString(_bytes, start, end - start);
            return text.Any(char.IsControl) ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    static readonly UTF8Encoding Strict = new(false, true);

    bool In(int offset, int length) => offset >= 0 && offset <= _bytes.Length - length;

    static int Header(byte[] bytes)
    {
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("not a PE file");

        int pe = (int)Need32(bytes, 0x3c);
        if (Need32(bytes, pe) != 0x4550) throw new InvalidDataException("no PE signature");
        if (Need16(bytes, pe + 4) != Amd64 || Need16(bytes, pe + 24) != Pe32Plus) throw new InvalidDataException("not a 64-bit PE");

        return pe;
    }

    static List<PeSection> ReadSections(byte[] bytes, int pe)
    {
        int count = Need16(bytes, pe + 6);
        int table = pe + 24 + Need16(bytes, pe + 20);
        var sections = new List<PeSection>();
        for (int i = 0; i < count; i++)
        {
            int at = table + 40 * i;
            if (at + 40 > bytes.Length) throw new InvalidDataException("section table truncated");

            string name = Encoding.ASCII.GetString(bytes, at, 8).TrimEnd('\0');
            uint raw = Need32(bytes, at + 20);
            uint size = Math.Min(Need32(bytes, at + 16), (uint)Math.Max(0, bytes.Length - (long)raw));
            sections.Add(new(name, Need32(bytes, at + 12), Need32(bytes, at + 8), (int)Math.Min(raw, (uint)bytes.Length), (int)size));
        }

        return sections;
    }

    static ushort Need16(byte[] b, int o) =>
        o >= 0 && o <= b.Length - 2 ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)) : throw new InvalidDataException("header truncated");

    static uint Need32(byte[] b, int o) =>
        o >= 0 && o <= b.Length - 4 ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : throw new InvalidDataException("header truncated");

    static ulong Need64(byte[] b, int o) =>
        o >= 0 && o <= b.Length - 8 ? BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o)) : throw new InvalidDataException("header truncated");
}
