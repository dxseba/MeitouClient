using System.Text;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.Mentors;

/// <summary>A teacher's field: its name and the skills it teaches.</summary>
sealed record Profession(string Name, IReadOnlyList<StatsEnumerated> Skills);

/// <summary>
/// Who teaches what, from the original's description: matched on words in the teacher's CHARACTER record name (the record says what the NPC
/// is: "Holy Weapon Trader", "Temple Guard", "Weapon Smith"...), trader words first, then its shop and its NPC class. Ordinary townspeople,
/// slaves and nobles teach nothing.
/// </summary>
static class Professions
{
    static readonly StatsEnumerated[] Attributes = [Strength, Dexterity, Toughness, Perception];

    static readonly StatsEnumerated[] Combat =
        [MeleeAttack, MeleeDefence, Dodge, Katanas, Sabres, Hackers, HeavyWeapons, Blunt, Polearms, Crossbows, Turrets, FriendlyFire, Athletics];

    static readonly Profession Fighter = new("Fighter", Combat);

    /// <summary>The record-name words of each field, most specific first.</summary>
    static readonly (string[] Words, Profession Field)[] ByName =
    [
        (["ninja", "assassin", "shinobi"], new("Ninja", [Stealth, Assassination, MeleeAttack, Dodge, Katanas, MartialArts, Lockpicking, Athletics])),
        (["thief", "smuggler"], new("Thief", [Thieving, Lockpicking, Stealth, Dodge, Athletics])),
        (["martial", "monk", "brawler"], new("Martial artist", [MartialArts, Dodge, Athletics])),
        (["shooter", "crossbowman", "sniper"], new("Shooter", [Turrets, Crossbows, FriendlyFire])),
        (["blacksmith", "smith"], new("Blacksmith", [SmithingWeapon, SmithingArmour])),
        (["doctor", "surgeon", "medic"], new("Doctor", [Medic, Science])),
        (["engineer", "mechanic", "scientist"], new("Engineer", [Engineering, Science, Robotics])),
        (["bartender", "barman", "baker", "cook", "chef"], new("Cook", [Cooking, Farming])),
        (["fisher"], new("Fisherman", [Swimming, Cooking])),
        (["guard", "soldier", "mercenary", "merc", "bounty hunter", "samurai", "paladin", "warrior", "police", "ronin"], Fighter),
        (["farmer", "peasant", "farmhand"], new("Farmer", [Farming, Cooking, Labouring])),
        (["miner", "drone", "labourer", "worker"], new("Miner", [Labouring, Strength])),
        (["wanderer", "drifter", "nomad"], new("Wanderer", [Athletics, Swimming, Dodge])),
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

    /// <summary>The teacher's field; null when it teaches nothing.</summary>
    public static Profession? Of(string recordName, bool trader, FundamentalType npcClass)
    {
        string name = recordName.ToLowerInvariant();
        if (TraderWords.Any(name.Contains)) return Trader(name);

        foreach (var (words, field) in ByName)
        {
            if (words.Any(name.Contains)) return field;
        }

        if (trader || npcClass == FundamentalType.Trader) return Trader(name);
        if (npcClass == FundamentalType.LawEnforcement) return Fighter;
        return null;
    }

    /// <summary>Any teacher, any skill: the original's RequireProfession=0.</summary>
    public static Profession Anyone { get; } = new("Teacher", [.. StatsInfo.RecordStats.Where(s => s is not (HiveMedic or Vet))]);

    static Profession Trader(string name)
    {
        var skills = Attributes.ToList();
        foreach (var (word, extra) in TraderGoods)
        {
            if (name.Contains(word)) skills.AddRange(extra.Where(s => !skills.Contains(s)));
        }

        return new("Trader", skills);
    }

    public static bool IsAttribute(StatsEnumerated stat) => Attributes.Contains(stat);

    /// <summary>The stat as the game's stats window names it.</summary>
    public static string Label(StatsEnumerated stat) => stat switch
    {
        Thieving => "Thievery",
        FriendlyFire => "Precision Shooting",
        SmithingWeapon => "Weapon Smith",
        SmithingArmour => "Armour Smith",
        SmithingBow => "Bow Smith",
        Medic => "Field Medic",
        MeleeDefence => "Melee Defence",
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
}
