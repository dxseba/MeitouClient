using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;

namespace Meitou.DynamicWasteland;

/// <summary>
/// World crises (the original's [Crises]: at most one at a time, MinDaysBetween 90, StartChance 3 % a day), mercenary companies
/// ([Mercenaries]: contracts, unpaid desertion and side switching) and emergent factions ([EmergentFactions]: MaxActive 12, CooldownDays 14,
/// SpawnChance 8 %).
/// </summary>
sealed partial class WorldSim
{
    static readonly string[] CrisisKinds =
    [
        "drought", "plague", "exodus", "trade boom", "mine collapse", "slave revolt", "beasts", "ruin find", "charismatic tribune", "black famine",
    ];

    #region Crises

    void Crises(World world)
    {
        if (_state.Crisis is { } c)
        {
            if (world.GameHours >= c.Until) EndCrisis(world, c);
            else CrisisDay(world, c);
            return;
        }

        if (world.GameHours - _state.LastCrisisEnd < 90 * 24 || _state.Nations.Count == 0 || !Chance(world, 3, 80)) return;

        var n = _state.Nations[Below(world, _state.Nations.Count, 81)];
        var towns = TownsOf(world, n.Faction);
        if (towns.Count == 0) return;

        var crisis = new Crisis
        {
            Kind = CrisisKinds[Below(world, CrisisKinds.Length, 82)], Nation = n.Faction, Site = towns[Below(world, towns.Count, 83)],
            Until = world.GameHours + (20 + Below(world, 21, 84)) * 24,
        };
        _state.Crisis = crisis;
        Say(world, Fmt("{0} begins", Fmt(CrisisTitle(crisis.Kind), TownName(world, crisis.Site), Name(world, n.Faction))), popup: true);
        StartCrisis(world, crisis, n);
    }

    static string CrisisTitle(string kind) => kind switch
    {
        "drought" => "The Great Drought of {1}",
        "plague" => "The Plague of {0}",
        "exodus" => "The Exodus from {0}",
        "trade boom" => "The Trade Boom of {0}",
        "mine collapse" => "The Collapse of the {0} mines",
        "slave revolt" => "The Slave Revolt of {0}",
        "beasts" => "The Beasts of {0}",
        "ruin find" => "The Ruin of {0}",
        "charismatic tribune" => "The Tribune of {0}",
        _ => "The Black Famine",
    };

    /// <summary>A crisis's one-off effect.</summary>
    void StartCrisis(World world, Crisis c, Nation n)
    {
        var t = Town(world, c.Site);
        switch (c.Kind)
        {
            case "black famine":
                c.Nation = "";
                break;
            case "exodus":
                t.Population -= t.Population / 5;
                break;
            case "ruin find":
                n.Treasury += 5000 + Below(world, 10001, 85);
                break;
            case "mine collapse":
                n.Economy = Math.Max(0, n.Economy - 10);
                break;
            case "slave revolt":
            case "charismatic tribune":
                t.Unrest = Math.Min(100, t.Unrest + 30);
                if (c.Kind == "slave revolt") Emerge(world, c.Site, "revolt");
                break;
            case "beasts":
                c.Platoon = Beasts(world, c.Site);
                break;
        }
    }

    /// <summary>A crisis's daily effect.</summary>
    void CrisisDay(World world, Crisis c)
    {
        var t = Town(world, c.Site);
        var n = _state.Find(c.Nation);
        switch (c.Kind)
        {
            case "plague":
                t.Population = Math.Max(10, t.Population - t.Population / 60);
                if (n is not null) n.Stability = Math.Max(0, n.Stability - 1);
                break;
            case "trade boom":
                if (n is not null) n.Treasury += 300;
                break;
            case "charismatic tribune":
                if (n is not null) n.Legitimacy = Math.Max(0, n.Legitimacy - 1);
                break;
        }
    }

    void EndCrisis(World world, Crisis c)
    {
        _state.Crisis = null;
        _state.LastCrisisEnd = world.GameHours;
        Say(world, Fmt("{0} comes to an end", Fmt(CrisisTitle(c.Kind), TownName(world, c.Site), c.Nation == "" ? "" : Name(world, c.Nation))),
            popup: false);
    }

