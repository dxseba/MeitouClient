using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.World;
using Meitou.Simulation.Combat;

namespace Meitou.GearCompareAlt;

/// <summary>How a property's value is printed (the original's row kinds 0 to 5).</summary>
enum Fmt
{
    /// <summary>A fraction shown as a whole percent.</summary>
    Percent,
    /// <summary>A multiplier, x1.20; its change in percent.</summary>
    Mult,
    /// <summary>A signed whole bonus.</summary>
    Signed,
    /// <summary>Kilograms with one decimal.</summary>
    Kg,
    /// <summary>A whole number.</summary>
    Int,
    /// <summary>A number with two decimals.</summary>
    Number,
}

/// <summary>
/// One property of an item: its label (an English message of the original's catalogue), how it prints, which way is better (1 higher, -1 lower,
/// 0 neither: not counted) and its value.
/// </summary>
readonly record struct Prop(string Label, Fmt Format, int Dir, float Value, string Suffix = "");

/// <summary>
/// What an item brings to a comparison: its kind, its main axis (label and value), its weight and its properties.
/// </summary>
sealed record GearStats(GearKind Kind, string MainLabel, float Main, float Weight, IReadOnlyList<Prop> Props);

/// <summary>The kinds of gear the panel compares.</summary>
enum GearKind { Armour, Weapon, Crossbow, Limb }

/// <summary>
/// The properties of armour, weapons, crossbows and prosthetics, from the merged data at the item's level (README "Facts"): the same numbers the
/// game's tooltip prints, read from the records rather than from the tooltip's text.
/// </summary>
static class GearReader
{
    /// <summary>The weather kinds an armour's protection rows name, in the original's order.</summary>
    static readonly (WeatherAffecting Kind, string Label)[] Weather =
    [
        ((WeatherAffecting)2, "Acid protection"), ((WeatherAffecting)4, "Gas protection"), ((WeatherAffecting)3, "Burning protection"),
        ((WeatherAffecting)1, "Dust storm protection"), ((WeatherAffecting)5, "Rain protection"),
    ];

    /// <summary>The kind of the record's gear; null for anything the panel does not compare.</summary>
    public static GearKind? KindOf(GameRecord? record) => record?.Type switch
    {
        FcsRecordType.Armour => GearKind.Armour,
        FcsRecordType.Weapon => GearKind.Weapon,
        FcsRecordType.Crossbow => GearKind.Crossbow,
        FcsRecordType.LimbReplacement => GearKind.Limb,
        _ => null,
    };

    /// <summary>The item's properties; null when the record is not gear or the data lacks it.</summary>
    public static GearStats? Read(GameDatabase db, CombatDatabase combat, GearItem item)
    {
        var record = db.Find(item.Record);
        var stats = KindOf(record) switch
        {
            GearKind.Armour when combat.Armour(item.Record) is { } a => Armour(db, a, item),
            GearKind.Weapon when combat.Weapon(item.Record) is { } w => Weapon(db, combat, w, item),
            GearKind.Crossbow when combat.Crossbow(item.Record) is { } c => Crossbow(db, combat, c, record!, item),
            GearKind.Limb => Limb(record!, item),
            _ => null,
        };
        return stats is null ? null : stats with { Props = [.. stats.Props, .. Common(item)] };
    }

    /// <summary>The rows every item has (the original's last two): its weight (lower is better, but not counted) and price (neither).</summary>
    static IEnumerable<Prop> Common(GearItem item)
    {
        yield return new("Weight", Fmt.Kg, -1, item.Weight);
        if (item.Price is { } price) yield return new("Price", Fmt.Int, 0, price);
    }

    static GearStats Armour(GameDatabase db, ArmourData a, GearItem item)
    {
        var piece = ArmourPiece.Create(a, ArmourPiece.QualityOf(a, item.Level), ArmourPiece.Graded(a, item.Level));
        float t = ArmourPiece.QualityOf(a, item.Level) + a.LevelBonus * 0.01f;
        var props = new List<Prop>
        {
            new("Cut resistance", Fmt.Percent, 1, piece.CutDefence),
            new("Blunt resistance", Fmt.Percent, 1, piece.BluntDefence),
            new("Harpoon resistance", Fmt.Number, 1, piece.PierceDefence),
        };
        if (piece.CutIntoStun > 0) props.Add(new("Cut resistance efficiency", Fmt.Percent, 1, 1 - piece.CutIntoStun));

        float main = 0;
        foreach (var part in a.Coverage)
        {
            float fraction = part.Percent * 0.01f;
            props.Add(new("Coverage: ", Fmt.Percent, 1, fraction, db.Find(part.PartStringId)?.ShownName() ?? part.PartStringId));
            main += (piece.CutDefence + piece.BluntDefence) * 0.5f * fraction;
        }

        foreach (var (kind, label) in Weather)
            props.Add(new(label, Fmt.Percent, 1, a.Protects(kind) ? a.WeatherProtectionAmount : 0));

        AddArmourSkills(props, a, t, item.Level);
        return new GearStats(GearKind.Armour, "Protection (with coverage)", main, item.Weight, props);
    }

