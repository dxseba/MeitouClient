using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.SentientSands;

/// <summary>An item a character carries, as the server's contexts list it (<c>name</c>, <c>count</c>, <c>price</c>, <c>kind</c>).</summary>
sealed record CarriedItem(string Name, string Record, int Count, int Price, string Kind);

/// <summary>
/// What the requests tell the server about one character (the plugin's context: <c>npc_id</c>, name, race, gender, faction, trader, squad,
/// money, health, inventory, town), read in the serial step.
/// </summary>
sealed record CharacterFacts(CharacterId Id, string Name, string Race, bool Female, string Faction, string FactionId, int Squad, bool IsTrader,
    bool IsPlayer, long Money, string Health, IReadOnlyList<CarriedItem> Inventory, string Town, Vector3 Position, bool Down)
{
    public Dictionary<string, object?> Context(int day, int hour, int minute) => new()
    {
        ["npc_id"] = ServerMessage.NpcId(Id), ["id"] = ServerMessage.Key(Id), ["name"] = Name, ["race"] = Race,
            ["gender"] = Female ? "female" : "male",
        ["faction"] = Faction, ["factionID"] = FactionId, ["is_trader"] = IsTrader, ["in_player_faction"] = IsPlayer,
        ["squad"] = Squad.ToString(System.Globalization.CultureInfo.InvariantCulture), ["money"] = Money, ["health"] = Health,
        ["is_unconscious"] = Down, ["inventory"] = Inventory.Select(Item).ToList(), ["in_town"] = Town.Length > 0,
        ["environment"] = new Dictionary<string, object?> { ["town_name"] = Town }, ["day"] = day, ["hour"] = hour, ["minute"] = minute,
    };

    static Dictionary<string, object?> Item(CarriedItem i) => new() { ["name"] = i.Name, ["count"] = i.Count, ["price"] = i.Price,
        ["kind"] = i.Kind };
}

/// <summary>The serial step's copy for the interface: the player's lead, the characters near it, the game clock and the player's cats.</summary>
sealed record GameFacts(CharacterId Lead, IReadOnlyDictionary<CharacterId, CharacterFacts> Characters, int Day, int Hour, int Minute, long Money)
{
    public static readonly GameFacts Empty = new(CharacterId.None, new Dictionary<CharacterId, CharacterFacts>(), 0, 0, 0, 0);

    /// <summary>The towns an override replaced (the report's <c>changed_towns</c>: name, owner, owner id, type).</summary>
    public IReadOnlyList<Dictionary<string, object?>> ChangedTowns { get; init; } = [];

    /// <summary>The player's outposts (the report's <c>player_towns</c>: key, name, zone).</summary>
    public IReadOnlyList<Dictionary<string, object?>> PlayerTowns { get; init; } = [];

    public CharacterFacts? Of(CharacterId id) => Characters.GetValueOrDefault(id);

    public Dictionary<string, object?>? Context(CharacterId id) => Of(id)?.Context(Day, Hour, Minute);

    /// <summary>Characters within <paramref name="radius"/> of <paramref name="from"/>, nearest first.</summary>
    public IEnumerable<CharacterFacts> Near(Vector3 from, float radius) =>
        Characters.Values.Where(c => Vector3.Distance(c.Position, from) <= radius).OrderBy(c => Vector3.Distance(c.Position, from));
}

/// <summary>Reads <see cref="GameFacts"/> from the world (serial step).</summary>
static class FactsReader
{
    const float Radius = 2500;

    public static GameFacts Read(World world, PopulationData population)
    {
        var table = world.Characters;
        var lead = Lead(world);
        if (!table.TryResolveNext(lead, out int l)) return GameFacts.Empty;

        var at = table.Next[l].Position;
        var facts = new Dictionary<CharacterId, CharacterFacts>();
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is { } c && Vector3.Distance(table.Next[i].Position, at) <= Radius) facts[table.IdOf(i)] = Of(world, population, i, c);
        }

        int minutes = (int)Math.Floor(world.GameHours * 60);
        return new GameFacts(lead, facts, minutes / 1440, minutes / 60 % 24, minutes % 60, world.Player.Money)
        {
            ChangedTowns = ChangedTowns(population), PlayerTowns = PlayerTowns(world, population),
        };
    }

    static List<Dictionary<string, object?>> ChangedTowns(PopulationData population)
    {
        var relations = population.Relations;
        return [.. population.Sites.Where(s => s.Replacement is not null).Select(s =>
        {
            int f = s.Town.Faction is { } id ? relations.IndexOf(id) : -1;
            string owner = relations.Contains(f) ? relations.Factions[f].Name : "";
            return new Dictionary<string, object?> { ["name"] = s.Town.Name, ["owner"] = owner, ["owner_id"] = s.Town.Faction
                ?? "", ["type"] = (int)s.Town.Type };
        })];
    }

    static List<Dictionary<string, object?>> PlayerTowns(World world, PopulationData population) =>
        [.. world.Buildings.Towns.Select(t => new Dictionary<string, object?>
        {
            ["key"] = t.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), ["name"] = t.Name, ["zone"] = TownName(population,
                t.Position),
        })];


    static CharacterId Lead(World world)
    {
        if (world.Player.Selection.Count > 0) return world.Player.Selection[0];

        var table = world.Characters;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is { IsPlayer: true, Medical.Dead: false }) return table.IdOf(i);
        }

        return CharacterId.None;
    }

    static CharacterFacts Of(World world, PopulationData population, int i, CharacterCold c)
    {
        var relations = population.Relations;
        var faction = relations.Contains(c.Faction) ? relations.Factions[c.Faction] : null;
        string race = c.Race is { } r ? population.Db.Find(r.StringId)?.Name ?? r.StringId : "";
        long money = c.IsPlayer ? world.Player.Money : world.Squads.Find(c.SquadId)?.Money ?? 0;
        string health = c.Medical switch { { Dead: true } => "Dead", { Unconscious: true } => "Unconscious", _ => "Healthy" };
        return new CharacterFacts(world.Characters.IdOf(i), c.Name, race, c.Appearance?.Female ?? false, faction?.Name ?? "", faction?.Id ?? "",
            c.SquadId, Simulation.Trading.TradeSystem.ShopOf(world, c) is not null, c.IsPlayer, money, health, Items(population, c),
            TownName(population, world.Characters.Next[i].Position), world.Characters.Next[i].Position, c.Medical is { Incapacitated: true });
    }

    static IReadOnlyList<CarriedItem> Items(PopulationData population, CharacterCold c)
    {
        if (c.Inventory is not { } inv) return [];

        return [.. inv.All().GroupBy(i => i.Record).Select(g => Carried(population, g.First(), g.Sum(i => i.Quantity)))];
    }

    static CarriedItem Carried(PopulationData population, Simulation.Items.ItemInstance item, int count)
    {
        var info = population.Items.Info(item.Record);
        string kind = item.Type.ToString().ToLowerInvariant();
        if (population.Db.Find(item.Record) is { } r && r.GetFloat("nutrition") > 0) kind = "food";
        return new CarriedItem(item.ShownName(), item.Record, count, info?.Value ?? 0, kind);
    }

    public static string TownName(PopulationData population,
        Vector3 at) => population.TownAt(at) is int t and >= 0 ? population.Sites[t].Town.Name : "";
}
