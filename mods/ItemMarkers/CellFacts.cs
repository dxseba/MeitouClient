using System.Linq.Expressions;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;
using Meitou.Simulation.Trading;

namespace Meitou.ItemMarkers;

/// <summary>
/// An item cell's <see cref="ItemFacts"/>, from the tooltip the cell carries (the internal <c>Meitou.Game.Ui.TooltipRuntime.ItemTip</c>, read through
/// compiled property getters) and the snapshot on screen; and the blueprint and map knowledge behind the eye. Host thread only.
/// </summary>
sealed class CellFacts
{
    readonly Func<object, string> _record, _material, _company;
    readonly Func<object, int> _level, _stolenFrom, _index;
    readonly Func<object, TradeItemView?> _trade;
    readonly Func<object, bool> _goods;
    readonly Func<object, ItemOwner?> _owner;

    readonly GameDatabase _data;
    readonly PopulationData? _population;
    readonly Dictionary<string, int[]?> _mapSites = new(StringComparer.Ordinal);

    WorldSnapshot? _indexed;
    Dictionary<CharacterId, CharacterSnapshot> _characters = [];

    CellFacts(Type tip, GameDatabase data, PopulationData? population)
    {
        _record = Getter<string>(tip, "Record");
        _material = Getter<string>(tip, "Material");
        _company = Getter<string>(tip, "Company");
        _level = Getter<int>(tip, "Level");
        _stolenFrom = Getter<int>(tip, "StolenFrom");
        _index = Getter<int>(tip, "Index");
        _trade = Getter<TradeItemView?>(tip, "Trade");
        _goods = Getter<bool>(tip, "Goods");
        _owner = Getter<ItemOwner?>(tip, "Owner");
        (_data, _population) = (data, population);
    }

    /// <summary>The reader; null (logged) when the tip type is missing, so no marks show.</summary>
    public static CellFacts? Create(GameDatabase data, PopulationData? population, Action<string> log)
    {
        if (AccessTools.TypeByName("Meitou.Game.Ui.TooltipRuntime") is not { } runtime || AccessTools.Inner(runtime, "ItemTip") is not { } tip)
        {
            log("TooltipRuntime.ItemTip not found: no marks");
            return null;
        }

        return new CellFacts(tip, data, population);
    }

    static Func<object, T> Getter<T>(Type type, string property)
    {
        var p = Expression.Parameter(typeof(object));
        var read = Expression.Property(Expression.Convert(p, type), property);
        return Expression.Lambda<Func<object, T>>(Expression.Convert(read, typeof(T)), p).Compile();
    }

    /// <summary>The facts of the cell whose tooltip is <paramref name="tip"/>, against <paramref name="snapshot"/>.</summary>
    public ItemFacts Of(object tip, WorldSnapshot? snapshot)
    {
        var trade = _trade(tip);
        bool goods = _goods(tip);
        var change = trade is { Buyback: true } ? goods ? TradeChange.Bought : TradeChange.Sold : TradeChange.None;
        bool uniform = goods || trade is null ? UniformBook.IsUniform(View(tip, snapshot)) : UniformBook.IsUniform(trade);
        return new(_record(tip), _material(tip), _company(tip), _level(tip), _stolenFrom(tip) >= 0, uniform, change);
    }

    /// <summary>The snapshot's view of the cell's item: its holder's (a character's or a storage's) item at its index; null when not found.</summary>
    object? View(object tip, WorldSnapshot? snapshot)
    {
        if (snapshot is null || _owner(tip) is not { } owner) return null;

        int index = _index(tip);
        var grid = owner.Holder switch
        {
            ItemHolder.Character => Character(snapshot, owner.Character)?.ItemGrid,
            ItemHolder.Site => snapshot.Machines.FirstOrDefault(m => m.Site == owner.Site)?.Storage,
            _ => null,
        };
        return grid?.Items.FirstOrDefault(i => i.Index == index);
    }

    CharacterSnapshot? Character(WorldSnapshot snapshot, CharacterId id)
    {
        if (!ReferenceEquals(snapshot, _indexed))
        {
            _characters = [];
            foreach (var c in snapshot.Characters) _characters.TryAdd(c.Id, c);
            _indexed = snapshot;
        }

        return _characters.GetValueOrDefault(id);
    }

    /// <summary>A blueprint whose research the player knows (finished, or its requirements met: the blueprint tooltip's learned state).</summary>
    public bool LearnedBlueprint(in ItemFacts item, WorldSnapshot? snapshot) =>
        snapshot is not null && item.Company.Length > 0 && _data.Find(item.Company)?.Type == FcsRecordType.Research
        && snapshot.Research.Known.Contains(item.Company);

    /// <summary>
    /// A MAP_ITEM all of whose <c>towns</c> (1 to 256, each a TOWN record placed in the world) are discovered (DLL <c>FUN_18000b120</c>); a map
    /// with none, too many, or one that is not a placed town is not.
    /// </summary>
    public bool KnownMap(in ItemFacts item, WorldSnapshot? snapshot)
    {
        if (snapshot is null || _data.Find(item.Record) is not { Type: FcsRecordType.MapItem } map) return false;
        if (!_mapSites.TryGetValue(item.Record, out int[]? sites))
        {
            sites = MapSites(map);
            _mapSites[item.Record] = sites;
        }

        return sites is not null && sites.All(site => Discovered(snapshot, site));
    }

    static bool Discovered(WorldSnapshot snapshot, int site) =>
        site >= 0 && site < snapshot.Towns.Count && (snapshot.Towns[site] & TownKnowledge.Discovered) != 0;

    /// <summary>
    /// For each listed town, a discovered placed site stands for it: the indices are the sites of each town (a town placed several times takes its
    /// first). Null for a map that does not qualify.
    /// </summary>
    int[]? MapSites(GameRecord map)
    {
        var links = map.GetReferences("towns").ToList();
        if (links.Count is 0 or > 256 || _population is null) return null;

        var sites = new int[links.Count];
        for (int i = 0; i < links.Count; i++)
        {
            string town = links[i].TargetStringId;
            if (_data.Find(town)?.Type != FcsRecordType.Town) return null;

            sites[i] = _population.Sites.FirstOrDefault(s => s.Town.Id == town)?.Index ?? -1;
            if (sites[i] < 0) return null;
        }

        return sites;
    }
}