    static void AddArmourSkills(List<Prop> props, ArmourData a, float t, int level)
    {
        props.Add(new("Attack", Fmt.Signed, 1, a.CombatAttackBonus));
        props.Add(new("Defence", Fmt.Signed, 1, a.CombatDefenceBonus));
        props.Add(new("Perception", Fmt.Signed, 1, a.PerceptionBonus));
        props.Add(new("Martial arts", Fmt.Signed, 1, a.UnarmedBonus));
        props.Add(new("Stealth", Fmt.Mult, 1, ArmourPiece.StealthOf(a, t, ArmourPiece.Graded(a, level))));
        props.Add(new("Assassination", Fmt.Mult, 1, a.AssassinationMult));
        props.Add(new("Athletics", Fmt.Mult, 1, ArmourPiece.AthleticsOf(a, t)));
        props.Add(new("Dexterity", Fmt.Mult, 1, a.DexterityMult));
        props.Add(new("Dodge", Fmt.Mult, 1, a.DodgeMult));
        props.Add(new("Combat speed", Fmt.Mult, 1, ArmourPiece.CombatSpeedOf(a, t)));
        props.Add(new("Damage", Fmt.Mult, 1, a.DamageOutputMult));
        props.Add(new("Ranged skills", Fmt.Mult, 1, a.RangedSkillMult));
        props.Add(new("Fist injuries", Fmt.Mult, -1, a.FistInjuryMult));
    }

    static GearStats Weapon(GameDatabase db, CombatDatabase combat, WeaponData w, GearItem item)
    {
        var maker = combat.Manufacturer(item.Company) ?? WeaponManufacturer.Neutral;
        var material = combat.WeaponMaterial(item.Material) ?? WeaponMaterial.None;
        var stats = WeaponStats.Compute(item.Level * 0.01f, w, maker, material, combat.Constants);
        var props = new List<Prop>
        {
            new("Cut damage", Fmt.Number, 1, stats.CutMultiplier),
            new("Blunt damage", Fmt.Number, 1, stats.BluntMultiplier),
            new("Bleeding", Fmt.Number, 1, w.BleedMult),
            new("Attack", Fmt.Signed, 1, stats.AttackMod),
            new("Defence", Fmt.Signed, 1, stats.DefenceMod),
            new("Indoors", Fmt.Signed, 1, stats.IndoorsMod),
            new("Armour penetration", Fmt.Percent, 1, w.ArmourPenetration),
        };
        AddRaceDamage(db, props, w.RobotDamageMult, w.HumanDamageMult, w.AnimalDamageMult, w.RaceDamage);
        props.Add(new("Weapon length", Fmt.Int, 0, w.Length));
        props.Add(new("Combat weight", Fmt.Number, 0, stats.Weight));
        return new GearStats(GearKind.Weapon, "Damage (cut + blunt)", stats.CutMultiplier + stats.BluntMultiplier, item.Weight, props);
    }

    static GearStats Crossbow(GameDatabase db, CombatDatabase combat, CrossbowData c, GameRecord record, GearItem item)
    {
        var gun = CrossbowInstance.Create(item.Level * 0.01f, c, combat.Constants, 1).Stats;
        float damage = (gun.PierceMin + gun.PierceMax) * 0.5f;
        var props = new List<Prop>
        {
            new("Damage", Fmt.Number, 1, damage),
            new("Range", Fmt.Number, 1, gun.Range),
            new("Shot speed", Fmt.Number, 1, gun.ShotSpeed),
            new("Aim speed", Fmt.Number, 1, record.GetFloat("aim speed", 1)),
            new("Spread", Fmt.Number, -1, gun.AccuracyDeviation),
            new("Reload time", Fmt.Number, -1, (gun.ReloadMin + gun.ReloadMax) * 0.5f),
            new("Shots", Fmt.Number, 1, Math.Max(c.NumShots, 1)),
            new("Bleeding", Fmt.Number, 1, c.BleedMult),
        };
        AddRaceDamage(db, props, c.RobotDamageMult, c.HumanDamageMult, c.AnimalDamageMult, null);
        props.Add(new("Combat weight", Fmt.Number, 0, item.Weight));
        return new GearStats(GearKind.Crossbow, "Damage (per shot)", damage, item.Weight, props);
    }

    static void AddRaceDamage(GameDatabase db, List<Prop> props, float robot, float human, float animal, IReadOnlyDictionary<string, int>? races)
    {
        props.Add(new("Damage vs robots", Fmt.Mult, 1, robot));
        props.Add(new("Damage vs humans", Fmt.Mult, 1, human));
        props.Add(new("Damage vs animals", Fmt.Mult, 1, animal));
        if (races is null) return;

        foreach (var (race, percent) in races.OrderBy(r => r.Key, StringComparer.Ordinal))
            props.Add(new("Damage vs", Fmt.Mult, 1, percent * 0.01f, db.Find(race)?.ShownName() ?? race));
    }

    static GearStats Limb(GameRecord record, GearItem item)
    {
        var limb = LimbReplacement.From(record);
        float t = Math.Clamp(item.Level * 0.01f, 0, 1);
        var props = new List<Prop>
        {
            new("Limb HP", Fmt.Int, 1, MathF.Floor(limb.Hp(t))),
            new("Unarmed damage", Fmt.Signed, 1, limb.UnarmedDamageBonus(t)),
        };
        foreach (var stat in Enum.GetValues<StatsEnumerated>())
        {
            if (limb.Multiplier(stat, t) is var m && m != 1) props.Add(new(stat.ToString(), Fmt.Mult, 1, m));
        }

        return new GearStats(GearKind.Limb, "Limb HP", MathF.Floor(limb.Hp(t)), item.Weight, props);
    }
}

/// <summary>An item as compared: its record, model (weapon material), manufacturer, level and weight (one unit), its shown name and price.</summary>
readonly record struct GearItem(string Record, string Material, string Company, int Level, float Weight, string Name)
{
    /// <summary>One unit's value as the game's tooltip prices it; null when unknown.</summary>
    public int? Price { get; init; }
}