    /// <summary>The Beasts crisis: a squad of the Wildlife faction's first beast template near the town (none when the data has none).</summary>
    int Beasts(World world, int site)
    {
        var pop = Pop(world)!;
        int wildlife = pop.Data.FactionIndex("3943-gamedata.base");
        if (wildlife < 0) return -1;

        var template = pop.Data.Factions[wildlife].Squads.Select(s => pop.Data.Db.Find(s.Id))
            .FirstOrDefault(r => r is { Type: FcsRecordType.SquadTemplate })?.StringId;
        if (template is null) return -1;

        var p = pop.Data.Sites[site].Position;
        return pop.SpawnPlatoon(world, wildlife, template, site, new Vector2(p.X, p.Z), 600, 1500);
    }

    #endregion

    #region Mercenaries

    static readonly string[] CompanyNames = ["Iron Jackals", "Dust Lions", "Red Cloaks", "Grey Fangs", "Sun Breakers", "Bone Crows"];

    /// <summary>
    /// Free companies form (up to four), nations at war hire free ones (NpcHiring) for their wage, wages are paid each step; a company unpaid
    /// three times leaves, and at war (SideSwitch) serves the enemy instead.
    /// </summary>
    void Mercenaries(World world)
    {
        if (_state.Companies.Count(c => !c.Player) < 4 && Chance(world, 10, 90)) FoundCompany(world, player: false);

        foreach (var c in _state.Companies.ToList())
        {
            if (c.Employer == "") Hire(world, c);
            else Pay(world, c);
        }
    }

    Company FoundCompany(World world, bool player)
    {
        var captain = NewPerson(world, "", "captain");
        var c = new Company
        {
            Id = _state.NextId++, Name = player ? tr("Your company") : CompanyNames[Below(world, CompanyNames.Length, 91)], Captain = captain.Id,
            Men = 10 + Below(world, 21, 92), Wage = 0, Player = player,
        };
        c.Wage = c.Men * 30;
        _state.Companies.Add(c);
        if (!player) Say(world, Fmt("{0} founds the {1}", captain.Name, c.Name), popup: false);
        return c;
    }

    void Hire(World world, Company c)
    {
        if (c.Player) return;

        var buyer = _state.Nations.Where(n => n.Treasury > c.Wage * 10 && _state.Wars.Any(w => w.Involves(n.Faction)))
            .OrderByDescending(n => n.Treasury).ThenBy(n => n.Faction, StringComparer.Ordinal).FirstOrDefault();
        if (buyer is null || !Chance(world, 30, 93)) return;

        c.Employer = buyer.Faction;
        c.Unpaid = 0;
        Say(world, Fmt("{0} hires {1}", Name(world, buyer.Faction), c.Name), popup: false);
    }

    void Pay(World world, Company c)
    {
        if (_state.Find(c.Employer) is not { } n)
        {
            c.Employer = "";
            return;
        }

        if (c.Player)
        {
            // The player's company hired out: the nation pays the player (the original's Hire it out to a nation on the WORLD tab).
            if (n.Treasury < c.Wage) return;

            n.Treasury -= c.Wage;
            world.Player.Money += c.Wage;
            return;
        }

        if (n.Treasury >= c.Wage)
        {
            n.Treasury -= c.Wage;
            c.Unpaid = 0;
            c.Fame++;
            return;
        }

        if (++c.Unpaid < 3) return;

        var enemy = _state.Wars.Where(w => w.Involves(n.Faction)).Select(w => w.Other(n.Faction)).FirstOrDefault();
        if (enemy is not null && Chance(world, 50, 94))
        {
            c.Employer = enemy;
            c.Unpaid = 0;
            Say(world, Fmt("{0} ({1}) left {2} unpaid and now fights for {3}.", c.Name, _state.FindPerson(c.Captain)?.Name ?? "", Name(world, n.Faction),
                Name(world, enemy)), popup: true);
            return;
        }

        c.Employer = "";
        Say(world, Fmt("{0} left {1}'s service: the treasury missed the contracts.", c.Name, Name(world, n.Faction)), popup: false);
    }

    /// <summary>The men of a nation's hired companies, a third of them as battle power.</summary>
    int Hired(Nation n) => _state.Companies.Where(c => c.Employer == n.Faction).Sum(c => c.Men) / 3;

    #endregion

    #region Emergent factions

