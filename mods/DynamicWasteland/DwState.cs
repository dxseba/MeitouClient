using System.Text.Json;
using Meitou.Simulation;

namespace Meitou.DynamicWasteland;

/// <summary>
/// A simulated nation: a faction that holds towns (or a rebel or emergent faction of the pools), with the original's national scores
/// (<c>military</c> 50, <c>economy</c> 50, <c>stability</c> 60, <c>warExhaustion</c> 0, <c>aggression</c> 50 when unset), its treasury in
/// Cats, government, state religion, leader and legitimacy.
/// </summary>
sealed class Nation
{
    public string Faction { get; set; } = "";
    public int Military { get; set; } = 50;
    public int Economy { get; set; } = 50;
    public int Stability { get; set; } = 60;
    public int Exhaustion { get; set; }
    public int Aggression { get; set; } = 50;
    public int Legitimacy { get; set; } = 50;
    public int BankruptDays { get; set; }
    public long Treasury { get; set; } = 10000;
    public string Government { get; set; } = "monarchy";
    public string Religion { get; set; } = "secular";
    public int Leader { get; set; } = -1;
    public double LastReform { get; set; } = -1e9;
    public double LastElection { get; set; }

    /// <summary>The nation this one rose from (a civil war's rebels, an emergent faction's town owner); "" for none.</summary>
    public string Origin { get; set; } = "";

    /// <summary>A pool faction (rebels or emergent) that is in play; false for a faction of the data.</summary>
    public bool Pooled { get; set; }

    /// <summary>The name the panel shows for a pooled faction ("" for the record's).</summary>
    public string Title { get; set; } = "";
}

/// <summary>A political character: a leader, governor, heir or mercenary captain (not a world character).</summary>
sealed class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Nation { get; set; } = "";
    public int Site { get; set; } = -1;
    public int Age { get; set; } = 40;
    public int Loyalty { get; set; } = 50;
    public int Ambition { get; set; } = 50;
    public int Competence { get; set; } = 50;
    public int Corruption { get; set; } = 20;
    public string Secret { get; set; } = "";
    public bool SecretKnown { get; set; }
    public bool Alive { get; set; } = true;

    /// <summary>The goal of the original's Ambitions: crown, wealth, faith, independence or revenge.</summary>
    public string Goal { get; set; } = "";

    /// <summary>Their opinion of the player (-100 to 100) and the bond (0 none, 1 friends, 2 dating, 3 married, -1 rivals).</summary>
    public int Opinion { get; set; }
    public int Bond { get; set; }
    public int Spouse { get; set; } = -1;
    public double LastFlirt { get; set; } = -1e9;

    /// <summary>The player holds leverage on them (the original's blackmail).</summary>
    public bool Leverage { get; set; }
}

/// <summary>A town's people: population, food store, majority religion, governor and unrest.</summary>
sealed class TownState
{
    public int Site { get; set; }
    public int Population { get; set; }
    public int Refugees { get; set; }
    public int Food { get; set; }
    public string Religion { get; set; } = "";
    public int Governor { get; set; } = -1;
    public int Unrest { get; set; }
}

sealed class War
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public double Since { get; set; }
    public int WinsA { get; set; }
    public int WinsB { get; set; }
    public int TownsA { get; set; }
    public int TownsB { get; set; }
    public bool Holy { get; set; }

    public bool Involves(string f) => A == f || B == f;

    public bool Between(string x, string y) => A == x && B == y || A == y && B == x;

    public string Other(string f) => A == f ? B : A;
}

/// <summary>A truce or an alliance between two nations, until a game hour.</summary>
sealed class Pact
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public double Until { get; set; }

    public bool Between(string x, string y) => A == x && B == y || A == y && B == x;
}

/// <summary>An offensive: the attacker's army (a campaign, null for none) sent against a town of the defender.</summary>
sealed class Offensive
{
    public string Attacker { get; set; } = "";
    public string Defender { get; set; } = "";
    public int Site { get; set; }
    public string? Campaign { get; set; }
    public double Since { get; set; }
    public int Instance { get; set; } = -1;
}

/// <summary>A town won by a faction whose owner is changed once the town is not loaded.</summary>
sealed class Conquest
{
    public int Site { get; set; }
    public string Faction { get; set; } = "";
}

