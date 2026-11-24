using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Meitou.Data.Fcs;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The boss table, wards and recalibrator read from the installed original's plugin (<c>ElementalEnchantmentTest.dll</c>) as bytes, never
/// loaded. Each part is found by the shape of the code that uses it, not by a file offset, and every value is checked; a part that does not
/// match throws <see cref="FormatException"/> and is read as none. Element numbers are the original's (<see cref="Element"/>).
/// </summary>
static partial class PluginTables
{
    /// <summary>
    /// The boss accessor: <c>index &lt; count ? &amp;table[index] : null</c> over 24-byte rows (<c>lea rcx,[rax+rax*2]; lea rax,[rax+rcx*8]</c>).
    /// </summary>
    const string BossAccessor = "83 F9 ?? 73 ?? 8B C1 48 8D 0C 40 48 8D 05 ?? ?? ?? ?? 48 8D 04 C8 C3";

    /// <summary>
    /// The ward check: an item id's length and capacity against one size, then the address of the id after its first character.
    /// </summary>
    const string WardCheck = "48 83 78 68 ?? 75 ?? 48 83 78 70 ?? 72 ?? 4C 8B 40 58 4D 85 C0 74 ?? 33 D2 4C 8D 0D ?? ?? ?? ??";

    /// <summary>
    /// The ward check's first character, one alone (<c>cmp cl,c; jne; mov eax,element</c>).
    /// </summary>
    const string WardSingle = "80 F9 ?? 75 ?? B8 ?? 00 00 00";

    /// <summary>
    /// The ward check's first character in a run (<c>lea eax,[rcx-low]; cmp al,n-1; ja; lea eax,[rcx-bias]</c>).
    /// </summary>
    const string WardRun = "8D 41 ?? 3C ?? 77 ?? 8D 41 ??";

    /// <summary>
    /// The recalibrator check: the used item id's length and capacity against one size, then the address of the whole id.
    /// </summary>
    const string RecalibratorCheck =
        "48 83 ?? 68 ?? 0F 85 ?? ?? ?? ?? 48 83 ?? 70 ?? 0F 82 ?? ?? ?? ?? 4C 8B ?? 58 ?? 85 C0 0F 84 ?? ?? ?? ?? ?? ?? ?? 4C 8D 0D ?? ?? ?? ??";

    const int BossRowSize = 24;
    const int MaxBosses = 1000;

