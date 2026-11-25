using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace Meitou.DynamicWasteland;

/// <summary>
/// Towns' people and food (the original's [Population] and [Food]): population grows with food and shrinks with famine and war; villages farm,
/// towns eat; food caravans run between a nation's towns (a real squad of the nation when it has a trader one), raided in war; misery drives
/// people to the nation's most prosperous town, war survivors flee as refugees.
/// </summary>
sealed partial class WorldSim
{
    /// <summary>Food a village grows and every 10 people eat a day (the original's "1 unit feeds 10 people/day").</summary>
    const int VillageHarvest = 40, TownHarvest = 8, PeoplePerFood = 10;

    /// <summary>A town's state, made on first use: town 300 people, village 80 (plus up to a third), 10 days of food, its owner's faith.</summary>
    TownState Town(World world, int site)
    {
        if (_state.FindTown(site) is { } t) return t;

        var data = Pop(world)!.Data;
        bool town = data.Sites[site].Town.Type == TownType.Town;
        int people = town ? 300 : 80;
        people += Below(world, people / 3 + 1, 60);
        string? owner = data.FactionId(data.SiteFactions[site]);
        t = new TownState
        {
            Site = site,
            Population = people,
            Food = people / PeoplePerFood * 10,
            Religion = owner is not null && _state.Find(owner) is { } n ? n.Religion : "secular",
        };
        _state.Towns.Add(t);
        return t;
    }

    /// <summary>A step of the towns: harvest and meals, growth or famine, unrest, migration.</summary>
    void Society(World world)
    {
        double days = _s.TickHours / 24.0;
        var data = Pop(world)!.Data;
        foreach (var n in _state.Nations)
        {
            var towns = TownsOf(world, n.Faction).Select(s => Town(world, s)).ToList();
            foreach (var t in towns) Feed(world, n, t, data.Sites[t.Site].Town.Type == TownType.Village, days);
            if (towns.Count > 1) Migrate(world, n, towns);
        }
    }

    void Feed(World world, Nation n, TownState t, bool village, double days)
    {
        float yield = _state.Crisis is { Kind: "drought" or "black famine" } c && (c.Nation == "" || c.Nation == n.Faction) ? 0.4f : 1;
        int harvest = (int)((village ? VillageHarvest : TownHarvest) * yield * days * Math.Max(1, t.Population / 50));
        int meals = (int)(t.Population / (float)PeoplePerFood * days);
        t.Food = Math.Max(0, t.Food + harvest - meals);
        if (t.Food > 0)
        {
            t.Population += Math.Max(0, (int)(t.Population * 0.004 * days));
            t.Unrest = Math.Max(0, t.Unrest - 1);
            return;
        }

        int starved = Math.Max(1, t.Population / 50);
        t.Population = Math.Max(10, t.Population - starved);
        t.Unrest = Math.Min(100, t.Unrest + 6);
        n.Stability = Math.Max(0, n.Stability - 1);
        n.Military = Math.Max(0, n.Military - 1);
        if (t.Unrest == 30) Say(world, Fmt("{0} settlements are starving. The roads fill with the displaced.", Name(world, n.Faction)), popup: true);
    }

    /// <summary>5 % of a miserable town's people (unrest 30 or more) leave for the nation's least troubled town with food.</summary>
    void Migrate(World world, Nation n, List<TownState> towns)
    {
        var best = towns.Where(t => t.Food > 0).OrderBy(t => t.Unrest).ThenBy(t => t.Site).FirstOrDefault();
        if (best is null) return;

        foreach (var t in towns.Where(t => t != best && t.Unrest >= 30))
        {
            int leaving = t.Population / 20;
            t.Population -= leaving;
            best.Population += leaving;
        }
    }

    /// <summary>A battle at a town kills 5 to 15 % of its people; a conquest sends a tenth of the rest as refugees to the old owner's town.</summary>
    void WarCasualties(World world, int site, string? oldOwner, bool conquered)
    {
        var t = Town(world, site);
        t.Population -= t.Population * (5 + Below(world, 11, 61)) / 100;
        t.Unrest = Math.Min(100, t.Unrest + 10);
        if (!conquered || oldOwner is null) return;

        int fleeing = t.Population / 10;
        if (TownsOf(world, oldOwner).Where(s => s != site).Select(s => Town(world, s)).OrderBy(x => x.Unrest).FirstOrDefault() is not { } refuge)
            return;

        t.Population -= fleeing;
        refuge.Population += fleeing;
        refuge.Refugees += fleeing;
    }

    #region Caravans

