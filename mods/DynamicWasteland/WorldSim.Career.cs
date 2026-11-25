using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;

namespace Meitou.DynamicWasteland;

/// <summary>An order from the panel: an action and its target (a faction id or a number).</summary>
public sealed record DwOrder(string Action, string Target = "", int Id = -1) : SimCommand;

/// <summary>
/// The player's side (the original's career, story missions, [Intrigue], [Relationships] and ruler actions): enlisting with a nation and rising
/// in its ranks by merit, the mission board, spying on, exposing and blackmailing political characters, courting them, founding a faith or a
/// mercenary company, and the decrees of a high rank. Every change arrives as a <see cref="DwOrder"/> in the inputs step.
/// </summary>
sealed partial class WorldSim
{
    /// <summary>The ranks and the merit each needs.</summary>
    static readonly (string Title, int Merit)[] Ranks =
    [
        ("Recruit", 0), ("Soldier", 25), ("Sergeant", 75), ("Captain", 150), ("Commander", 300), ("Marshal", 600),
    ];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is DwOrder order && Pop(world) is not null && settings.Enabled) Order(world, order);
        }
    }

    void Order(World world, DwOrder o)
    {
        _s = DwSettingsCopy.From(settings);
        switch (o.Action)
        {
            case "enlist": Enlist(world, o.Target); break;
            case "resign": _state.Career.Patron = ""; break;
            case "accept": Accept(world, o.Id); break;
            case "spy": Spy(world, o.Id); break;
            case "expose": Expose(world, o.Id); break;
            case "blackmail": Blackmail(world, o.Id); break;
            case "flirt": Flirt(world, o.Id); break;
            case "found-faith": FoundFaith(world); break;
            case "found-company": PlayerCompany(world); break;
            case "hire-out": HireOut(world, o.Target); break;
            case "decree-war": DecreeWar(world, o.Target); break;
            case "decree-peace": DecreePeace(world, o.Target); break;
            case "levy": Levy(world); break;
        }

        View = Publish(world);
    }

    #region Career

    void Enlist(World world, string faction)
    {
        if (_state.Find(faction) is null || PlayerIndex(world) < 0) return;

        _state.Career.Patron = faction;
        Say(world, Fmt("{0} enlisted in the army of {1}.", tr("You"), Name(world, faction)), popup: true);
        ChangePlayerRelation(world, faction, 5);
    }

    /// <summary>Merit raises the rank; each step a patron pays 100 Cats a rank while it can.</summary>
    void CareerStep(World world)
    {
        var c = _state.Career;
        if (c.Patron == "" || _state.Find(c.Patron) is not { } patron) return;

        int rank = Ranks.Count(r => c.Merit >= r.Merit) - 1;
        if (rank > c.Rank)
        {
            c.Rank = rank;
            Say(world, Fmt("{0} was promoted to {1} in the army of {2}.", tr("You"), tr(Ranks[rank].Title), Name(world, patron.Faction)), popup: true);
        }

        long pay = 100L * c.Rank;
        if (pay > 0 && patron.Treasury >= pay)
        {
            patron.Treasury -= pay;
            world.Player.Money += pay;
        }
    }

    void Merit(int merit, int influence = 0)
    {
        _state.Career.Merit = Math.Max(0, _state.Career.Merit + merit);
        _state.Career.Influence = Math.Max(0, _state.Career.Influence + influence);
    }

    int PlayerIndex(World world) => world.Player.Faction;

    void ChangePlayerRelation(World world, string faction, float delta)
    {
        int f = Index(world, faction), p = PlayerIndex(world);
        if (f < 0 || p < 0) return;

        PlayerRelations.Add(world, Relations(world), f, p, delta, new RelationCause(RelationCauseType.Dialogue));
    }

    /// <summary>A commander or above decrees war on a nation for the patron (30 influence).</summary>
    void DecreeWar(World world, string target)
    {
        var c = _state.Career;
        if (c.Rank < 4 || c.Influence < 30 || _state.Find(c.Patron) is not { } a || _state.Find(target) is not { } b || a == b) return;
        if (_state.WarOf(a.Faction, b.Faction) is not null) return;

        c.Influence -= 30;
        if (_state.InTruce(a.Faction, b.Faction, world.GameHours))
        {
            _state.Truces.RemoveAll(p => p.Between(a.Faction, b.Faction));
            a.Legitimacy -= 10;
            Say(world, Fmt("{0} tore up its truce with {1} and marched to war. Even its own people call the ruler an oathbreaker.",
                Name(world, a.Faction), Name(world, b.Faction)), popup: true);
        }

        StartWar(world, a.Faction, b.Faction);
        Say(world, Fmt("{0} declared war on {1} ({2}).", Name(world, a.Faction), Name(world, b.Faction), tr("player decree")), popup: true);
        CallAllies(world, a, b);
    }

    void DecreePeace(World world, string target)
    {
        var c = _state.Career;
        if (c.Rank < 4 || c.Influence < 20 || _state.WarOf(c.Patron, target) is not { } war) return;

        c.Influence -= 20;
        EndWar(world, war, truce: true);
    }

    /// <summary>A levy (captain or above): 2000 Cats of the patron's treasury buy 5 military.</summary>
    void Levy(World world)
    {
        var c = _state.Career;
        if (c.Rank < 3 || _state.Find(c.Patron) is not { Treasury: >= 2000 } n) return;

        n.Treasury -= 2000;
        n.Military = Math.Min(100, n.Military + 5);
        Say(world, Fmt("{0} ordered a levy in {1} ({2} Cats).", tr("You"), Name(world, n.Faction), 2000), popup: false);
    }

    /// <summary>The player founds a faith (FoundReligionMerit 150, FoundReligionInfluence 50).</summary>
    void FoundFaith(World world)
    {
        var c = _state.Career;
        if (c.Faith != "" || c.Merit < 150 || c.Influence < 50) return;

        c.Influence -= 50;
        var f = new Faith { Id = "player" + _state.NextId++, Name = tr("Faith of the Wanderer"), Authority = 50, Founder = "player" };
        _state.Faiths.Add(f);
        c.Faith = f.Id;
        Say(world, Fmt("{0} founds {1}", tr("You"), f.Name), popup: true);
    }

    void PlayerCompany(World world)
    {
        if (_state.Career.Company >= 0) return;

        var company = FoundCompany(world, player: true);
        _state.Career.Company = company.Id;
        Say(world, Fmt("{0} founded a mercenary company. Hire it out to a nation on the WORLD tab.", tr("You")), popup: true);
    }

    void HireOut(World world, string faction)
    {
        if (_state.Companies.FirstOrDefault(c => c.Id == _state.Career.Company) is not { } company || _state.Find(faction) is null) return;

        company.Employer = faction;
        Say(world, Fmt("{0} now serves {1} ({2} Cats/tick).", company.Name, Name(world, faction), company.Wage), popup: false);
    }

    /// <summary>The player's kills: merit for the patron's enemies, a loss of it for the patron's own people.</summary>
    public void Died(World world, in CharacterDeath death)
    {
        if (death.KillerFaction < 0 || death.KillerFaction != PlayerIndex(world) || _state.Career.Patron == "" || Pop(world) is not { } pop) return;
        if (pop.Data.FactionId(death.Faction) is not { } victim) return;

        if (victim == _state.Career.Patron) Merit(-5);
        else if (_state.WarOf(_state.Career.Patron, victim) is not null) Merit(1);
    }

    #endregion

    #region Missions

    /// <summary>Keeps three offers on the board, each from a nation (a bounty, a tribute or famine relief), ten days to accept.</summary>
    void Missions(World world)
    {
        _state.Missions.RemoveAll(m => m.State == 0 && m.Expires <= world.GameHours);
        if (_state.Missions.Count(m => m.State == 0) >= 3 || _state.Nations.Count == 0) return;

        var giver = _state.Nations[Below(world, _state.Nations.Count, 100)];
        var towns = TownsOf(world, giver.Faction);
        if (towns.Count == 0) return;

        string kind = Below(world, 3, 101) switch { 0 => "bounty", 1 => "tribute", _ => "relief" };
        _state.Missions.Add(new Mission
        {
            Id = _state.NextId++, Kind = kind, Giver = giver.Faction, Site = towns[Below(world, towns.Count, 102)],
            Reward = kind == "bounty" ? 1500 + Below(world, 2501, 103) : kind == "tribute" ? 2000 : 1000,
            Expires = world.GameHours + 240,
        });
    }

    void Accept(World world, int id)
    {
        if (_state.Missions.FirstOrDefault(m => m.Id == id && m.State == 0) is not { } m) return;

        switch (m.Kind)
        {
            case "bounty":
                if (BountyTarget(world, m) is not { } platoon) return;

                m.State = 1;
                m.Platoon = platoon;
                m.Expires = world.GameHours + 24 * 20;
                Say(world, Fmt("{0} sets a bounty near {1}.", Name(world, m.Giver), TownName(world, m.Site)), popup: true);
                break;
            case "tribute":
                if (world.Player.Money < m.Reward) return;

                world.Player.Money -= m.Reward;
                if (_state.Find(m.Giver) is { } n) n.Treasury += m.Reward;
                ChangePlayerRelation(world, m.Giver, 8);
                Merit(10, 10);
                _state.Missions.Remove(m);
                break;
            default:
                if (world.Player.Money < m.Reward) return;

                world.Player.Money -= m.Reward;
                Town(world, m.Site).Food += CaravanCapacity;
                ChangePlayerRelation(world, m.Giver, 5);
                Merit(5, 15);
                _state.Missions.Remove(m);
                break;
        }
    }

    /// <summary>A bounty's quarry: a squad of an enemy of the giver (else of an emergent faction model) spawned 1500-3000 from the town.</summary>
    int? BountyTarget(World world, Mission m)
    {
        var pop = Pop(world)!;
        string? enemy = _state.Wars.Where(w => w.Involves(m.Giver)).Select(w => w.Other(m.Giver)).FirstOrDefault() ?? TownVersions.Emergent(0);
        int f = Index(world, enemy);
        if (f < 0) return null;

        var template = pop.Data.Factions[f].Squads.Select(s => pop.Data.Db.Find(s.Id)).FirstOrDefault(r => r is { Type: FcsRecordType.SquadTemplate });
        if (template is null) return null;

        var p = pop.Data.Sites[m.Site].Position;
        int id = pop.SpawnPlatoon(world, f, template.StringId, m.Site, new Vector2(p.X, p.Z), 1500, 3000);
        if (world.Platoons.Find(id) is not { } platoon) return null;

        platoon.ModTags[modId] = "mission:" + m.Id;
        m.Target = enemy;
        return id;
    }

    /// <summary>A bounty's quarry beaten (lost a fight or no one left): the reward, merit and the giver's favour.</summary>
    void MissionPlatoonGone(World world, Platoon platoon, PlatoonGone gone)
    {
        if (_state.Missions.FirstOrDefault(m => m.State == 1 && m.Platoon == platoon.Id) is not { } m) return;

        _state.Missions.Remove(m);
        if (gone is not (PlatoonGone.LostFight or PlatoonGone.NoMembers)) return;

        world.Player.Money += m.Reward;
        ChangePlayerRelation(world, m.Giver, 5);
        Merit(30, 5);
        Say(world, Fmt("{0} paid you {1} Cats for the bounty.", Name(world, m.Giver), m.Reward), popup: true);
    }

    void ExpireMissions(World world)
    {
        foreach (var m in _state.Missions.Where(m => m.State == 1 && m.Expires <= world.GameHours).ToList())
        {
            _state.Missions.Remove(m);
            if (world.Platoons.Find(m.Platoon) is not null) Pop(world)!.DespawnPlatoon(world, m.Platoon);
        }
    }

    #endregion

    #region Intrigue and romance

    /// <summary>SPY (SpyCooldownHours 24, SpySuccessChance 45 %): uncovers the character's secret, if any.</summary>
    void Spy(World world, int id)
    {
        var c = _state.Career;
        if (world.GameHours - c.LastSpy < 24 || _state.FindPerson(id) is not { Alive: true } p) return;

        c.LastSpy = world.GameHours;
        if (p.Secret == "" || !Chance(world, 45, 110))
        {
            Say(world, Fmt("{0} pulled back. The moment was not right.", tr("Your spy")), popup: false);
            return;
        }

        p.SecretKnown = true;
        Say(world, p.Name + " " + tr(p.Secret) + ".", popup: false);
    }

    /// <summary>EXPOSE: a public scandal; the nation loses stability and legitimacy, and the character stands trial (Trials).</summary>
    void Expose(World world, int id)
    {
        if (_state.FindPerson(id) is not { SecretKnown: true, Alive: true } p || _state.Find(p.Nation) is not { } n) return;

        Scandal(world, p, n);
        Merit(0, 10);
        if (Chance(world, 50, 111))
        {
            p.Alive = false;
            Say(world, Fmt("{0} was imprisoned on charges of treason.", p.Name), popup: false);
        }
    }

    void Scandal(World world, Person p, Nation n)
    {
        n.Stability = Math.Max(0, n.Stability - 8);
        n.Legitimacy = Math.Max(0, n.Legitimacy - 10);
        Say(world, Fmt("Scandal in {0}: {1} {2}.", Name(world, n.Faction), p.Name, tr(p.Secret)), popup: true);
        p.Secret = "";
        p.SecretKnown = false;
    }

    /// <summary>BLACKMAIL: 60 % leverage and 500-2000 Cats from the nation's treasury; else it backfires (relation -10, opinion -40).</summary>
    void Blackmail(World world, int id)
    {
        if (_state.FindPerson(id) is not { SecretKnown: true, Alive: true } p || _state.Find(p.Nation) is not { } n) return;

        if (Chance(world, 60, 112))
        {
            long cats = Math.Min(n.Treasury, 500 + Below(world, 1501, 113));
            n.Treasury -= cats;
            world.Player.Money += cats;
            p.Leverage = true;
            Say(world, Fmt("{0} pales as you hint at what you know. They will remember this", p.Name) + " " + tr("and they will fear you. (Leverage gained)"),
                popup: true);
            return;
        }

        p.Opinion -= 40;
        ChangePlayerRelation(world, n.Faction, -10);
        Say(world, Fmt("{0} publicly admitted that they {1} rather than yield to blackmail, and named an outsider as the extortionist.", p.Name,
            tr(p.Secret)), popup: true);
        p.Secret = "";
        p.SecretKnown = false;
    }

    /// <summary>
    /// Courting (cooldown 12 h): the opinion rises 5-15 with a chance of 50 % + opinion / 2; at 40 friends, 60 dating, 80 married (the house's
    /// nation +20 towards the player). Flirting while married costs the spouse 20 opinion (Jealousy).
    /// </summary>
    void Flirt(World world, int id)
    {
        if (_state.FindPerson(id) is not { Alive: true } p || world.GameHours - p.LastFlirt < 12) return;

        p.LastFlirt = world.GameHours;
        if (_state.People.FirstOrDefault(x => x.Bond == 3 && x.Id != p.Id) is { } spouse) spouse.Opinion -= 20;
        if (!Chance(world, 50 + p.Opinion / 2, 114))
        {
            p.Opinion -= 5;
            Say(world, Fmt("{0} seemed offended by your advance.", p.Name), popup: false);
            return;
        }

        p.Opinion = Math.Min(100, p.Opinion + 5 + Below(world, 11, 115));
        int bond = p.Opinion >= 80 ? 3 : p.Opinion >= 60 ? 2 : p.Opinion >= 40 ? 1 : p.Bond;
        if (bond <= p.Bond)
        {
            Say(world, Fmt("{0} responded warmly to your flirtation.", p.Name), popup: false);
            return;
        }

        p.Bond = bond;
        Say(world, Fmt(bond switch
        {
            1 => "{0} wants to remain friends for now.",
            2 => "{0} returned your feelings. You are now dating.",
            _ => "{0} accepted your proposal! You are now married.",
        }, p.Name), popup: true);
        if (bond == 3)
        {
            ChangePlayerRelation(world, p.Nation, 20);
            Merit(0, 20);
        }
    }

    /// <summary>A step of NPC intrigue: opinions drift toward 0, bonds fall with them, and NPCs expose each other now and then (NPCScandals).</summary>
    void Intrigue(World world)
    {
        foreach (var p in _state.People.Where(p => p.Alive))
        {
            p.Opinion -= Math.Sign(p.Opinion);
            if (p.Bond > 0 && p.Opinion < 20) p.Bond = Math.Max(0, p.Bond - 1);
            if (p.Opinion <= -40) p.Bond = -1;
        }

        var exposed = _state.People.Where(p => p.Alive && p.Secret != "" && p.Role is "leader" or "governor").ToList();
        if (exposed.Count > 0 && Chance(world, 3, 116))
        {
            var p = exposed[Below(world, exposed.Count, 117)];
            if (_state.Find(p.Nation) is { } n) Scandal(world, p, n);
        }
    }

    #endregion
}
