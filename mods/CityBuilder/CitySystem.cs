using Meitou.Data;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Population;

namespace Meitou.CityBuilder;

/// <summary>
/// Runs the settlement in the tick: finds the player's finished CITY buildings and furnishes them, brings their buyers, innkeepers and guards,
/// applies the window's choices (sell, refuse, hire, shop kind) and pays the morning's tavern shares and shop taxes. Its people are squads of the
/// player's faction the AI runs (independent, uncounted, homed at their building; <see cref="CitySystem"/> People). Saved and hashed.
/// </summary>
internal sealed partial class CitySystem : ITickSystem, IStateHashed, IWorldReady, IPausedCommandHandler
{
    /// <summary>Ticks between sweeps (5 s of game time at 30 ticks a second).</summary>
    const int Cadence = 150;

    /// <summary>The morning hour after which the night's tavern share and the shops' tax are paid (City.dll: 7.0).</summary>
    const double Morning = 7;

    /// <summary>The evening hour the tavern's night begins (City.dll: 19.0); the share is paid only before it.</summary>
    const double Evening = 19;

    readonly string _modId;
    readonly GameDatabase _db;
    readonly CityCatalog _catalog;
    readonly CityLayouts _layouts;
    readonly CitySettings _settings;
    readonly Action<SimCommand> _enqueue;
    readonly Func<string, string> _tr;
    readonly RngPurpose _purpose;
    readonly PopulationData _population;
    readonly SortedDictionary<string, CityPlace> _places = new(StringComparer.Ordinal);
    CityRules _rules;
    ulong _rolls;

    public CitySystem(string modId, GameDatabase db, CityCatalog catalog, CityLayouts layouts, CitySettings settings, Action<SimCommand> enqueue,
        Func<string, string> tr, RngPurpose purpose, PopulationData population)
    {
        _modId = modId;
        _db = db;
        _catalog = catalog;
        _layouts = layouts;
        _settings = settings;
        _enqueue = enqueue;
        _tr = tr;
        _purpose = purpose;
        _population = population;
        _rules = settings.Rules();
    }

    /// <summary>What the window shows, published every sweep (host thread reads it).</summary>
    public volatile CityView View = CityView.Empty;

    public void Ready(World world, bool fromSave) => Publish(world);

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _rules = _settings.Rules();
        foreach (var command in commands)
        {
            switch (command)
            {
                case SellPlaceOrder sell: Sell(world, sell.Site); break;
                case RefusePlaceOrder refuse: Refuse(world, refuse.Site); break;
                case HireGuardsOrder hire: Hire(world, hire.Site, hire.Count); break;
                case ChooseShopOrder shop: ChooseShop(world, shop.Site, shop.Shop); break;
                default: continue;
            }

            Publish(world);
        }
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % Cadence != 0 || !world.Player.Exists) return;

        FindNewBuildings(world);
        DropGoneBuildings(world);
        Arrivals(world);
        PayMorning(world);
        TavernNights(world);
        PostGuards(world);
        WorkHours(world);
        Tidy(world);
        Publish(world);
    }

    #region Buildings

    /// <summary>
    /// A finished CITY building of the player's faction the mod does not know yet: furnished from its layout, its arrival drawn (a guard post is
    /// open at once).
    /// </summary>
    void FindNewBuildings(World world)
    {
        foreach (var site in world.Buildings.All.ToList())
        {
            if (site.Parent is not null || !site.Complete || site.Destroyed || site.Owner != world.Player.Faction) continue;
            if (_places.ContainsKey(site.Id) || _catalog.Find(site.Record) is not { } building) continue;

            var place = new CityPlace { Site = site.Id, Record = site.Record };
            _places[site.Id] = place;
            Start(world, place, building);
        }

        foreach (var place in _places.Values)
        {
            if (!place.Furnished && world.Buildings.Find(place.Site) is { } site && _catalog.Find(place.Record) is { } building
                && !HasPieces(world, place))
                Furnish(world, place, site, building);
        }
    }

    void Start(World world, CityPlace place, CityBuilding building)
    {
        if (building.Kind == CityKind.GuardPost)
        {
            place.State = PlaceState.Open;
            return;
        }

        if (building.Kind == CityKind.TradeHouse) place.Shop = CityCatalog.ShopKinds[Rng.Int(Roll(world), CityCatalog.ShopKinds.Length)];

        var hours = building.Kind switch
        {
            CityKind.Residence => _rules.HomeHours,
            CityKind.TradeHouse => _rules.TradeHours,
            _ => _rules.InnkeeperHours,
        };
        place.ArriveAt = world.GameHours + Between(world, hours.Min, hours.Max);
    }

    /// <summary>A building gone (dismantled, pulled down): its people leave the faction and the mod forgets it.</summary>
    void DropGoneBuildings(World world)
    {
        foreach (var place in _places.Values.ToList())
        {
            if (world.Buildings.Find(place.Site) is { Destroyed: false } site && site.Owner == world.Player.Faction) continue;

            bool hadPeople = SendAway(world, place.Site, role: null);
            _places.Remove(place.Site);
            world.Post(_tr("Building pulled down") + (hadPeople ? ": " + _tr("Its people have left.") : ""));
        }
    }

    Data.Gameplay.SquadTemplate? TemplateOf(string id) => _db.Find(id) is { } r ? Data.Gameplay.SquadTemplate.From(r) : null;

    #endregion

    #region Rolls

    /// <summary>The next roll of the mod's own stream: from the world seed and a counter that is saved.</summary>
    ulong Roll(World world) => Rng.Hash(world.Seed, 0xC17, _purpose, _rolls++);

    double Between(World world, int min, int max) => min + Rng.Float(Roll(world)) * Math.Max(0, max - min);

    static int Day(double hours) => (int)Math.Floor(hours / 24);

    static double HourOfDay(double hours) => hours - Day(hours) * 24.0;

    #endregion

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_rolls);
        hasher.Add(_places.Count);
        foreach (var place in _places.Values) place.Hash(ref hasher);
        HashTidy(ref hasher);
    }
}