    /// <summary>
    /// The boss table: CHARACTER id to main element, secondary (or none) and level. A row whose id is another type of record in the loaded data is
    /// left out: no character can have it.
    /// </summary>
    public static FrozenDictionary<string, (Element Main, Element Second, int Level)> Bosses(PeImage pe, Func<string, FcsRecordType?> typeOf)
    {
        ulong at = Single(pe, BossAccessor, "boss table");
        int count = pe.ByteAt(at + 2);
        if (count is 0 or > MaxBosses) throw new FormatException($"boss count {count}");

        ulong table = pe.RipTarget(at + 14, at + 18);
        var map = new Dictionary<string, (Element, Element, int)>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var (id, boss) = BossRow(pe, table + (ulong)(i * BossRowSize));
            if (typeOf(id) is { } type && type != FcsRecordType.Character) continue;
            if (!map.TryAdd(id, boss)) throw new FormatException($"boss row {i} repeats an id");
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// The wards: ITEM id to the element it holds.
    /// </summary>
    public static FrozenDictionary<string, Element> Wards(PeImage pe, Func<string, FcsRecordType?> typeOf)
    {
        ulong at = Single(pe, WardCheck, "ward check");
        int length = pe.ByteAt(at + 4);
        if (pe.ByteAt(at + 11) != length) throw new FormatException("ward check sizes differ");

        string rest = pe.CString(pe.RipTarget(at + 28, at + 32));
        if (rest.Length != length - 1) throw new FormatException("ward id length differs from the check");

        var map = new Dictionary<string, Element>(StringComparer.Ordinal);
        foreach (var (first, element) in WardFirstCharacters(pe, at))
        {
            string id = first + rest;
            CheckRecord(id, FcsRecordType.Item, typeOf);
            if (!map.TryAdd(id, element)) throw new FormatException("ward ids repeat");
        }

        if (map.Values.Distinct().Count() != map.Count) throw new FormatException("ward elements repeat");

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// The aptitude recalibrator's ITEM id.
    /// </summary>
    public static string Recalibrator(PeImage pe, Func<string, FcsRecordType?> typeOf)
    {
        ulong at = Single(pe, RecalibratorCheck, "recalibrator check");
        int length = pe.ByteAt(at + 4);
        if (pe.ByteAt(at + 15) != length) throw new FormatException("recalibrator check sizes differ");

        string id = pe.CString(pe.RipTarget(at + 41, at + 45));
        if (id.Length != length) throw new FormatException("recalibrator id length differs from the check");

        CheckRecord(id, FcsRecordType.Item, typeOf);
        return id;
    }

    /// <summary>
    /// One boss row: id pointer, main, secondary, level, padding.
    /// </summary>
    static (string Id, (Element, Element, int) Boss) BossRow(PeImage pe, ulong row)
    {
        string id = pe.CString(pe.UInt64At(row));
        var main = ElementOf(pe.Int32At(row + 8), allowNone: false);
        var second = ElementOf(pe.Int32At(row + 12), allowNone: true);
        int level = pe.Int32At(row + 16);
        if (second == main) throw new FormatException("boss secondary equals its main");

        if (level is < 1 or > Elements.MaxLevel) throw new FormatException($"boss level {level}");

        if (pe.Int32At(row + 20) != 0) throw new FormatException("boss row padding not zero");

        return (id, (main, second, level));
    }

    /// <summary>
    /// The first characters the ward check accepts and the element each gives: one alone, then a run of consecutive characters.
    /// </summary>
    static IEnumerable<(char First, Element Element)> WardFirstCharacters(PeImage pe, ulong check)
    {
        const int window = 96;
        ulong single = pe.FindNear(check, window, WardSingle) ?? throw new FormatException("ward check: no single character");
        ulong run = pe.FindNear(single, window, WardRun) ?? throw new FormatException("ward check: no character run");

        yield return (Digit(pe.ByteAt(single + 2)), ElementOf(pe.ByteAt(single + 6), allowNone: false));

        int low = -(sbyte)pe.ByteAt(run + 2);
        int count = pe.ByteAt(run + 4) + 1;
        int bias = -(sbyte)pe.ByteAt(run + 9);
        for (int c = low; c < low + count; c++) yield return (Digit(c), ElementOf(c - bias, allowNone: false));
    }

    /// <summary>
    /// The one place matching <paramref name="pattern"/>.
    /// </summary>
    static ulong Single(PeImage pe, string pattern, string what)
    {
        var found = pe.FindCode(pattern);
        return found.Count == 1 ? found[0] : throw new FormatException($"{what} found {found.Count} times");
    }

    /// <summary>
    /// A string id of the form <c>number-file.mod</c> (or <c>.base</c>), of <paramref name="type"/> where the loaded data has it.
    /// </summary>
    static void CheckRecord(string id, FcsRecordType type, Func<string, FcsRecordType?> typeOf)
    {
        if (!StringId().IsMatch(id)) throw new FormatException("an id is not a record string id");

        if (typeOf(id) is { } actual && actual != type) throw new FormatException($"an id is a {actual}, not a {type}");
    }

    static Element ElementOf(int value, bool allowNone) =>
        value is >= 0 and <= (int)Element.Ice && (allowNone || value != 0) ? (Element)value : throw new FormatException($"element {value}");

    static char Digit(int c) => c is >= '0' and <= '9' ? (char)c : throw new FormatException("ward id does not start with a digit");

    [GeneratedRegex(@"^\d+-[^\\/:*?""<>|]+\.(mod|base)$")]
    private static partial Regex StringId();
}
