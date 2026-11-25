using System.Globalization;
using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Population;
using Meitou.Simulation.Trading;

namespace Meitou.CityBuilder;

/// <summary>
/// The settlement's people. The original makes them NPC squads of its own factions (Townsfolk, Town Guard) and hands the building over; Meitou's
/// save keeps only the player faction's player-built buildings, so here the building stays the player's and its people are squads of the player's
/// faction the AI runs: independent (not led, not selectable), out of the faction size count, homed at the building, on their template's AI
/// package, a trader with a faction shop on its template's terms and stock, guards on the City guard packages with the gate rules. Each squad is
/// tagged <c>site|role</c> under the mod's id (saved with the squad), so it is found again after a load.
/// </summary>
internal sealed partial class CitySystem
{
    #region Arrivals and offers

    /// <summary>The buyers (or the innkeeper) whose hour has come: an offer to the player, or the innkeeper opening the tavern.</summary>
    void Arrivals(World world)
    {
        foreach (var place in _places.Values)
        {
            if (place.State != PlaceState.Waiting || world.GameHours < place.ArriveAt) continue;
            if (_catalog.Find(place.Record) is not { } building || world.Buildings.Find(place.Site) is not { } site) continue;

            if (building.Kind == CityKind.Tavern) OpenTavern(world, place, site);
            else MakeOffer(world, place, building);
        }
    }

    void MakeOffer(World world, CityPlace place, CityBuilding building)
    {
        place.State = PlaceState.Offer;
        place.Price = building.Kind == CityKind.Residence
            ? building.Small ? _rules.HomeSmall : _rules.HomeLarge
            : building.Small ? _rules.TradeSmall : _rules.TradeLarge;
        if (building.Kind == CityKind.Residence)
        {
            var templates = CityCatalog.Residents(building.People);
            place.Template = templates[Rng.Int(Roll(world), templates.Length)];
        }

        _offerSerial++;
        string head = building.Kind == CityKind.Residence ? "Settlers want to buy the house" : "A trader wants to buy the building";
        world.Post(_tr(head) + ": " + _tr("Offer: ") + Cats(place.Price));
    }

    void Sell(World world, string siteId)
    {
        if (!_places.TryGetValue(siteId, out var place) || place.State != PlaceState.Offer) return;
        if (_catalog.Find(place.Record) is not { } building || world.Buildings.Find(siteId) is not { } site) return;

        string template = building.Kind == CityKind.Residence ? place.Template : CityCatalog.Shop(building.People, place.Shop);
        string role = building.Kind == CityKind.Residence ? "home" : "shop";
        var squad = MakeSquad(world, site, role, template, building.Kind == CityKind.TradeHouse);
        if (squad is null)
        {
            world.Post(_tr("Could not pay."));
            return;
        }

        world.Player.Money += place.Price;
        place.State = PlaceState.Open;
        place.LastPaidDay = Day(world.GameHours);
        world.Post(_tr("Deal made") + ". " + _tr("Received: ") + Cats(place.Price) + ". " + _tr("Welcome, ") + Names(world, squad));
    }

    void Refuse(World world, string siteId)
    {
        if (!_places.TryGetValue(siteId, out var place) || place.State != PlaceState.Offer) return;

        place.State = PlaceState.Waiting;
        place.ArriveAt = world.GameHours + (0.8 + 0.4 * Rng.Float(Roll(world))) * _rules.RetryHours;
        world.Post(_tr("Refused") + ". " + _tr("They left to look elsewhere. Others will come in a while."));
    }

    void OpenTavern(World world, CityPlace place, BuildSite site)
    {
        var squad = MakeSquad(world, site, "inn", CityCatalog.Innkeeper, shop: true);
        if (squad is null) return;

        place.State = PlaceState.Open;
        place.LastPaidDay = Day(world.GameHours);
        world.Post(_tr("The tavern is open. Innkeeper: ") + Names(world, squad));
    }

    #endregion

    #region Shops and guards

    /// <summary>
    /// Changes a trade house's shop: before the sale only the choice; once open, the trader and guards leave and a trader of the other shop comes at
    /// once (no new deal).
    /// </summary>
    void ChooseShop(World world, string siteId, int shop)
    {
        if (!_places.TryGetValue(siteId, out var place) || !CityCatalog.ShopKinds.Contains(shop) || place.Shop == shop) return;
        if (_catalog.Find(place.Record) is not { Kind: CityKind.TradeHouse } building || world.Buildings.Find(siteId) is not { } site) return;

        place.Shop = shop;
        Unfurnish(world, place);
        if (place.State != PlaceState.Open) return;

        SendAway(world, siteId, "shop");
        if (MakeSquad(world, site, "shop", CityCatalog.Shop(building.People, shop), shop: true) is { } squad)
            world.Post(_tr("City: the shop changed its kind") + " - " + _tr("Trader: ") + Names(world, squad));
    }

