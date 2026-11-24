using System.Globalization;
using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Ui;

namespace Meitou.ItemMarkers;

/// <summary>
/// An item's grade as a mark shows it: its rank (0 to 5, compared with <see cref="MarkerSettings.MinGrade"/>) and colour.
/// </summary>
public readonly record struct ItemGrade(int Rank, Vector4 Colour);

/// <summary>
/// The grade of an item: a registered provider's (<see cref="ItemMarkersApi"/>), else a profile rule's (<see cref="GradeRules"/>), else the game's.
/// The game's (DLL <c>FUN_180009c30</c>): armour that covers some part (<c>Armour::hasArmourCoverage</c>), weapons, crossbows and limbs, by level:
/// below 16 grade 0, 36 grade 1, 51 grade 2, 66 grade 3, up to 80 grade 4, above 5; anything else none. Colours are the interface's
/// <c>ItemGrade0..5</c> (grade 5 falls back to 4's, as the game), and a Meitou-class weapon (material <c>52293-rebirth.mod</c>) takes
/// <c>ItemGradeMeitou</c>, else <c>ItemGrade5</c>, else 4's.
/// </summary>
sealed class Grades(GameDatabase data, GradeRules rules)
{
    public const string MeitouMaterial = "52293-rebirth.mod";

    /// <summary>The vanilla <c>kenshi_colours.xml</c> grade colours (<b>Verified</b>, the installed file); grade 5 has none.</summary>
    static readonly string[] VanillaHex = ["666666", "d9ccbf", "4099b3", "66fc80", "fceb0d"];

    /// <summary>A colour no palette writes, standing for "the palette lacks this key".</summary>
    const string Missing = "010203";

    readonly Dictionary<string, bool> _covers = new(StringComparer.Ordinal);

    /// <summary>The grade of <paramref name="item"/>; null for an item without one.</summary>
    public ItemGrade? Of(in ItemFacts item)
    {
        var type = data.Find(item.Record)?.Type;
        if (ItemMarkersApi.Ask(item, type) is { } provided) return provided;
        if (rules.Match(item, type, IsGear(type)) is { } ruled) return ruled;
        if (!Graded(item.Record, type)) return null;

        int rank = RankOf(item.Level);
        return new(rank, item.Material == MeitouMaterial ? MeitouColour() : GradeColour(rank));
    }

    /// <summary>The record type of <paramref name="record"/>; null when the data lacks it.</summary>
    public FcsRecordType? TypeOf(string record) => data.Find(record)?.Type;

    /// <summary>The game's grade of a gear level (DLL <c>FUN_180009c30</c>).</summary>
    public static int RankOf(int level) => level < 16 ? 0 : level < 36 ? 1 : level < 51 ? 2 : level < 66 ? 3 : level <= 80 ? 4 : 5;

    /// <summary>The record types whose level is a gear level (the game's graded types).</summary>
    public static bool IsGear(FcsRecordType? type) =>
        type is FcsRecordType.Weapon or FcsRecordType.Armour or FcsRecordType.Crossbow or FcsRecordType.LimbReplacement;

    bool Graded(string record, FcsRecordType? type) => type switch
    {
        FcsRecordType.Armour => Covers(record),
        FcsRecordType.Weapon or FcsRecordType.Crossbow or FcsRecordType.LimbReplacement => true,
        _ => false,
    };

    /// <summary>An ARMOUR covering any part (a <c>part coverage</c> entry above 0); sandals and some trousers cover none and have no grade.</summary>
    bool Covers(string record)
    {
        if (_covers.TryGetValue(record, out bool known)) return known;

        bool covers = data.Find(record)?.GetReferences("part coverage").Any(r => r.Values.Value0 > 0) == true;
        _covers[record] = covers;
        return covers;
    }

    /// <summary>The palette's <c>ItemGrade{rank}</c>; grade 5 without a key of its own takes grade 4's.</summary>
    public static Vector4 GradeColour(int rank)
    {
        rank = Math.Clamp(rank, 0, 5);
        if (rank == 5 && Palette("ItemGrade5") is { } five) return five;

        int shown = Math.Min(rank, 4);
        return MyGuiColours.Parse(MyGuiColours.Named($"ItemGrade{shown}", VanillaHex[shown]), Vector4.One);
    }

    /// <summary>The Meitou class's colour: <c>ItemGradeMeitou</c>, else grade 5's.</summary>
    public static Vector4 MeitouColour() => Palette("ItemGradeMeitou") ?? GradeColour(5);

    /// <summary>A palette role's colour (<c>ItemGrade0..5</c>, <c>ItemGradeMeitou</c>); null when the palette lacks it.</summary>
    public static Vector4? Palette(string role)
    {
        string value = MyGuiColours.Named(role, Missing);
        return value == MyGuiColours.Named("\u0001missing", Missing) ? null : MyGuiColours.Parse(value, Vector4.One);
    }

    /// <summary>A <c>#RRGGBB</c> text as a colour; null when it is not one.</summary>
    public static Vector4? Hex(string? text)
    {
        string s = (text ?? "").Trim();
        if (s.Length != 7 || s[0] != '#' || !int.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return null;

        return new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    }
}