    /// <summary>
    /// Each nation sends a food caravan (CaravanCapacity) from its best-fed town to a hungry one (less than 5 days of food) when none is on the
    /// way; a real squad of its trader template when it has one walks the route (held and sent by the mod), else the food arrives after the
    /// route's length at a walker's pace.
    /// </summary>
    void Caravans(World world)
    {
        foreach (var n in _state.Nations)
        {
            if (_state.Caravans.Any(c => c.Nation == n.Faction)) continue;

            var towns = TownsOf(world, n.Faction).Select(s => Town(world, s)).ToList();
            var hungry = towns.Where(t => t.Food < t.Population / PeoplePerFood * 5).OrderBy(t => t.Food).ThenBy(t => t.Site).FirstOrDefault();
            var rich = towns.Where(t => t != hungry).OrderByDescending(t => t.Food).ThenBy(t => t.Site).FirstOrDefault();
            if (hungry is null || rich is null || rich.Food < 2 * CaravanCapacity) continue;

            rich.Food -= CaravanCapacity;
            SendCaravan(world, n, rich.Site, hungry.Site);
        }
    }

    const int CaravanCapacity = 200;

    void SendCaravan(World world, Nation n, int from, int to)
    {
        var pop = Pop(world)!;
        var a = pop.Data.Sites[from].Position;
        var b = pop.Data.Sites[to].Position;
        var caravan = new Caravan
        {
            Id = _state.NextId++, Nation = n.Faction, From = from, To = to, Food = CaravanCapacity,
            Due = world.GameHours + Vector3.Distance(a, b) / 2500 + 6,
        };
        if (TraderTemplate(world, n.Faction) is { } template)
        {
            int id = pop.SpawnPlatoon(world, Index(world, n.Faction), template, from, new Vector2(a.X, a.Z), 100, 400);
            if (world.Platoons.Find(id) is { } platoon)
            {
                platoon.ModTags[modId] = "caravan:" + caravan.Id;
                PopulationSystem.HoldPlatoon(world, id, modId, true);
                PopulationSystem.SendPlatoon(world, id, modId, new Vector2(b.X, b.Z));
                caravan.Platoon = id;
                caravan.Due = world.GameHours + 24 * 10;
            }
        }

        _state.Caravans.Add(caravan);
    }

    /// <summary>The nation's first squad template named for traders or caravans.</summary>
    static string? TraderTemplate(World world, string faction)
    {
        var data = Pop(world)!.Data;
        int f = data.FactionIndex(faction);
        return f < 0 ? null : data.Factions[f].Squads
            .Select(s => data.Db.Find(s.Id))
            .FirstOrDefault(r => r is { Type: FcsRecordType.SquadTemplate } &&
                (r.Name.Contains("Caravan", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Trader", StringComparison.OrdinalIgnoreCase)))
            ?.StringId;
    }

    /// <summary>Abstract caravans that are due arrive; a real one arrives when its squad does (<see cref="CaravanArrived"/>) or is lost.</summary>
    void WatchCaravans(World world)
    {
        foreach (var c in _state.Caravans.Where(c => c.Platoon < 0 && c.Due <= world.GameHours).ToList()) Deliver(world, c);
        foreach (var c in _state.Caravans.Where(c => c.Platoon >= 0 && (world.Platoons.Find(c.Platoon) is null || c.Due <= world.GameHours)).ToList())
        {
            _state.Caravans.Remove(c);
            if (world.Platoons.Find(c.Platoon) is not null) PopulationSystem.HoldPlatoon(world, c.Platoon, modId, false);
            else Say(world, Fmt("{0} lost a food caravan on the road.", Name(world, c.Nation)), popup: false);
        }
    }

    void CaravanArrived(World world, Platoon platoon)
    {
        if (_state.Caravans.FirstOrDefault(c => c.Platoon == platoon.Id) is not { } c) return;

        PopulationSystem.HoldPlatoon(world, platoon.Id, modId, false);
        Deliver(world, c);
        Pop(world)!.DespawnPlatoon(world, platoon.Id);
    }

    /// <summary>The food reaches its town; at war, raiders take 30 % of it with a chance of 25 % per enemy (WarInterdiction).</summary>
    void Deliver(World world, Caravan c)
    {
        _state.Caravans.Remove(c);
        int food = c.Food;
        int enemies = _state.Wars.Count(w => w.Involves(c.Nation));
        for (int i = 0; i < enemies; i++)
        {
            if (Chance(world, 25, 62)) food = food * 7 / 10;
        }

        Town(world, c.To).Food += food;
        if (food < c.Food) Say(world, Fmt("Raiders intercepted part of a {0} food caravan.", Name(world, c.Nation)), popup: false);
    }

    #endregion
}
