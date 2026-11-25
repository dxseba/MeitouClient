using Meitou.Data.Gameplay;
using Meitou.Simulation;

namespace Meitou.DynamicWasteland;

/// <summary>
/// The political characters and governments (the original's [Government], [Health], [Dynasties], [Ambitions] and Governors): a leader per
/// nation and a governor per held town, aged by the game's days; a government type per nation with elections, reforms, coups, succession and
/// civil wars; diplomatic marriages; goat ministers.
/// </summary>
sealed partial class WorldSim
{
    static readonly string[] Governments = ["monarchy", "theocracy", "republic", "oligarchy", "dictatorship", "tribal_confederation"];

    static readonly string[] Goals = ["crown", "wealth", "faith", "independence", "revenge"];

    static readonly string[] Secrets =
    [
        "secretly follows a faith other than the state religion", "quietly funds rebel groups", "ordered a murder disguised as an accident",
        "skims the treasury", "keeps a secret lover", "took bribes from a rival nation", "sold slaves against the law",
    ];

    static readonly string[] First =
    [
        "Ard", "Bel", "Cor", "Dra", "Esk", "Fen", "Gar", "Hal", "Ira", "Jor", "Kai", "Lun", "Mor", "Nar", "Osk", "Pel", "Rha", "Sev", "Tor", "Vel",
    ];

    static readonly string[] Last = ["an", "ek", "ira", "os", "un", "ath", "el", "ix", "or", "ys"];

    /// <summary>Game days per virtual year (the original's <c>[Health] YearDays</c> 40).</summary>
    const int YearDays = 40;

    /// <summary>A new political character of <paramref name="nation"/>.</summary>
    Person NewPerson(World world, string nation, string role, int site = -1)
    {
        var p = new Person
        {
            Id = _state.NextId++,
            Name = First[Below(world, First.Length, 30)] + Last[Below(world, Last.Length, 31)] + " " + First[Below(world, First.Length, 32)] +
                Last[Below(world, Last.Length, 33)].ToLowerInvariant(),
            Role = role,
            Nation = nation,
            Site = site,
            Age = 25 + Below(world, 35, 34),
            Loyalty = 30 + Below(world, 60, 35),
            Ambition = 20 + Below(world, 70, 36),
            Competence = 20 + Below(world, 70, 37),
            Corruption = Below(world, 50, 38),
            Goal = Goals[Below(world, Goals.Length, 39)],
            Opinion = Below(world, 41, 40) - 20,
        };
        if (Chance(world, 60, 41)) p.Secret = Secrets[Below(world, Secrets.Length, 42)];
        _state.People.Add(p);
        return p;
    }

    /// <summary>Gives every nation a government and a leader and every held town a governor (Governors=1).</summary>
    void EnsurePeople(World world)
    {
        foreach (var n in _state.Nations)
        {
            if (n.Leader < 0 || _state.FindPerson(n.Leader) is not { Alive: true })
            {
                if (n.Government == "monarchy" && n.Leader < 0) n.Government = Governments[Below(world, Governments.Length, 43)];
                n.Leader = NewPerson(world, n.Faction, "leader").Id;
            }

            foreach (int site in TownsOf(world, n.Faction))
            {
                var town = Town(world, site);
                if (town.Governor >= 0 && _state.FindPerson(town.Governor) is { Alive: true } g && g.Nation == n.Faction) continue;

                town.Governor = NewPerson(world, n.Faction, "governor", site).Id;
            }
        }
    }

