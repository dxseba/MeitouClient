using System.Text;
using System.Text.Json;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.Mentors;

/// <summary>A teacher's trade: its name, what it covers (the window's words) and the skills it teaches.</summary>
sealed record Profession(string Name, string Covers, IReadOnlyList<StatsEnumerated> Skills);

/// <summary>One skill the window can list: its stat, its label and whether it is an attribute (priced higher).</summary>
sealed record Teachable(StatsEnumerated Stat, string Label, bool Attribute);

/// <summary>
/// Who teaches what. The port's own heuristic (not the original's table, which is its author's and is not shipped): words in the teacher's
/// CHARACTER record name, trader words first, then its shop and its NPC class; ordinary townspeople teach nothing. A <c>trades.json</c> in the mod
/// folder replaces it (<see cref="Load"/>, format in the README).
/// </summary>
static class Professions
{
    static readonly StatsEnumerated[] Attributes = [Strength, Dexterity, Toughness, Perception];

    static readonly StatsEnumerated[] Combat =
        [MeleeAttack, MeleeDefence, Dodge, Katanas, Sabres, Hackers, HeavyWeapons, Blunt, Polearms, Crossbows, Turrets, FriendlyFire, Athletics];

    static readonly Profession Fighter = new("fighter", "combat and weapons", Combat);

    /// <summary>The record-name words of each field, most specific first.</summary>
    static readonly (string[] Words, Profession Field)[] ByName =
    [
        (["ninja", "assassin", "shinobi"],
            new("ninja", "stealth and blades", [Stealth, Assassination, MeleeAttack, Dodge, Katanas, MartialArts, Lockpicking, Athletics])),
        (["thief", "smuggler"], new("thief", "thieving and stealth", [Thieving, Lockpicking, Stealth, Dodge, Athletics])),
        (["martial", "monk", "brawler"], new("martial artist", "unarmed fighting", [MartialArts, Dodge, Athletics])),
        (["shooter", "crossbowman", "sniper"], new("shooter", "ranged weapons", [Turrets, Crossbows, FriendlyFire])),
        (["blacksmith", "smith"], new("blacksmith", "smithing", [SmithingWeapon, SmithingArmour])),
        (["doctor", "surgeon", "medic"], new("doctor", "medicine and science", [Medic, Science])),
        (["engineer", "mechanic", "scientist"], new("engineer", "engineering, science, robotics", [Engineering, Science, Robotics])),
        (["bartender", "barman", "baker", "cook", "chef"], new("cook", "cooking and farming", [Cooking, Farming])),
        (["fisher"], new("fisherman", "swimming and cooking", [Swimming, Cooking])),
        (["guard", "soldier", "mercenary", "merc", "bounty hunter", "samurai", "paladin", "warrior", "police", "ronin"], Fighter),
        (["farmer", "peasant", "farmhand"], new("farmer", "farming, cooking, labouring", [Farming, Cooking, Labouring])),
        (["miner", "drone", "labourer", "worker"], new("miner", "labouring and strength", [Labouring, Strength])),
        (["wanderer", "drifter", "nomad"], new("wanderer", "athletics, swimming, dodge", [Athletics, Swimming, Dodge])),
    ];

    /// <summary>Record-name words of a trader; they come first, so a "Ronin Weapon Trader" is a trader, not a fighter.</summary>
    static readonly string[] TraderWords = ["trader", "merchant", "vendor", "bartender", "barman"];

    /// <summary>A trader's extra skill by the goods its record name says it sells.</summary>
    static readonly (string Word, StatsEnumerated[] Skills)[] TraderGoods =
    [
        ("weapon", [SmithingWeapon]), ("armour", [SmithingArmour]), ("armor", [SmithingArmour]), ("crossbow", [SmithingBow, Crossbows]),
        ("mechanic", [Robotics]), ("robot", [Robotics]), ("building", [Engineering]), ("construction", [Engineering]), ("book", [Science]),
        ("bar", [Cooking]), ("food", [Cooking]), ("bak", [Cooking]),
    ];

    /// <summary>The game's 33 trainable skills (record stats, Medic's shared numbers once), attributes first, with the stats window's names.</summary>
    public static readonly Teachable[] All =
    [
        .. Attributes.Select(s => new Teachable(s, Label(s), true)),
        .. StatsInfo.RecordStats.Where(s => s is not (HiveMedic or Vet) && !Attributes.Contains(s)).Select(s => new Teachable(s, Label(s), false)),
    ];

    /// <summary>The <c>trades.json</c> table when one was loaded; null: the heuristic.</summary>
    static TradeTable? s_table;

