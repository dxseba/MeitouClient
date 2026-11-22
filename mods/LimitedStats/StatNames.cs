using Meitou.Data.Gameplay.Bodies;

namespace Meitou.LimitedStats;

/// <summary>
/// The 33 stats the original caps and their names in its ini (<c>strength</c>, <c>melee_attack</c>...), in the original's own list order, which is
/// also the order its skill pool is drawn from (<b>Verified</b>, original source and DLL strings).
/// </summary>
static class StatNames
{
    public static readonly (string Name, StatsEnumerated Stat)[] All =
    [
        ("strength", StatsEnumerated.Strength), ("melee_attack", StatsEnumerated.MeleeAttack), ("labouring", StatsEnumerated.Labouring),
        ("science", StatsEnumerated.Science), ("engineer", StatsEnumerated.Engineering), ("robotics", StatsEnumerated.Robotics),
        ("weapon_smith", StatsEnumerated.SmithingWeapon), ("armour_smith", StatsEnumerated.SmithingArmour), ("field_medic", StatsEnumerated.Medic),
        ("thievery", StatsEnumerated.Thieving), ("turrets", StatsEnumerated.Turrets), ("farming", StatsEnumerated.Farming),
        ("cooking", StatsEnumerated.Cooking), ("stealth", StatsEnumerated.Stealth), ("athletics", StatsEnumerated.Athletics),
        ("dexterity", StatsEnumerated.Dexterity), ("melee_defence", StatsEnumerated.MeleeDefence), ("toughness", StatsEnumerated.Toughness),
        ("assassination", StatsEnumerated.Assassination), ("swimming", StatsEnumerated.Swimming), ("perception", StatsEnumerated.Perception),
        ("katanas", StatsEnumerated.Katanas), ("sabres", StatsEnumerated.Sabres), ("hackers", StatsEnumerated.Hackers),
        ("heavy_weapons", StatsEnumerated.HeavyWeapons), ("blunt", StatsEnumerated.Blunt), ("martial_arts", StatsEnumerated.MartialArts),
        ("dodge", StatsEnumerated.Dodge), ("polearms", StatsEnumerated.Polearms), ("crossbows", StatsEnumerated.Crossbows),
        ("precision_shooting", StatsEnumerated.FriendlyFire), ("lockpicking", StatsEnumerated.Lockpicking),
        ("crossbow_smith", StatsEnumerated.SmithingBow),
    ];

    /// <summary>Array length for per-stat tables indexed by stat number.</summary>
    public static readonly int Count = (int)StatsEnumerated.End;

    /// <summary>The stats in list order.</summary>
    public static readonly StatsEnumerated[] Stats = [.. All.Select(a => a.Stat)];

    /// <summary>Names longest first, so a race token such as <c>melee_attack1.2</c> is not read as a shorter name.</summary>
    static readonly (string Name, StatsEnumerated Stat)[] Longest = [.. All.OrderByDescending(a => a.Name.Length)];

    /// <summary>The stat an ini name names (case and surrounding blanks ignored), else <see cref="StatsEnumerated.None"/>.</summary>
    public static StatsEnumerated Find(string name)
    {
        string key = name.Trim();
        foreach (var (n, stat) in All)
            if (string.Equals(n, key, StringComparison.OrdinalIgnoreCase)) return stat;

        return StatsEnumerated.None;
    }

    /// <summary>A race token, a stat name followed by a number (<c>farming 1.0</c>, <c>farming1.0</c>): the stat and the number, else none.</summary>
    public static (StatsEnumerated Stat, float Value)? FindWithNumber(string token)
    {
        foreach (var (n, stat) in Longest)
        {
            if (token.Length <= n.Length || !token.StartsWith(n, StringComparison.OrdinalIgnoreCase)) continue;

            if (Ini.LeadingNumber(token[n.Length..]) is { } value) return (stat, value);
        }

        return null;
    }

    public static string NameOf(StatsEnumerated stat) => All.FirstOrDefault(a => a.Stat == stat).Name ?? stat.ToString();
}