    /// <summary>
    /// Hires guards for a post, each for <see cref="CityRules.HireCost"/>, paid at once, up to its capacity (those on their way counted);
    /// they take up
    /// the post when one of the player's people is near it (<see cref="PostGuards"/>).
    /// </summary>
    void Hire(World world, string siteId, int count)
    {
        if (!_places.TryGetValue(siteId, out var place) || _catalog.Find(place.Record) is not { Kind: CityKind.GuardPost } building) return;
        if (world.Buildings.Find(siteId) is null) return;

        int have = GuardsOf(world, siteId).Values.Sum() + place.PendingGuards;
        int room = building.GuardCapacity - have;
        int wanted = count <= 0 ? room : Math.Min(count, room);
        if (wanted <= 0)
        {
            world.Post(_tr("The post is fully staffed."));
            return;
        }

        long cost = (long)wanted * _rules.HireCost;
        if (world.Player.Money < cost)
        {
            world.Post(_tr("Not enough money."));
            return;
        }

        world.Player.Money -= cost;
        place.PendingGuards += wanted;
        world.Post(_tr("Hired and paid guards on their way: ") + wanted.ToString(CultureInfo.InvariantCulture)
            + _tr(". They take up the post as soon as one of your people is near it."));
    }

    /// <summary>The post's living guards by shift.</summary>
    Dictionary<GuardShift, int> GuardsOf(World world, string siteId) =>
        Enum.GetValues<GuardShift>().ToDictionary(s => s, s => Living(world, Tagged(world, siteId, Role(s))));

    /// <summary>
    /// Guards on their way take up a post once a person of the player's (not the settlement's) is within the scan radius: every fifth guard
    /// watches the gate, the others share the day and night shifts (City.dll's texts); those that no longer fit get their money back.
    /// </summary>
    void PostGuards(World world)
    {
        foreach (var place in _places.Values)
        {
            if (place.PendingGuards <= 0 || _catalog.Find(place.Record) is not { Kind: CityKind.GuardPost } building) continue;
            if (world.Buildings.Find(place.Site) is not { } site || !PlayerNear(world, site.Position, _rules.ScanRadius)) continue;

            var shifts = GuardsOf(world, place.Site);
            int room = Math.Max(0, building.GuardCapacity - shifts.Values.Sum());
            int hired = HireEach(world, site, building, shifts, Math.Min(room, place.PendingGuards));
            int back = place.PendingGuards - hired;
            place.PendingGuards = 0;
            if (hired > 0) world.Post(_tr("Hired guards took up their posts: ") + hired.ToString(CultureInfo.InvariantCulture));
            if (back <= 0) continue;

            world.Player.Money += (long)back * _rules.HireCost;
            world.Post(_tr("The post is already full, money back for the extra ones: ") + Cats((long)back * _rules.HireCost));
        }
    }

