using Meitou.Data;
using Meitou.Data.Fcs;

namespace Meitou.CityBuilder;

/// <summary>What a CITY building is for.</summary>
internal enum CityKind
{
    Residence,
    TradeHouse,
    GuardPost,
    Tavern,
}

/// <summary>Who lives and works in a CITY building (the record name's bracket: none, Hive, Skeletons, Swamp).</summary>
internal enum CityPeople
{
    People,
    Hive,
    Skeletons,
    Swamp,
}

/// <summary>
/// A CITY building of City.mod: its kind, its people, its level (Guard Post I-IV, Tavern I-III), whether it is priced small, the vanilla building
/// whose layouts furnish it (its icon's name) and its own name after the colon.
/// </summary>
internal sealed record CityBuilding(string Record, string Name, CityKind Kind, CityPeople People, int Level, bool Small, string Vanilla)
{
    /// <summary>Guards a post holds: 5, 10, 15, 20 for posts I-IV (City.ini comment, Workshop description).</summary>
    public int GuardCapacity => Kind == CityKind.GuardPost ? 5 * Math.Clamp(Level, 1, 4) : 0;

    /// <summary>Tavern beds and seats (I: 3 and 11, II: 6 and 30, III: 11 and 39; Workshop description).</summary>
    public (int Beds, int Seats) Tavern => Level switch { 1 => (3, 11), 2 => (6, 30), 3 => (11, 39), _ => (0, 0) };
}

/// <summary>
/// The CITY buildings of the loaded data (<c>building category</c> CITY, as City.mod sets it) and the squad templates of City.mod by role. City.mod
/// numbers its templates by people: People 300-340, Hive 410-428, Skeletons 430-448, Swamp 450-468; within a block the residents come first, then
/// the day, night and gate guards, then the nine shops (General Goods, Weapon, Armour, Prosthetics, Food, Tavern, Clothing, Tool, Crossbow).
/// </summary>
internal sealed class CityCatalog
{
    const string Mod = "-City.mod";

    readonly Dictionary<string, CityBuilding> _buildings = new(StringComparer.Ordinal);

    /// <summary>The shop kinds a trade house may hold: offsets into a block's shops (5, the tavern, is the innkeeper's).</summary>
    public static readonly int[] ShopKinds = [0, 1, 2, 3, 4, 6, 7, 8];

    /// <summary>The innkeeper's template (People only).</summary>
    public const string Innkeeper = "319" + Mod;

    public IReadOnlyDictionary<string, CityBuilding> Buildings => _buildings;

    public CityCatalog(GameDatabase db)
    {
        foreach (var r in db.OfType(FcsRecordType.Building).OrderBy(r => r.StringId, StringComparer.Ordinal))
        {
            if (r.GetString("building category") == "CITY" && Parse(r) is { } b) _buildings[r.StringId] = b;
        }
    }

    public CityBuilding? Find(string record) => _buildings.GetValueOrDefault(record);

    static CityBuilding? Parse(GameRecord r)
    {
        string name = r.BaseName;
        int colon = name.IndexOf(':');
        if (colon < 0) return null;

        string head = name[..colon];
        string own = name[(colon + 1)..].Trim();
        CityKind? kind = head.StartsWith("Residence", StringComparison.Ordinal) ? CityKind.Residence
            : head.StartsWith("Trade House", StringComparison.Ordinal) ? CityKind.TradeHouse
            : head.StartsWith("Guard Post", StringComparison.Ordinal) ? CityKind.GuardPost
            : head.StartsWith("Tavern", StringComparison.Ordinal) ? CityKind.Tavern
            : null;
        if (kind is not { } k) return null;

        var people = head.Contains("(Hive)", StringComparison.Ordinal) ? CityPeople.Hive
            : head.Contains("(Skeletons)", StringComparison.Ordinal) ? CityPeople.Skeletons
            : head.Contains("(Swamp)", StringComparison.Ordinal) ? CityPeople.Swamp
            : CityPeople.People;
        bool small = own is "Small Shack" or "Storm House";
        return new(r.StringId, own, k, people, Level(head), small, Vanilla(r.GetPath("icon")));
    }

    /// <summary>The roman level after the kind's name (I to IV); 0 for none.</summary>
    static int Level(string head)
    {
        foreach (string word in head.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int level = word switch { "I" => 1, "II" => 2, "III" => 3, "IV" => 4, _ => 0 };
            if (level > 0) return level;
        }

        return 0;
    }

    /// <summary>The vanilla BUILDING id an icon path is named after (<c>.\data\icons\buildings\4092-TwoStorey.mod.png</c>).</summary>
    static string Vanilla(string icon)
    {
        string file = icon.Replace('\\', '/');
        file = file[(file.LastIndexOf('/') + 1)..];
        return file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    #region Templates

    static int Block(CityPeople people) => people switch { CityPeople.Hive => 410, CityPeople.Skeletons => 430, CityPeople.Swamp => 450, _ => 300 };

    static int ShopBlock(CityPeople people) => Block(people) + 10;

    /// <summary>The residents' templates: People draws among three (300, 304, 305: different settlers), the others have one.</summary>
    public static string[] Residents(CityPeople people) =>
        people == CityPeople.People ? ["300" + Mod, "304" + Mod, "305" + Mod] : [Block(people) + Mod];

    /// <summary>A guard's template: day 1, night 2, gate 3 after the block's residents.</summary>
    public static string Guard(CityPeople people, GuardShift shift) => Block(people) + 1 + (int)shift + Mod;

    /// <summary>A shop's template, <paramref name="shop"/> an offset of <see cref="ShopKinds"/>.</summary>
    public static string Shop(CityPeople people, int shop) => ShopBlock(people) + shop + Mod;

    #endregion
}

/// <summary>A guard's shift: its template and AI package (City guard: day shift, night shift, gate).</summary>
internal enum GuardShift
{
    Day,
    Night,
    Gate,
}
