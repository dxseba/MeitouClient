using System.Buffers.Binary;
using System.Text;
using Meitou.Modding;

namespace Meitou.CityBuilder;

/// <summary>
/// The original's translations, read at start from the installed original for the game's language and added in memory as the mod's catalogue
/// (nothing is written): Chinese from its zh_CN.txt (English, a tab, Chinese; <c>\n</c> a line break; <c>#</c> comments), Russian from City.dll,
/// whose texts are pairs passed to one picker (the English wide string in r8, the Russian in rdx: <c>lea r8, [rip+d]</c> then
/// <c>lea rdx, [rip+d]</c> within 40 bytes), kept when the Russian has Cyrillic letters and the English is ASCII.
/// </summary>
static class CityTexts
{
    public static void Add(ModContext context, string? original)
    {
        if (original is null) return;

        string language = context.Language.Split('_', '-')[0].ToLowerInvariant();
        try
        {
            var pairs = language switch
            {
                "zh" => Chinese(Path.Combine(original, "zh_CN.txt")),
                "ru" => Russian(Path.Combine(original, "City.dll")),
                _ => null,
            };
            if (pairs is null) return;

            context.AddTranslations(context.Language, pairs);
            context.Log($"original texts read: {pairs.Count} for {context.Language}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            context.Log($"original texts not read ({e.Message}): texts stay English");
        }
    }

    static Dictionary<string, string>? Chinese(string path)
    {
        if (!File.Exists(path)) return null;

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            int tab = line.IndexOf('\t');
            if (line.StartsWith('#') || tab <= 0) continue;

            pairs.TryAdd(Unescape(line[..tab]), Unescape(line[(tab + 1)..]));
        }

        return pairs;
    }

    static string Unescape(string s) => s.Replace("\\n", "\n", StringComparison.Ordinal);

    static Dictionary<string, string>? Russian(string path)
    {
        if (!File.Exists(path)) return null;

        var image = new Pe(File.ReadAllBytes(path));
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (image.Text is not { } text) return pairs;

        for (int i = text.Raw; i < text.Raw + text.Size - 14; i++)
        {
            if (!image.Lea(i, 0x4c, 0x05) || image.Wide(image.Target(i)) is not { } english || !english.All(c => c < 128)) continue;

            for (int j = i + 7; j < i + 40; j++)
            {
                if (!image.Lea(j, 0x48, 0x15)) continue;

                if (image.Wide(image.Target(j)) is { } russian && Cyrillic(russian)) pairs.TryAdd(english, russian);
                break;
            }
        }

        AddTables(image, pairs);
        return pairs;
    }

    static bool Cyrillic(string s) => s.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    /// <summary>
    /// The data tables of names (the buildings' own names, the shops and their traders): pointer pairs to the Russian and the English wide string.
    /// </summary>
    static void AddTables(Pe image, Dictionary<string, string> pairs)
    {
        foreach (var (raw, size) in image.DataSections)
        {
            for (int o = raw; o + 16 <= raw + size; o += 8)
            {
                if (image.WideAt(image.U64(o)) is not { } russian || !Cyrillic(russian)) continue;
                if (image.WideAt(image.U64(o + 8)) is { } english && english.All(c => c < 128)) pairs.TryAdd(english, russian);
            }
        }
    }

    /// <summary>
    /// The few PE facts the scan needs: the sections (RVA, raw offset, raw size).
    /// </summary>
    sealed class Pe
    {
        readonly byte[] _d;
        readonly List<(string Name, int Rva, int Raw, int Size)> _sections = [];

        public Pe(byte[] d)
        {
            _d = d;
            int pe = I32(0x3c);
            if (pe <= 0 || pe + 24 > d.Length || I32(pe) != 0x4550) throw new InvalidDataException("not a PE image");

            int count = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pe + 6));
            int optional = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pe + 20));
            for (int i = 0, o = pe + 24 + optional; i < count && o + 40 <= d.Length; i++, o += 40)
            {
                string name = Encoding.ASCII.GetString(d, o, 8).TrimEnd('\0');
                _sections.Add((name, I32(o + 12), I32(o + 20), I32(o + 16)));
            }
        }

        /// <summary>The image base (the optional header's, PE32+).</summary>
        ulong ImageBase => U64(I32(0x3c) + 24 + 24);

        /// <summary>The raw extents of the sections other than .text.</summary>
        public IEnumerable<(int Raw, int Size)> DataSections => _sections.Where(s => s.Name != ".text" && s.Size > 0).Select(s => (s.Raw, s.Size));

        public ulong U64(int o) => o >= 0 && o + 8 <= _d.Length ? BinaryPrimitives.ReadUInt64LittleEndian(_d.AsSpan(o)) : 0;

        /// <summary>The wide string a pointer (a virtual address) names; null when it points outside the image.</summary>
        public string? WideAt(ulong va) => va > ImageBase && va - ImageBase < int.MaxValue ? Wide((int)(va - ImageBase)) : null;

        public (int Rva, int Raw, int Size)? Text =>
            _sections.FirstOrDefault(s => s.Name == ".text") is { Size: > 0 } s ? (s.Rva, s.Raw, s.Size) : null;

        int I32(int o) => o >= 0 && o + 4 <= _d.Length ? BinaryPrimitives.ReadInt32LittleEndian(_d.AsSpan(o)) : 0;

        /// <summary>
        /// A <c>lea reg, [rip+disp32]</c> with REX <paramref name="rex"/> and ModRM <paramref name="modrm"/> at raw offset <paramref name="o"/>.
        /// </summary>
        public bool Lea(int o, byte rex, byte modrm) => o + 7 <= _d.Length && _d[o] == rex && _d[o + 1] == 0x8d && _d[o + 2] == modrm;

        /// <summary>The RVA a lea at raw offset <paramref name="o"/> points to.</summary>
        public int Target(int o) => RvaOf(o) + 7 + I32(o + 3);

        int RvaOf(int raw)
        {
            foreach (var s in _sections)
            {
                if (raw >= s.Raw && raw < s.Raw + s.Size) return s.Rva + raw - s.Raw;
            }

            return -1;
        }

        int RawOf(int rva)
        {
            foreach (var s in _sections)
            {
                if (rva >= s.Rva && rva < s.Rva + s.Size) return s.Raw + rva - s.Rva;
            }

            return -1;
        }

        /// <summary>The NUL-ended UTF-16 string at <paramref name="rva"/>; null when outside the image, empty or too long.</summary>
        public string? Wide(int rva)
        {
            int start = RawOf(rva);
            if (start < 0) return null;

            int end = start;
            while (end + 1 < _d.Length && (_d[end] | _d[end + 1]) != 0 && end - start < 8000) end += 2;
            return end > start && end - start < 8000 ? Encoding.Unicode.GetString(_d, start, end - start) : null;
        }
    }
}
