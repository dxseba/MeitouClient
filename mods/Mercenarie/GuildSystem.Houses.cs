using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Mercenarie;

/// <summary>
/// Guild Houses (the original's offices): a building the player owns (bought through the game's own house purchase, or built) is defined as one
/// from guild level 3 (one; three from level 4; one per town); with a Client Chair and a Waiting Chair inside it is operational, and a client
/// walks in once a day with work (talking to the client opens the town's board).
/// </summary>
sealed partial class GuildSystem
{
    const string ClientChair = "880130" + Mod, WaitingChair = "880131" + Mod, ClientTemplate = "880101" + Mod, MissionFaction = "880050" + Mod;
    const double ClientEveryHours = 24;

    static int HouseLimit(int level) => level >= 4 ? 3 : level >= 3 ? 1 : 0;

    void DefineHouse(World world, DefineGuildHouse define)
    {
        var existing = _state.Houses.FirstOrDefault(h => h.Building == define.Building);
        if (define.Abandon)
        {
            if (existing is not null) _state.Houses.Remove(existing);
            return;
        }

        string? why = existing is not null ? "This building is already registered as a Guild House." : HouseRefusal(world, define.Building);
        if (why is not null)
        {
            world.Post(context.Tr(why));
            return;
        }

        string town = BuildingAt(world, define.Building) is { } at && population.TownAt(at) is int s and >= 0 ? population.Sites[s].Town.Name : "";
        _state.Houses.Add(new GuildHouse(define.Building, town, $"{context.Tr("Guild House of")} {town}"));
        world.Post(context.Tr(Operational(world, define.Building)
            ? "OPERATIONAL GUILD HOUSE: The Client Chair and Waiting Chair have been detected in the defined building."
            : "Building defined as Guild House. Install a Customer Chair and a Waiting Chair in this building."));
    }

    string? HouseRefusal(World world, string building)
    {
        if (_state.Level < 3) return "Locked Feature: Reach Guild Level 3 to create a Guild House.";
        if (_state.Houses.Count >= HouseLimit(_state.Level)) return "Limit reached for your Guild level.";
        if (!OwnedByPlayer(world, building)) return "Select a building belonging to the player.";

        string town = BuildingAt(world, building) is { } at && population.TownAt(at) is int s and >= 0 ? population.Sites[s].Town.Name : "";
        return town.Length > 0 && _state.Houses.Any(h => h.Town == town) ? "A Guild House is already registered in this city." : null;
    }

    /// <summary>The player's: a town house it bought (the property's owner), or a building it built.</summary>
    static bool OwnedByPlayer(World world, string building) =>
        world.Fixtures.OwnsBuilding(building, world.Player.Faction)
        || world.Buildings.All.Any(s => s.Id == building && s.Owner == world.Player.Faction && s.Complete);

    static Vector3? BuildingAt(World world, string building) =>
        world.Buildings.All.FirstOrDefault(s => s.Id == building)?.Position ?? world.Fixtures.OfBuilding(building).FirstOrDefault()?.Position;

    /// <summary>Both chairs stand finished inside the building.</summary>
    static bool Operational(World world, string building) =>
        world.Buildings.All.Any(s => s.Parent == building && s.Complete && s.Record == ClientChair)
        && world.Buildings.All.Any(s => s.Parent == building && s.Complete && s.Record == WaitingChair);

    /// <summary>Once a day an operational house that has no client waiting gets one, by the building.</summary>
    void HouseClients(World world)
    {
        if (!settings.HouseClients || spawner is null) return;

        foreach (var house in _state.Houses.ToList())
        {
            if (!OwnedByPlayer(world, house.Building))
            {
                _state.Houses.Remove(house);
                continue;
            }

            if (world.GameHours - house.LastClient < ClientEveryHours || ClientWaiting(world, house) || !Operational(world, house.Building)) continue;

            NewClient(world, house);
        }
    }

    void NewClient(World world, GuildHouse house)
    {
        if (BuildingAt(world, house.Building) is not { } at || population.TownAt(at) is not (int site and >= 0)) return;

        int faction = population.Relations.IndexOf(MissionFaction);
        string tag = $"client-{++_state.Spawns}";
        int id = faction < 0 ? -1 : spawner!.SpawnPlatoon(world, faction, ClientTemplate, site, Flat(at), 30, 150, maxMembers: 1);
        if (id < 0 || world.Platoons.Find(id) is not { } platoon) return;

        platoon.ModTags[context.Id] = tag;
        house.Client = tag;
        house.LastClient = world.GameHours;
        world.Post($"{house.Name}: {context.Tr("I came directly to your Guild House because I have work to entrust to your company.")}");
    }

    bool ClientWaiting(World world, GuildHouse house) =>
        house.Client.Length > 0 && world.Platoons.FindByTag(context.Id, house.Client) is { } p
        && (p.SquadId < 0 || world.Squads.Find(p.SquadId)?.Members.Any(m => Alive(world, m)) == true);

    bool IsHouseClient(World world, CharacterCold c) =>
        c.SquadId >= 0 && _state.Houses.Any(h => h.Client.Length > 0 && world.Platoons.FindByTag(context.Id, h.Client)?.SquadId == c.SquadId);

    /// <summary>The waiting clients' characters (the window offers them "I am looking for a contract.").</summary>
    IReadOnlySet<CharacterId> ClientIds(World world)
    {
        var ids = new HashSet<CharacterId>();
        foreach (var h in _state.Houses)
        {
            if (h.Client.Length > 0 && world.Platoons.FindByTag(context.Id, h.Client) is { SquadId: >= 0 } p
                && world.Squads.Find(p.SquadId) is { } squad)
                ids.UnionWith(squad.Members);
        }

        return ids;
    }
}