    /// <summary>The teacher's trade; null when it teaches nothing.</summary>
    public static Profession? Of(string recordName, bool trader, FundamentalType npcClass)
    {
        string name = recordName.ToLowerInvariant();
        if (s_table is { } table) return table.Of(name);
        if (TraderWords.Any(name.Contains)) return Trader(name);

        foreach (var (words, field) in ByName)
        {
            if (words.Any(name.Contains)) return field;
        }

        if (trader || npcClass == FundamentalType.Trader) return Trader(name);
        return npcClass == FundamentalType.LawEnforcement ? Fighter : null;
    }

    /// <summary>Any teacher, every skill: RequireProfession=0.</summary>
    public static Profession Anyone { get; } = new("teacher", "everything", [.. All.Select(t => t.Stat)]);

    static Profession Trader(string name)
    {
        var skills = Attributes.ToList();
        foreach (var (word, extra) in TraderGoods)
        {
            if (name.Contains(word)) skills.AddRange(extra.Where(s => !skills.Contains(s)));
        }

        return new("trader", skills.Count > Attributes.Length ? "attributes and goods" : "attributes", skills);
    }

    /// <summary>The stat as the game's stats window names it.</summary>
    static string Label(StatsEnumerated stat) => stat switch
    {
        Thieving => "Thievery",
        FriendlyFire => "Precision Shooting",
        SmithingWeapon => "Weapon Smith",
        SmithingArmour => "Armour Smith",
        SmithingBow => "Bow Smith",
        Medic => "Field Medic",
        MeleeAttack => "Attack",
        MeleeDefence => "Defence",
        _ => Spaced(stat.ToString()),
    };

    static string Spaced(string name)
    {
        var text = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && text.Length > 0) text.Append(' ');
            text.Append(c);
        }

        return text.ToString();
    }

    /// <summary>Reads <c>trades.json</c> from <paramref name="folder"/> when it exists; a file that does not read leaves the heuristic, logged.</summary>
    public static void Load(string folder, Action<string> log)
    {
        string path = Path.Combine(folder, "trades.json");
        if (!File.Exists(path)) return;

        try
        {
            s_table = TradeTable.Read(path, log);
            log($"trades.json read: {s_table.Count} trades");
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
        {
            log($"trades.json not read, using the built-in trades: {e.Message}");
        }
    }
}

/// <summary>
/// A <c>trades.json</c>: <c>trades</c> (name -> <c>covers</c> text and <c>skills</c>, <see cref="StatsEnumerated"/> names), <c>exact</c> (a whole
/// record name -> trade, checked first) and <c>words</c> (an ordered list of <c>[word, trade]</c>; the first word the lower-case record name
/// contains wins). A name nothing matches teaches nothing.
/// </summary>
sealed class TradeTable
{
    readonly Dictionary<string, Profession> _trades = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Profession> _exact = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(string Word, Profession Trade)> _words = [];

    public int Count => _trades.Count;

    public Profession? Of(string name)
    {
        if (_exact.TryGetValue(name, out var exact)) return exact;

        foreach (var (word, trade) in _words)
            if (name.Contains(word, StringComparison.Ordinal)) return trade;

        return null;
    }

    public static TradeTable Read(string path, Action<string> log)
    {
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        using var doc = JsonDocument.Parse(File.ReadAllText(path), options);
        var table = new TradeTable();
        var root = doc.RootElement;
        if (root.TryGetProperty("trades", out var trades)) table.ReadTrades(trades, log);
        if (root.TryGetProperty("exact", out var exact)) table.ReadExact(exact, log);
        if (root.TryGetProperty("words", out var words)) table.ReadWords(words, log);
        return table;
    }

    void ReadTrades(JsonElement trades, Action<string> log)
    {
        foreach (var entry in trades.EnumerateObject())
        {
            string covers = entry.Value.TryGetProperty("covers", out var c) ? c.GetString() ?? "" : "";
            var skills = new List<StatsEnumerated>();
            if (entry.Value.TryGetProperty("skills", out var list))
            {
                foreach (var s in list.EnumerateArray())
                {
                    if (Enum.TryParse<StatsEnumerated>(s.GetString(), ignoreCase: true, out var stat) && stat != StatsEnumerated.None) skills.Add(stat);
                    else log($"trades.json: unknown skill '{s.GetString()}' in {entry.Name}");
                }
            }

            _trades[entry.Name] = new(entry.Name, covers, skills);
        }
    }

    void ReadExact(JsonElement exact, Action<string> log)
    {
        foreach (var e in exact.EnumerateObject())
            if (Trade(e.Value.GetString(), log) is { } t) _exact[e.Name.ToLowerInvariant()] = t;
    }

    void ReadWords(JsonElement words, Action<string> log)
    {
        foreach (var pair in words.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2 || Trade(pair[1].GetString(), log) is not { } t) continue;

            _words.Add(((pair[0].GetString() ?? "").ToLowerInvariant(), t));
        }
    }

    Profession? Trade(string? name, Action<string> log)
    {
        if (name is not null && _trades.TryGetValue(name, out var trade)) return trade;

        log($"trades.json: unknown trade '{name}'");
        return null;
    }
}