    /// <summary>
    /// A step of politics: ageing and natural death (succession), elections of republics every 120 days, reforms after their cooldown (100
    /// days), coups under low stability, civil wars, governors' independence, diplomatic marriages and goat ministers.
    /// </summary>
    void Politics(World world)
    {
        double days = _s.TickHours / 24.0;
        foreach (var p in _state.People.Where(p => p.Alive).ToList()) Age(world, p, days);

        foreach (var n in _state.Nations.ToList())
        {
            if (_state.FindPerson(n.Leader) is not { } leader) continue;

            if (n.Government == "republic" && world.GameHours - n.LastElection > 120 * 24) Election(world, n, leader);
            else if (n.Stability < 25 && Chance(world, 4, 44)) Coup(world, n, leader);
            else if (world.GameHours - n.LastReform > 100 * 24 && n.Stability > 55 && Chance(world, 2, 45)) Reform(world, n);

            if (n.Stability < 15 && TownsOf(world, n.Faction).Count >= 3 && !n.Pooled && Chance(world, 10, 46)) CivilWar(world, n);
            if (Chance(world, 1, 47)) Say(world, Fmt("{0} was appointed {1} of {2} in {3}.", "A goat", tr("minister"), Name(world, n.Faction),
                TownName(world, TownsOf(world, n.Faction) is [var c, ..] ? c : 0)), popup: false);
        }

        Marriages(world);
        Independence(world);
        _state.People.RemoveAll(p => !p.Alive && p.Bond <= 0 && !p.SecretKnown);
    }

    /// <summary>Ageing: a year each YearDays days; past 60 a yearly chance of death of (age - 55) %.</summary>
    void Age(World world, Person p, double days)
    {
        if (Below(world, YearDays * 100, 48) >= (int)(days * 100)) return;

        p.Age++;
        if (p.Age < 60 || !Chance(world, p.Age - 55, 49)) return;

        p.Alive = false;
        Say(world, Fmt("{0} dies in {1}", p.Name, Name(world, p.Nation)), popup: p.Role == "leader");
        if (p.Role == "leader" && _state.Find(p.Nation) is { } n) Succession(world, n, p);
    }

    /// <summary>A new leader: the spouse's or a relative's house first (dynasties), else a new name; stability falls by 5.</summary>
    void Succession(World world, Nation n, Person old)
    {
        var heir = NewPerson(world, n.Faction, "leader");
        if (old.Spouse >= 0) heir.Name = old.Name.Split(' ')[0] + " " + heir.Name.Split(' ')[^1];
        n.Leader = heir.Id;
        n.Stability -= 5;
        Say(world, Fmt("{0} crowned in {1}", heir.Name, Name(world, n.Faction)), popup: true);
    }

    void Election(World world, Nation n, Person leader)
    {
        n.LastElection = world.GameHours;
        int share = 35 + Below(world, 40, 50);
        if (share >= 50)
        {
            Say(world, Fmt("{0} elected in {1}", leader.Name, Name(world, n.Faction)), popup: false);
            n.Legitimacy = Math.Min(100, n.Legitimacy + 5);
            return;
        }

        leader.Role = "exile";
        leader.Alive = false;
        var winner = NewPerson(world, n.Faction, "leader");
        n.Leader = winner.Id;
        Say(world, Fmt("{0} won with {1}% of the vote and took power in {2}. Tally: {3}.", winner.Name, 100 - share, Name(world, n.Faction),
            $"{100 - share}-{share}"), popup: true);
    }

    void Coup(World world, Nation n, Person leader)
    {
        var plotter = NewPerson(world, n.Faction, "leader");
        if (Below(world, 100, 51) < 40 + plotter.Ambition / 3 - n.Legitimacy / 4)
        {
            leader.Alive = false;
            n.Leader = plotter.Id;
            n.Government = "dictatorship";
            n.Stability = Math.Max(0, n.Stability - 10);
            n.Legitimacy = 30;
            Say(world, Fmt("{0} seizes power in {1}", plotter.Name, Name(world, n.Faction)), popup: true);
        }
        else
        {
            plotter.Alive = false;
            Say(world, Fmt("{0} attempted a failed coup in {1} and was stripped of military authority for treason.", plotter.Name,
                Name(world, n.Faction)), popup: false);
        }
    }

    void Reform(World world, Nation n)
    {
        string from = n.Government;
        n.Government = Governments[Below(world, Governments.Length, 52)];
        n.LastReform = world.GameHours;
        if (n.Government == from) return;

        Say(world, Fmt("{0} government changed from {1} to {2}.", Name(world, n.Faction), tr(from), tr(n.Government)), popup: false);
    }