    /// <summary>Whether a living character of the player's faction that is not one of the settlement's people stands within the radius.</summary>
    bool PlayerNear(World world, System.Numerics.Vector3 at, float radius)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold) continue;
            if (world.Squads.Find(cold.SquadId) is { } squad && squad.ModTags.ContainsKey(_modId)) continue;

            var p = table.Next[slot].Position;
            float dx = p.X - at.X, dz = p.Z - at.Z;
            if (dx * dx + dz * dz <= radius * radius) return true;
        }

        return false;
    }

    int HireEach(World world, BuildSite site, CityBuilding building, Dictionary<GuardShift, int> shifts, int wanted)
    {
        int hired = 0;
        for (int i = 0; i < wanted; i++)
        {
            int total = shifts.Values.Sum() + 1;
            var shift = shifts[GuardShift.Gate] < total / 5 ? GuardShift.Gate
                : shifts[GuardShift.Day] <= shifts[GuardShift.Night] ? GuardShift.Day : GuardShift.Night;
            var squad = Tagged(world, site.Id, Role(shift)) ?? MakeSquad(world, site, Role(shift), null, shop: false, guard: shift);
            if (squad is null) break;

            if (SpawnMembers(world, squad, site, CityCatalog.Guard(building.People, shift)) == 0)
            {
                if (squad.Members.Count == 0) SendAway(world, site.Id, Role(shift));
                break;
            }

            shifts[shift]++;
            hired++;
        }

        return hired;
    }

    static string Role(GuardShift shift) => "guard-" + shift.ToString().ToLowerInvariant();

    #endregion

    #region Squads

    /// <summary>
    /// A new squad of the player's faction for the building, its members made from <paramref name="template"/> (none for an empty guard squad),
    /// then switched to the AI: independent, uncounted, homed at the building, on its package; a shop on the template's terms. Null when nobody
    /// could be made.
    /// </summary>
    Squad? MakeSquad(World world, BuildSite site, string role, string? template, bool shop, GuardShift? guard = null)
    {
        string? templateId = template ?? (guard is { } g && _catalog.Find(site.Record) is { } b ? CityCatalog.Guard(b.People, g) : null);
        if (templateId is null || TemplateOf(templateId) is not { } plan) return null;

        var squad = SquadManagement.NewSquad(world, _db, site.Position, _tr(plan.Name));
        squad.Name = _tr(plan.Name);
        squad.ModTags[_modId] = site.Id + "|" + role;
        if (template is not null && SpawnMembers(world, squad, site, template) == 0)
        {
            _enqueue(new RemoveSquadCommand(squad.Id) { Tick = world.Tick + 1 });
            squad.ModTags.Remove(_modId);
            return null;
        }

        SwitchToAi(world, squad, site, plan, shop, guard is not null);
        return squad;
    }

    void SwitchToAi(World world, Squad squad, BuildSite site, SquadTemplate plan, bool shop, bool guard)
    {
        long first = world.Tick + 1, then = world.Tick + 2;
        _enqueue(new SetSquadUncounted(squad.Id, true, _modId) { Tick = first });
        _enqueue(new SetSquadAutonomy(squad.Id, true, Independent: true, Owner: _modId) { Tick = first });
        _enqueue(new SetSquadHome(squad.Id, site.Id) { Tick = then });
        if (plan.AiPackages.FirstOrDefault() is { Id.Length: > 0 } package) _enqueue(new ChangeAi(squad.Id, package.Id) { Tick = then });
        if (shop) _enqueue(new SetSquadShop(squad.Id, true, plan.Id, _modId) { Tick = then });
        if (!guard) return;

        _enqueue(new SetSquadConduct(squad.Id, SquadConduct.GateInside | SquadConduct.CloseDoors) { Tick = then });
        _enqueue(new SetSquadOptions(squad.Id, Simulation.Player.SquadAi.Feed | Simulation.Player.SquadAi.Share, _modId) { Tick = then });
    }

    /// <summary>Makes the template's members (leader, squad, animals) at the building, into <paramref name="squad"/>; how many were made.</summary>
    int SpawnMembers(World world, Squad squad, BuildSite site, string template)
    {
        if (TemplateOf(template) is not { } plan || world.Systems.OfType<PopulationSystem>().FirstOrDefault() is not { } population) return 0;

        var members = SquadFactory.Plan(_db, plan, 1, world.Seed, Roll(world)).Members;
        var centre = new Vector2(site.Position.X, site.Position.Z);
        int made = 0;
        foreach (var member in members)
        {
            if (CharacterPlacement.WalkableNear(world, centre + member.Offset, Roll(world)) is not { } at) continue;

            var id = population.SpawnIntoPlayerSquad(world, squad.Id, member.RecordId, at,
                new SpawnOptions(member.IsAnimal ? member.Age : 1, IgnoreCaps: true));
            if (!id.IsNone) made++;
        }

        return made;
    }

    /// <summary>The building's squad of <paramref name="role"/>, by the mod's tag.</summary>
    Squad? Tagged(World world, string site, string role)
    {
        string tag = site + "|" + role;
        return world.Squads.All.Where(s => s.ModTags.TryGetValue(_modId, out var t) && t == tag).OrderBy(s => s.Id).FirstOrDefault();
    }

    /// <summary>The building's squads (every role, or one), in id order.</summary>
    List<Squad> SquadsOf(World world, string site, string? role)
    {
        bool Mine(string tag) => role is null ? tag.StartsWith(site + "|", StringComparison.Ordinal) : tag == site + "|" + role;

        return [.. world.Squads.All.Where(s => s.ModTags.TryGetValue(_modId, out var t) && Mine(t)).OrderBy(s => s.Id)];
    }

    /// <summary>
    /// The building's people (of a role, or all) leave: each living member is dismissed from the faction (it walks off as a dismissed recruit) and
    /// the emptied squad is removed. Whether anyone left.
    /// </summary>
    bool SendAway(World world, string site, string? role)
    {
        bool any = false;
        foreach (var squad in SquadsOf(world, site, role))
        {
            squad.ModTags.Remove(_modId);
            if (squad.Shop is not null) _enqueue(new SetSquadShop(squad.Id, false, Owner: _modId) { Tick = world.Tick + 1 });
            foreach (var member in squad.Members.ToList())
            {
                _enqueue(new DismissCommand(member) { Tick = world.Tick + 2 });
                any = true;
            }

            _enqueue(new RemoveSquadCommand(squad.Id) { Tick = world.Tick + 3 });
        }

        return any;
    }

    static int Living(World world, Squad? squad)
    {
        if (squad is null) return 0;

        int alive = 0;
        foreach (var member in squad.Members)
        {
            if (world.Characters.TryResolveNext(member, out int slot) && world.Characters.Next[slot].Alive) alive++;
        }

        return alive;
    }

    static string Names(World world, Squad squad) =>
        string.Join(", ", squad.Members.Select(world.Characters.NameOf).Where(n => n.Length > 0));

    string Cats(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + _tr(" cats");

    #endregion
}