sealed class News
{
    public double Hours { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A mercenary company: its captain, men, employer and wage (the original's Mercenaries).</summary>
sealed class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Captain { get; set; } = -1;
    public int Men { get; set; }
    public string Employer { get; set; } = "";
    public int Wage { get; set; }
    public int Unpaid { get; set; }
    public int Fame { get; set; }
    public bool Player { get; set; }
}

/// <summary>The crisis of the era (at most one).</summary>
sealed class Crisis
{
    public string Kind { get; set; } = "";
    public string Nation { get; set; } = "";
    public int Site { get; set; } = -1;
    public double Until { get; set; }
    public int Platoon { get; set; } = -1;
}

/// <summary>A food caravan between two towns of a nation; a real platoon when the nation has a trader squad.</summary>
sealed class Caravan
{
    public int Id { get; set; }
    public string Nation { get; set; } = "";
    public int From { get; set; }
    public int To { get; set; }
    public int Food { get; set; }
    public double Due { get; set; }
    public int Platoon { get; set; } = -1;
}

/// <summary>A mission on the board: offered (0), accepted (1).</summary>
sealed class Mission
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public string Giver { get; set; } = "";
    public string Target { get; set; } = "";
    public int Site { get; set; } = -1;
    public int Reward { get; set; }
    public int State { get; set; }
    public double Expires { get; set; }
    public int Platoon { get; set; } = -1;
}

/// <summary>A faith: its name, whether it is militant, its authority and founder.</summary>
sealed class Faith
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Militant { get; set; }
    public int Authority { get; set; } = 60;
    public string Founder { get; set; } = "";
}

/// <summary>The player's career: patron nation, merit, influence, rank, own company and faith.</summary>
sealed class Career
{
    public string Patron { get; set; } = "";
    public int Merit { get; set; }
    public int Influence { get; set; }
    public int Rank { get; set; }
    public double LastSpy { get; set; } = -1e9;
    public string Faith { get; set; } = "";
    public int Company { get; set; } = -1;
}

/// <summary>Everything the simulation keeps; saved as JSON and hashed from the same bytes.</summary>
sealed class DwState
{
    public int Version { get; set; } = 2;
    public List<Nation> Nations { get; set; } = [];
    public List<Person> People { get; set; } = [];
    public List<TownState> Towns { get; set; } = [];
    public List<War> Wars { get; set; } = [];
    public List<Pact> Truces { get; set; } = [];
    public List<Pact> Alliances { get; set; } = [];
    public List<Offensive> Offensives { get; set; } = [];
    public List<Conquest> Pending { get; set; } = [];
    public SortedDictionary<int, string> Owners { get; set; } = [];
    public List<News> News { get; set; } = [];
    public List<Company> Companies { get; set; } = [];
    public Crisis? Crisis { get; set; }
    public double LastCrisisEnd { get; set; }
    public List<Caravan> Caravans { get; set; } = [];
    public List<Mission> Missions { get; set; } = [];
    public List<Faith> Faiths { get; set; } = [];
    public Career Career { get; set; } = new();
    public double LastCult { get; set; } = -1e9;
    public double LastEmergent { get; set; } = -1e9;
    public double NextStep { get; set; }
    public int LastDay { get; set; } = -1;
    public ulong Rolls { get; set; }
    public int NextId { get; set; } = 1;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public Nation? Find(string faction) => Nations.FirstOrDefault(n => n.Faction == faction);

    public Person? FindPerson(int id) => id < 0 ? null : People.FirstOrDefault(p => p.Id == id);

    public TownState? FindTown(int site) => Towns.FirstOrDefault(t => t.Site == site);

    public War? WarOf(string a, string b) => Wars.FirstOrDefault(w => w.Between(a, b));

    public bool Allied(string a, string b) => Alliances.Any(p => p.Between(a, b));

    public bool InTruce(string a, string b, double now) => Truces.Any(p => p.Between(a, b) && p.Until > now);

    public Faith? FindFaith(string id) => Faiths.FirstOrDefault(f => f.Id == id);

    public byte[] Bytes() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    public static DwState FromBytes(byte[] bytes)
    {
        var s = JsonSerializer.Deserialize<DwState>(bytes, Options);
        return s is { Version: 2 } ? s : new DwState();
    }

    public void Hash(ref StateHasher h)
    {
        ulong x = 0xCBF29CE484222325UL;
        foreach (byte b in Bytes()) x = (x ^ b) * 0x100000001B3UL;
        h.Add(x);
    }
}