    /// <summary>
    /// A civil war: the nation's rebel faction (made in the data phase) seizes a third of its towns and is at war with it (the original's
    /// "{0} entered a civil war. {1} formed the {2} and seized {3}.").
    /// </summary>
    void CivilWar(World world, Nation n)
    {
        string rebels = TownVersions.Rebels(n.Faction);
        if (Index(world, rebels) < 0 || _state.Find(rebels) is not null) return;

        var towns = TownsOf(world, n.Faction);
        var seized = towns.Skip(1).Where((_, i) => i % 3 == 0).ToList();
        if (seized.Count == 0) return;

        var nation = new Nation { Faction = rebels, Pooled = true, Origin = n.Faction, Stability = 50, Military = n.Military / 2, Treasury = 4000,
            Aggression = 70, Government = "tribal_confederation", Religion = n.Religion };
        _state.Nations.Add(nation);
        var chief = NewPerson(world, rebels, "leader");
        nation.Leader = chief.Id;
        n.Military -= n.Military / 3;
        foreach (int site in seized) _state.Pending.Add(new Conquest { Site = site, Faction = rebels });
        StartWar(world, rebels, n.Faction);
        Say(world, Fmt("{0} entered a civil war. {1} formed the {2} and seized {3}.", Name(world, n.Faction), chief.Name, Name(world, rebels),
            string.Join(", ", seized.Select(s => TownName(world, s)))), popup: true);
    }

    /// <summary>
    /// A disloyal governor bent on independence (loyalty below 20) breaks a village away as a free city (DynamicIndependence, FreeCities): an
    /// emergent faction takes it; each step loyalty drifts down with corruption and up with the nation's stability.
    /// </summary>
    void Independence(World world)
    {
        foreach (var g in _state.People.Where(p => p.Alive && p.Role == "governor" && p.Site >= 0).ToList())
        {
            int nationStability = _state.Find(g.Nation)?.Stability ?? 50;
            g.Loyalty = Math.Clamp(g.Loyalty + (nationStability >= 50 ? 1 : -1) - (g.Corruption > 40 ? 1 : 0), 0, 100);
            if (g.Loyalty >= 20 || g.Goal != "independence" || !Chance(world, 5, 56)) continue;

            var data = Pop(world)!.Data;
            if (data.Sites[g.Site].Town.Type != TownType.Village) continue;

            int before = _state.Nations.Count;
            Emerge(world, g.Site, "free city");
            if (_state.Nations.Count == before) continue;

            var city = _state.Nations[^1];
            city.Title = Fmt("Free City of {0}", TownName(world, g.Site));
            _state.Pending.Add(new Conquest { Site = g.Site, Faction = city.Faction });
            g.Nation = city.Faction;
            g.Role = "leader";
            city.Leader = g.Id;
        }
    }

    /// <summary>Two leaders of nations at peace with good relations may marry their houses (relations +10 both ways; the original's claim).</summary>
    void Marriages(World world)
    {
        if (!Chance(world, 3, 53) || _state.Nations.Count < 2) return;

        var a = _state.Nations[Below(world, _state.Nations.Count, 54)];
        var b = _state.Nations[Below(world, _state.Nations.Count, 55)];
        if (a == b || _state.WarOf(a.Faction, b.Faction) is not null) return;
        if (_state.FindPerson(a.Leader) is not { Spouse: < 0 } la || _state.FindPerson(b.Leader) is not { Spouse: < 0 } lb) return;

        var relations = Relations(world);
        int ia = Index(world, a.Faction), ib = Index(world, b.Faction);
        if (relations.Get(ia, ib) < 0) return;

        (la.Spouse, lb.Spouse) = (lb.Id, la.Id);
        Quiet(() =>
        {
            relations.Add(ia, ib, 10);
            relations.Add(ib, ia, 10);
        });
        Say(world, Fmt("{0} of {1} marries into {2} of {3}. Blood opens a claim.", la.Name, Name(world, a.Faction), lb.Name, Name(world, b.Faction)),
            popup: false);
    }
}
