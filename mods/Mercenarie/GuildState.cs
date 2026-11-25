using System.Text.Json;

namespace Meitou.Mercenarie;

/// <summary>
/// The contract types, numbered as the original's (<c>FUN_1800e60d0</c>: escort, caravan, scientific expedition, bounty hunt, mail).
/// </summary>
enum ContractKind
{
    Escort = 0,
    Caravan = 1,
    Expedition = 2,
    Bounty = 3,
    Mail = 4,
}

/// <summary>
/// A contract on a board or accepted: its kind, the issuing and destination sites (indices and names), the distance in km (one way), the danger
/// (1 to 5), the distance tier (0 near, 1 medium, 2 long), the group size, the reward and the deadline (game day).
/// </summary>
sealed record Contract(ContractKind Kind, int From, string FromName, int To, string ToName, float Km, int Danger, int Tier, int Group, int Reward,
    int Deadline)
{
    /// <summary>The spawned group's platoon tag (client, caravan, scientists or the wanted group; "" none).</summary>
    public string Tag { get; init; } = "";

    /// <summary>The negotiated change of the price, percent (the original's -20..+25).</summary>
    public int Negotiated { get; init; }

    /// <summary>The spawned group's squad once it has loaded (-1 not yet).</summary>
    public int Squad { get; set; } = -1;

    /// <summary>Members of the group when it loaded (losses are counted against it).</summary>
    public int Members { get; set; }

    /// <summary>0 outbound; 1 at the destination (expedition research); 2 the return leg; 3 done, to report.</summary>
    public int Stage { get; set; }

    /// <summary>Game hours when the current stage ends (research at the ruin).</summary>
    public double StageUntil { get; set; }

    /// <summary>Round trips (caravan, expedition) travel the distance twice.</summary>
    public bool RoundTrip => Kind is ContractKind.Caravan or ContractKind.Expedition;
}

/// <summary>A closed contract, for the board's history.</summary>
sealed record ClosedContract(ContractKind Kind, string FromName, string ToName, int Reward, int Xp, int Day, bool Success);

/// <summary>A registered Guild House: the building's instance key, its town and name.</summary>
sealed record GuildHouse(string Building, string Town, string Name)
{
    /// <summary>Game hours of the last client that came in.</summary>
    public double LastClient { get; set; } = double.NegativeInfinity;

    /// <summary>The waiting client's platoon tag ("" none).</summary>
    public string Client { get; set; } = "";
}

/// <summary>
/// What the guild keeps per save: XP (past level 10's threshold it also counts as prestige), the reputation per town, the accepted contracts, the
/// Guild Houses and the history; JSON in the mod's sidecar.
/// </summary>
sealed class GuildState
{
    public int Xp { get; set; }
    public SortedDictionary<string, int> Reputation { get; set; } = new(StringComparer.Ordinal);
    public List<Contract> Active { get; set; } = [];
    public List<ClosedContract> History { get; set; } = [];
    public List<GuildHouse> Houses { get; set; } = [];
    public List<Lease> Leases { get; set; } = [];
    public int RentDay { get; set; } = -1;
    public ulong Spawns { get; set; }

    public int Level => Pricing.LevelOf(Xp);

    public int Prestige => Math.Max(0, Xp - Pricing.MaxLevelXp);

    public void Rep(string town, int delta) => Reputation[town] = Math.Clamp(Reputation.GetValueOrDefault(town) + delta, -100, 100);

    public void Close(Contract c, int day, int xp, bool success)
    {
        Active.Remove(c);
        History.Add(new ClosedContract(c.Kind, c.FromName, c.ToName, success ? c.Reward : 0, xp, day, success));
        if (History.Count > 100) History.RemoveAt(0);
    }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static GuildState Deserialize(Stream stream)
    {
        var s = JsonSerializer.Deserialize<GuildState>(stream) ?? new GuildState();
        s.Reputation = new SortedDictionary<string, int>(s.Reputation ?? [], StringComparer.Ordinal);
        s.Houses ??= [];
        s.Leases ??= [];
        return s;
    }
}
