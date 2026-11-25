using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation.Population;

namespace Meitou.DirectorPanel;

/// <summary>One pickable record of a list: its string id, shown name and group (an item's kind, a character's people or animals).</summary>
internal sealed record Entry(string Id, string Name, string Group);

/// <summary>A weapon grade of a manufacturer: its model (MATERIAL_SPECS_WEAPON) and level.</summary>
internal sealed record Grade(string Model, string Name, int Level);

/// <summary>
/// The panel's lists, built once from the merged data (the original builds them when a save is loaded, "List built: %d items, %d manufacturers"):
/// items by kind, weapon manufacturers with their own grades (<c>weapon models</c>), characters (people, animals and machines), factions,
/// research, AI packages and effects. Read only after <see cref="DirectorPanelMod.Initialize"/>.
/// </summary>
internal sealed class Catalog
{
    /// <summary>The item kinds of the Items tab filter, in the original's order.</summary>
    internal static readonly string[] Kinds = ["All", "Weapons", "Armour", "Crossbows", "Limbs", "Backpacks", "Items"];

    public Catalog(GameDatabase db, PopulationData population)
    {
        Items = [.. Sorted(Records(db, ItemTypes).Select(r => new Entry(r.StringId, r.Name, KindOf(r.Type))))];
        Makers = [.. Sorted(Records(db, [FcsRecordType.WeaponManufacturer]).Select(r => new Entry(r.StringId, r.Name, "")))];
        Grades = Makers.ToDictionary(m => m.Id, m => GradesOf(db, m.Id));
        People = [.. Sorted(Records(db, [FcsRecordType.Character]).Select(r => new Entry(r.StringId, r.Name, "people")))];
        Animals = [.. Sorted(Records(db, [FcsRecordType.AnimalCharacter]).Select(r => new Entry(r.StringId, r.Name, "animals")))];
        Factions = [.. population.Factions.Select((f, i) => new Entry(i.ToString(System.Globalization.CultureInfo.InvariantCulture), f.Name, f.Id))];
        Research = [.. Sorted(Records(db, [FcsRecordType.Research]).Select(r => new Entry(r.StringId, r.Name, "")))];
        Packages = [.. Sorted(Records(db, [FcsRecordType.AiPackage]).Select(r => new Entry(r.StringId, r.Name, "")))];
        Effects = [.. Sorted(Records(db, [FcsRecordType.Effect]).Select(r => new Entry(r.StringId, r.Name, IsBlood(r.Name) ? "blood" : "")))];
        Colours = [.. Sorted(Records(db, [FcsRecordType.ColorData]).Select(r => new Entry(r.StringId, r.Name, "")))];
        Poses = [.. db.OfType(FcsRecordType.Animation).Where(IsIdleStance).Select(a => new Entry(a.Name, a.Name, ""))];
    }

    static readonly FcsRecordType[] ItemTypes =
        [FcsRecordType.Weapon, FcsRecordType.Armour, FcsRecordType.Crossbow, FcsRecordType.LimbReplacement, FcsRecordType.Container, FcsRecordType.Item];

    /// <summary>The original's "Blood only" effect filter: names holding blood, splat, hack, spurt or peel.</summary>
    static readonly string[] BloodWords = ["blood", "splat", "hack", "spurt", "peel"];

    public IReadOnlyList<Entry> Items { get; }

    public IReadOnlyList<Entry> Makers { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<Grade>> Grades { get; }

    public IReadOnlyList<Entry> People { get; }

    public IReadOnlyList<Entry> Animals { get; }

    /// <summary>Every faction, its id the faction's index as text and its group the FACTION record id.</summary>
    public IReadOnlyList<Entry> Factions { get; }

    public IReadOnlyList<Entry> Research { get; }

    public IReadOnlyList<Entry> Packages { get; }

    public IReadOnlyList<Entry> Effects { get; }

    /// <summary>The COLOR_DATA schemes worn items can be painted with.</summary>
    public IReadOnlyList<Entry> Colours { get; }

    /// <summary>The editor's idle stances (their id is the ANIMATION record name), in data order.</summary>
    public IReadOnlyList<Entry> Poses { get; }

    /// <summary>
    /// An idle stance as the character editor lists them (the original's "same poses the editor shows"): an ANIMATION that is <c>idle</c>, not
    /// <c>disabled</c>, of no <c>weather type</c>, not on the <c>overlay</c> layer, with no weapon in either hand and not <c>crouched</c>.
    /// </summary>
    static bool IsIdleStance(GameRecord a) =>
        a.GetBool("idle") && !a.GetBool("disabled") && a.GetInt("weather type") < 1 && a.GetString("layer", "") != "overlay"
        && a.GetInt("has weapon L") == 0 && a.GetInt("has weapon R") == 0 && !a.GetBool("crouched");

    /// <summary>The entries whose name or id holds <paramref name="search"/> (any case), at most <paramref name="max"/>, and how many matched.</summary>
    public static (List<Entry> Shown, int Found) Search(IEnumerable<Entry> entries, string search, int max)
    {
        string s = search.Trim();
        var shown = new List<Entry>();
        int found = 0;
        foreach (var e in entries)
        {
            if (s.Length > 0 && !e.Name.Contains(s, StringComparison.OrdinalIgnoreCase) && !e.Id.Contains(s, StringComparison.OrdinalIgnoreCase))
                continue;

            if (found++ < max) shown.Add(e);
        }

        return (shown, found);
    }

    public string NameOf(IEnumerable<Entry> list, string? id) => id is null ? "" : list.FirstOrDefault(e => e.Id == id)?.Name ?? id;

    static IEnumerable<GameRecord> Records(GameDatabase db, FcsRecordType[] types) =>
        types.SelectMany(db.OfType).Where(r => r.Name.Trim().Length > 0);

    static IEnumerable<Entry> Sorted(IEnumerable<Entry> entries) =>
        entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal);

    static string KindOf(FcsRecordType type) => type switch
    {
        FcsRecordType.Weapon => "Weapons",
        FcsRecordType.Armour => "Armour",
        FcsRecordType.Crossbow => "Crossbows",
        FcsRecordType.LimbReplacement => "Limbs",
        FcsRecordType.Container => "Backpacks",
        _ => "Items",
    };

    /// <summary>A manufacturer's own grades: its <c>weapon models</c> list (model, level in value 0), lowest level first.</summary>
    static IReadOnlyList<Grade> GradesOf(GameDatabase db, string maker) =>
        [.. (db.Find(maker)?.GetReferences("weapon models") ?? [])
            .Select(r => (Record: db.Find(r.TargetStringId), r.Values.Value0))
            .Where(m => m.Record is not null)
            .Select(m => new Grade(m.Record!.StringId, m.Record.Name, m.Value0))
            .OrderBy(g => g.Level)];

    static bool IsBlood(string name) => BloodWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
}