    /// <summary>
    /// An emergent faction rises at a troubled town (famine, unrest, a cult, a revolt, a warlord): the first free pool faction, at war with the
    /// town's owner, with two warbands of its squads near the town. Cooldown 14 days, at most 12.
    /// </summary>
    void Emerge(World world, int site, string cause)
    {
        if (world.GameHours - _state.LastEmergent < 14 * 24) return;

        var pop = Pop(world)!;
        string? owner = pop.Data.FactionId(pop.Data.SiteFactions[site]);
        if (owner is null || _state.Find(owner) is null) return;

        string? slot = Enumerable.Range(0, TownVersions.EmergentCount).Select(TownVersions.Emergent)
            .FirstOrDefault(f => Index(world, f) >= 0 && _state.Find(f) is null);
        if (slot is null) return;

        _state.LastEmergent = world.GameHours;
        var n = new Nation { Faction = slot, Pooled = true, Origin = owner, Military = 30, Treasury = 1500, Aggression = 75, Stability = 40,
            Government = "tribal_confederation", Religion = Town(world, site).Religion };
        _state.Nations.Add(n);
        n.Leader = NewPerson(world, slot, "leader").Id;
        StartWar(world, slot, owner);
        Warbands(world, n, site, 2);
        Say(world, Fmt("{0} emerges within {1}", Name(world, slot), Name(world, owner)) + " (" + tr(cause) + ")", popup: true);
    }

    /// <summary>Spawns warbands of a pool faction's squads near a town, tagged for the faction.</summary>
    void Warbands(World world, Nation n, int site, int count)
    {
        var pop = Pop(world)!;
        int f = Index(world, n.Faction);
        var squads = pop.Data.Factions[f].Squads.Select(s => s.Id).Where(id => pop.Data.Db.Find(id) is not null).ToList();
        if (squads.Count == 0) return;

        var p = pop.Data.Sites[site].Position;
        for (int i = 0; i < count; i++)
        {
            int id = pop.SpawnPlatoon(world, f, squads[Below(world, squads.Count, 95)], site, new Vector2(p.X, p.Z), 800, 2000, 200);
            if (world.Platoons.Find(id) is { } platoon) platoon.ModTags[modId] = "warband:" + n.Faction;
        }
    }

    /// <summary>
    /// Emergent factions: famine or unrest above 60 raises one (SpawnChance 8 %); a pool faction without towns lives while its warbands do,
    /// and after ten days in the field takes the nearest village of its enemy.
    /// </summary>
    void EmergentFactions(World world)
    {
        var troubled = _state.Towns.Where(t => t.Unrest > 60).OrderByDescending(t => t.Unrest).ThenBy(t => t.Site).FirstOrDefault();
        if (troubled is not null && Chance(world, 8, 96)) Emerge(world, troubled.Site, troubled.Food == 0 ? "famine" : "unrest");

        foreach (var n in _state.Nations.Where(n => n.Pooled && n.Origin != "" && TownsOf(world, n.Faction).Count == 0).ToList())
        {
            bool alive = world.Platoons.All.Any(p => p.ModTags.TryGetValue(modId, out var tag) && tag == "warband:" + n.Faction);
            if (!alive && !_state.Pending.Any(c => c.Faction == n.Faction))
            {
                Dissolve(world, n);
                continue;
            }

            if (Chance(world, 5, 97) && Target(world, n.Origin, n.Faction, villages: true) is { } village)
                _state.Pending.Add(new Conquest { Site = village, Faction = n.Faction });
        }
    }

    void Dissolve(World world, Nation n)
    {
        foreach (var war in _state.Wars.Where(w => w.Involves(n.Faction)).ToList()) EndWar(world, war, truce: false);
        _state.Nations.Remove(n);
        Say(world, Fmt("{0} comes to an end", Name(world, n.Faction)), popup: false);
    }

    /// <summary>The origin's village nearest the pool faction's warbands (any of its villages when none are found).</summary>
    int? Target(World world, string origin, string faction, bool villages)
    {
        var data = Pop(world)!.Data;
        var band = world.Platoons.All.FirstOrDefault(p => p.ModTags.TryGetValue(modId, out var tag) && tag == "warband:" + faction);
        var sites = TownsOf(world, origin).Where(s => !villages || data.Sites[s].Town.Type == Meitou.Data.Gameplay.TownType.Village).ToList();
        if (sites.Count == 0) return null;

        if (band is null) return sites[0];

        return sites.OrderBy(s => Vector2.DistanceSquared(band.Position, new(data.Sites[s].Position.X, data.Sites[s].Position.Z))).First();
    }

    #endregion
}
