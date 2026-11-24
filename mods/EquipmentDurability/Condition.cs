using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;

namespace Meitou.EquipmentDurability;

/// <summary>
/// The original's ten durability classes (its <c>SafeTypeIndex</c>), indexing <see cref="DurabilityConfig.Classes"/>; <see cref="Other"/> is a weapon
/// of no class, which uses <c>DurabilityRange</c>.
/// </summary>
enum GearClass
{
    Other = -1,
    Katana = 0,
    Sabre = 1,
    Hacker = 2,
    Heavy = 3,
    Blunt = 4,
    Polearm = 5,
    Ranged = 6,
    LightArmour = 7,
    MediumArmour = 8,
    HeavyArmour = 9,
}

/// <summary>
/// An item's durability, an integer from 0 (broken) to its class's cap, kept in <see cref="ItemInstance.ModTag"/> under the key <c>dur</c> of the shared
/// tag (<see cref="ItemTag"/>), so it is saved and copied with the item as the original keeps it in the item's own save record. An item without the key
/// has not rolled yet; one another mod tagged is never tracked.
/// </summary>
sealed class Condition(DurabilityConfig config, PopulationData data)
{
    #region Classes

    /// <summary>
    /// The tag key.
    /// </summary>
    const string Key = "dur";

    /// <summary>
    /// The ARMOUR sections in the original's repair order (helmet, chest armour, shirt, legs, boots).
    /// </summary>
    public static readonly string[] ArmourSections = ["head", "armour", "shirt", "legs", "boots"];

    /// <summary>
    /// The weapon sections in the original's repair order: weapon slot 1 (<c>back</c>) before slot 2 (<c>hip</c>).
    /// </summary>
    public static readonly string[] WeaponSections = ["back", "hip"];

    /// <summary>
    /// The item level each weapon grade starts at (the original's <c>GRADE_LEVEL_WEAPON</c>).
    /// </summary>
    static readonly int[] WeaponGradeLevels = [5, 10, 15, 20, 25, 30, 35, 40, 50, 55, 60, 70, 75, 80];

    /// <summary>
    /// The item level each armour and crossbow grade starts at (the original's <c>GRADE_LEVEL_ARMOUR</c>).
    /// </summary>
    static readonly int[] ArmourGradeLevels = [5, 20, 40, 60, 80, 95];

    /// <summary>
    /// The item's class: armour by its <c>class</c> (medium, heavy, else light: cloth with coverage counts as light), a crossbow ranged, a melee
    /// weapon by its <c>skill category</c>; null when the original leaves it out (not gear, armour off, locked armour, armour without coverage).
    /// </summary>
    public GearClass? ClassOf(string record, FcsRecordType type, bool locked)
    {
        var combat = data.Combat;
        if (type == FcsRecordType.Crossbow) return combat.Crossbow(record) is null ? null : GearClass.Ranged;
        if (type == FcsRecordType.Weapon) return combat.Weapon(record) is { } weapon ? MeleeClass(weapon.SkillCategory) : null;
        if (type != FcsRecordType.Armour || !config.Armour || locked) return null;
        if (combat.Armour(record) is not { Coverage.Count: > 0 } armour) return null;

        return armour.Class switch
        {
            ArmourClass.Medium => GearClass.MediumArmour,
            ArmourClass.Heavy => GearClass.HeavyArmour,
            _ => GearClass.LightArmour,
        };
    }

    /// <summary>
    /// The class of an item in the world.
    /// </summary>
    public GearClass? ClassOf(ItemInstance item) => ClassOf(item.Record, item.Type, item.IsLocked);

    /// <summary>
    /// A melee weapon's class by its skill category.
    /// </summary>
    static GearClass MeleeClass(int category) => (WeaponCategory)category switch
    {
        WeaponCategory.Katanas => GearClass.Katana,
        WeaponCategory.Sabres => GearClass.Sabre,
        WeaponCategory.Hackers => GearClass.Hacker,
        WeaponCategory.Heavy => GearClass.Heavy,
        WeaponCategory.Blunt => GearClass.Blunt,
        WeaponCategory.Polearms => GearClass.Polearm,
        _ => GearClass.Other,
    };

    /// <summary>
    /// Whether the class is an armour class.
    /// </summary>
    public static bool IsArmour(GearClass c) => c >= GearClass.LightArmour;

    /// <summary>
    /// The range of a class (<c>DurabilityRange</c> for a class without one).
    /// </summary>
    public DurabilityRange RangeOf(GearClass c) => c >= 0 && config.Classes[(int)c] is { } range ? range : config.Default;

    /// <summary>
    /// Whether the item has durability: a class, not another mod's tag, and not ignored (its name, material or maker holds an ignored word).
    /// </summary>
    public bool Tracked(ItemInstance item, out GearClass gearClass)
    {
        gearClass = GearClass.Other;
        if (!ItemTag.Ours(item.ModTag) || ClassOf(item) is not { } c) return false;

        gearClass = c;
        return !Ignored(item, IsArmour(c) ? config.IgnoreArmour : config.IgnoreWeapons);
    }

