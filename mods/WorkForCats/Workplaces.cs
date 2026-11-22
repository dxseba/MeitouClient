using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Construction;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.WorkForCats;

/// <summary>
/// A workplace a character can be employed at: its building key, name, position, how near the worker stands (its functionality's
/// <c>use range</c>), the work clip and functionality, the skill it trains and pays by, the owning faction and whether that is the player's.
/// </summary>
internal sealed record Workplace(string Key, string Name, Vector3 Position, float Range, string Clip, string? Functionality, StatsEnumerated Stat,
    int Owner, bool Own);

/// <summary>
/// A building key's workplace, and why it offers no employment (null: it does).
/// </summary>
internal readonly record struct Lookup(Workplace? Place, string? Refusal)
{
    public Workplace? Eligible => Refusal is null ? Place : null;
}

/// <summary>
/// Finds the workplace a building key names: a building of the player's or another faction's registry (<c>World.Buildings</c>), a level building
/// of a town, or an object of a town building's layout (a forge or bench inside a smithy). Its <see cref="MachineData"/> decides whether it
/// employs: operator slots, a <c>stat used</c>, an owner, not a natural deposit. Sim thread only.
/// </summary>
internal sealed class WorkplaceFinder(PopulationData data)
{
    /// <summary>
    /// What a key names before it is judged: the BUILDING record, its position, its owner and whether it stands.
    /// </summary>
    readonly record struct Found(string Record, Vector3 Position, int Owner, bool Standing);

    /// <summary>
    /// A level building or layout object with the town it stands in.
    /// </summary>
    readonly record struct LevelEntry(TownBuilding House, TownLayoutBuilding? Item, int Site);

    readonly Dictionary<string, MachineData?> _machines = new(StringComparer.Ordinal);
    IReadOnlyList<TownSite>? _indexed;
    int _revision = -1;
    Dictionary<string, LevelEntry> _level = new(StringComparer.Ordinal);

    /// <summary>
    /// The workplace of <paramref name="key"/> judged by <paramref name="rules"/>; no place for a key that names no building with a functionality.
    /// </summary>
    public Lookup Find(World world, string? key, WorkRules rules)
    {
        if (key is null || Locate(world, key) is not { } found) return default;
        if (Machine(found.Record) is not { Functionality: not null } machine) return default;

        bool own = found.Owner >= 0 && found.Owner == world.Player.Faction;
        var place = new Workplace(key, machine.Name, found.Position, machine.UseRange, machine.Animation, machine.Functionality,
            (StatsEnumerated)machine.Stat, found.Owner, own);
        return new Lookup(place, Refusal(machine, found, own, rules));
    }

    /// <summary>
    /// Workplaces the original refuses by record (its <c>UnsupportedWorkplace</c>): Brewing Booze Barrels, Grain Silo, Iron Refinery, Ore
    /// Processor, Iron Resource, Copper Resource, Iron Rock.
    /// </summary>
    static readonly HashSet<string> Unsupported = new(StringComparer.Ordinal)
    {
        "5354-furniture_otto.mod", "1930-gamedata.base", "42167-gamedata.base", "1512-gamedata.base", "42248-gamedata.base", "42249-gamedata.base",
        "14521-rebirth.mod",
    };

    static string? Refusal(MachineData machine, Found found, bool own, WorkRules rules)
    {
        if (!found.Standing) return "This building is not standing.";
        if (Unsupported.Contains(found.Record)) return "This workplace offers no employment.";
        if (machine.Function == MachineData.NaturalMineFunction) return "Natural deposits offer no employment.";
        if (machine.IsAutomatic) return "Fully automatic: it has no operator slots.";
        if (machine.Stat <= 0 || machine.Function == MachineData.TrainingFunction || machine.Stat == (int)StatsEnumerated.Turrets)
            return "No work skill is used here.";

        if (found.Owner < 0) return "Nobody owns this workplace.";
        if (own && !rules.AllowOwn) return "This is your own workplace.";
        return null;
    }

    Found? Locate(World world, string key) => Built(world, key) ?? Level(key);

    /// <summary>
    /// A building of the registry (the player's, or one another faction owns there).
    /// </summary>
    static Found? Built(World world, string key)
    {
        if (world.Buildings.Find(key) is not { } site) return null;

        bool standing = site.Complete && !site.Destroyed && !site.Ruined;
        return new Found(site.Record, site.Position, site.Owner, standing);
    }

    /// <summary>
    /// A level building or one of its layout objects; owned by the building's <c>owner faction ID</c>, else its town's faction.
    /// </summary>
    Found? Level(string key)
    {
        if (!LevelIndex().TryGetValue(key, out var entry)) return null;

        int owner = OwnerOf(entry);
        var house = entry.House;
        return entry.Item is { } item
            ? new Found(item.RecordId, item.Position, owner, !house.Destroyed)
            : new Found(house.BuildingId, house.Position, owner, !house.Destroyed);
    }

    int OwnerOf(LevelEntry entry)
    {
        if (entry.House.OwnerFactionId is { } id && data.FactionIndex(id) is >= 0 and var named) return named;

        return entry.Site >= 0 && entry.Site < data.SiteFactions.Count ? data.SiteFactions[entry.Site] : -1;
    }

    /// <summary>
    /// The level buildings and their layout objects by key, rebuilt when the towns are.
    /// </summary>
    Dictionary<string, LevelEntry> LevelIndex()
    {
        if (ReferenceEquals(_indexed, data.Sites) && _revision == data.BuildingsRevision) return _level;

        var index = new Dictionary<string, LevelEntry>(StringComparer.Ordinal);
        foreach (var site in data.Sites)
        {
            foreach (var house in site.Buildings) Add(index, house, site.Index);
        }

        (_indexed, _revision, _level) = (data.Sites, data.BuildingsRevision, index);
        return index;
    }

    static void Add(Dictionary<string, LevelEntry> index, TownBuilding house, int site)
    {
        index.TryAdd(house.Key, new LevelEntry(house, null, site));
        foreach (var item in house.LayoutBuildings) index.TryAdd(item.Id, new LevelEntry(house, item, site));
    }

    MachineData? Machine(string record)
    {
        if (_machines.TryGetValue(record, out var known)) return known;

        var machine = data.Db.Find(record) is { } building ? MachineData.From(data.Db, building) : null;
        _machines[record] = machine;
        return machine;
    }
}