    /// <summary>
    /// The original's <c>IsIgnored</c>: "name | material | maker", lower case, holds one of the words.
    /// </summary>
    bool Ignored(ItemInstance item, IReadOnlyList<string> words)
    {
        if (words.Count == 0) return false;

        var combat = data.Combat;
        string material = combat.WeaponMaterial(item.MaterialId)?.Name ?? "";
        string maker = combat.Manufacturer(item.CompanyId)?.Name ?? "";
        string text = string.Join(" | ", item.Name, material, maker).ToLowerInvariant();
        return words.Any(w => text.Contains(w, StringComparison.Ordinal));
    }

    #endregion

    #region Values

    /// <summary>
    /// The durability a tag records (held to the cap, as the original holds it when the cfg lowered the maximum); null when none was rolled.
    /// </summary>
    public static int? InTag(string tag, int cap) =>
        ItemTag.Get(tag, Key) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, 0, cap)
            : null;

    /// <summary>
    /// Records the durability on the item.
    /// </summary>
    public static void Set(ItemInstance item, int value) =>
        item.ModTag = ItemTag.Put(item.ModTag, Key, Math.Max(0, value).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The diagnostics a roll is written to (null: none).
    /// </summary>
    public VerboseLog? Verbose { get; init; }

    /// <summary>
    /// The item's durability, rolling it first when it has none (a player-crafted item at its cap with <c>CraftedFullDurability</c>), as the original's
    /// <c>GetOrRoll</c>; <paramref name="roll"/> is the 64-bit roll to use.
    /// </summary>
    public int GetOrRoll(ItemInstance item, GearClass c, ulong roll)
    {
        var range = RangeOf(c);
        if (InTag(item.ModTag, range.Cap) is { } value) return value;

        bool full = config.CraftedFull && item.Crafter.Length > 0;
        int rolled = full ? range.Cap : range.Min + Rng.Int(roll, range.Max - range.Min + 1);
        Set(item, rolled);
        Verbose?.Rolled(item, rolled, range, c, full);
        return rolled;
    }

    /// <summary>
    /// The percent chance that a use costs a point (the original's <c>GradeSkipsWear</c>): the grade is the last whose start level the item's level
    /// reaches; 100 with grade wear off or for a weapon of no class.
    /// </summary>
    public int WearChance(ItemInstance item, GearClass c)
    {
        if (!config.GradeWear || c == GearClass.Other) return 100;

        var (levels, chances) = c == GearClass.Ranged ? (ArmourGradeLevels, config.RangedGrades)
            : IsArmour(c) ? (ArmourGradeLevels, config.ArmourGrades)
            : (WeaponGradeLevels, config.WeaponGrades);
        int grade = 0;
        for (int i = 0; i < levels.Length; i++)
            if (item.Level >= levels[i]) grade = i;

        return chances[grade];
    }

    #endregion

    #region Effects

    /// <summary>
    /// A weapon's damage factor (the original's <c>DebuffMult</c>): 1 + bonus above the bonus start, else from the debuff start falling in a straight
    /// line to the minimum at 0.
    /// </summary>
    public float DamageFactor(int dur, int cap) =>
        Factor(dur, cap, config.Bonus, config.BonusStart, config.BonusDamage, config.Debuff, config.DebuffStart, config.MinDamage);

    /// <summary>
    /// An armour piece's cut and blunt factor (the original's <c>ArmourMult</c>), the same shape with the armour settings.
    /// </summary>
    public float ResistFactor(int dur, int cap) => Factor(dur, cap, config.ArmourBonus, config.ArmourBonusStart, config.ArmourBonusPercent,
        config.ArmourDebuff, config.ArmourDebuffStart, config.ArmourMinResist);

    /// <summary>
    /// The shared factor shape.
    /// </summary>
    static float Factor(int dur, int cap, bool bonus, float bonusStart, float bonusPercent, bool debuff, float debuffStart, float minPercent)
    {
        if (bonus && cap > 0 && dur * 100f > cap * bonusStart) return 1 + bonusPercent / 100;
        if (!debuff) return 1;

        float start = cap * debuffStart / 100;
        if (start <= 0 || dur >= start) return 1;

        float min = minPercent / 100;
        return min + (1 - min) * Math.Max(0, dur / start);
    }

    /// <summary>
    /// A crossbow's reload change in seconds (the original's <c>ReloadDeltaFor</c>): up to + the penalty below the debuff start, - the bonus above the
    /// bonus start. Shown in the tooltip; the engine has no reload hook to apply it.
    /// </summary>
    public float ReloadDelta(int dur, int cap)
    {
        float delta = 0;
        float start = cap * config.DebuffStart / 100;
        if (config.Debuff && start > 0 && dur < start) delta += config.ReloadPenalty * (1 - dur / start);
        if (config.Bonus && cap > 0 && dur * 100f > cap * config.BonusStart) delta -= config.ReloadBonus;

        return delta;
    }

    #endregion
}
