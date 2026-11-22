using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Items;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.KenshiAffixes;

/// <summary>
/// What an affix changes: a stat by points, a stat by a share of its trained value, a weapon's damage term by a share, or an armour piece's cut or
/// blunt resistance by a share of what it leaves through.
/// </summary>
enum AffixEffect { StatPoints, StatPercent, Cut, Blunt, Bleed, CutResist, BluntResist }

/// <summary>One affix kind of the original's tables: its name, effect, the stat it reads, and its value range from item level 1 to 100.</summary>
sealed record AffixKind(string Name, AffixEffect Effect, StatsEnumerated Stat, float Min, float Max, string Unit);

/// <summary>A rolled affix on an item.</summary>
readonly record struct Affix(AffixKind Kind, float Value)
{
    public string Text => Kind.Effect == AffixEffect.StatPoints
        ? $"{Kind.Name} +{Value:0} {Kind.Unit}"
        : $"{Kind.Name} +{Value:0}% {Kind.Unit}";
}

/// <summary>
/// The affix tables (Kenshi Affixes v0.6.0's description, its ranges) and an item's affixes in the shared tag (<see cref="ItemTag"/>, keys
/// <c>affix</c> as <c>Name:value,Name:value</c> or <c>none</c>, and <c>ilvl</c>).
/// </summary>
static class Affixes
{
    const string Key = "affix", LevelKey = "ilvl";

    public static readonly AffixKind[] Armour =
    [
        new("Guarding", AffixEffect.StatPoints, MeleeDefence, 1, 10, "melee defence"),
        new("Aggressive", AffixEffect.StatPoints, MeleeAttack, 1, 10, "melee attack"),
        new("Fleet", AffixEffect.StatPercent, Athletics, 1, 15, "athletics"),
        new("Silent", AffixEffect.StatPercent, Stealth, 2, 30, "stealth"),
        new("Nimble", AffixEffect.StatPercent, Dexterity, 1, 15, "dexterity"),
        new("Reinforced", AffixEffect.CutResist, None, 2, 25, "remaining cut resistance"),
        new("Cushioned", AffixEffect.BluntResist, None, 2, 25, "remaining blunt resistance"),
    ];

    public static readonly AffixKind[] Weapon =
    [
        new("Keen", AffixEffect.Cut, None, 3, 50, "cut damage"),
        new("Crushing", AffixEffect.Blunt, None, 3, 50, "blunt damage"),
        new("Serrated", AffixEffect.Bleed, None, 4, 60, "bleeding"),
        new("Precise", AffixEffect.StatPoints, MeleeAttack, 1, 15, "melee attack"),
        new("Parrying", AffixEffect.StatPoints, MeleeDefence, 1, 15, "melee defence"),
    ];

    public static readonly string[] ArmourSections = ["head", "shirt", "armour", "legs", "boots"];
    public static readonly string[] WeaponSections = ["hip", "back"];

    /// <summary>A weapon or armour item whose tag this mod may read and write.</summary>
    public static bool Eligible(ItemInstance item) => item.Type is FcsRecordType.Weapon or FcsRecordType.Armour && ItemTag.Ours(item.ModTag);

    public static bool Rolled(ItemInstance item) => ItemTag.Get(item.ModTag, Key) is not null;

    public static int LevelOf(ItemInstance item) => LevelIn(item.ModTag) ?? 0;

    /// <summary>The level a tag records; null when it records no roll.</summary>
    public static int? LevelIn(string tag) => int.TryParse(ItemTag.Get(tag, LevelKey), CultureInfo.InvariantCulture, out int level) ? level : null;

    public static List<Affix> Of(ItemInstance item) => In(item.ModTag, item.Type == FcsRecordType.Weapon ? Weapon : Armour);

    /// <summary>The affixes a tag records (no item type: both tables, whose names are distinct).</summary>
    public static List<Affix> In(string tag) => In(tag, [.. Weapon, .. Armour]);

    static List<Affix> In(string tag, AffixKind[] table)
    {
        var affixes = new List<Affix>();
        if (ItemTag.Get(tag, Key) is not { } text || text == "none") return affixes;

        foreach (var entry in text.Split(','))
        {
            var pair = entry.Split(':');
            if (pair.Length == 2 && table.FirstOrDefault(k => k.Name == pair[0]) is { } kind
                && float.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                affixes.Add(new(kind, value));
        }

        return affixes;
    }

    public static void Write(ItemInstance item, int level, IReadOnlyList<Affix> affixes)
    {
        string text = affixes.Count == 0 ? "none"
            : string.Join(',', affixes.Select(a => a.Kind.Name + ":" + a.Value.ToString("0", CultureInfo.InvariantCulture)));
        item.ModTag = ItemTag.Put(ItemTag.Put(item.ModTag, Key, text), LevelKey, level.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The character's worn armour and its fighting weapon (first weapon on the hip, else the back).</summary>
    public static IEnumerable<ItemInstance> Equipped(Inventory? inventory)
    {
        if (inventory is null) yield break;

        foreach (var item in inventory.Items.Where(i => i.Type == FcsRecordType.Armour && Eligible(i) && ArmourSections.Contains(i.Section)))
            yield return item;
        if (FightingWeapon(inventory) is { } weapon) yield return weapon;
    }

    public static ItemInstance? FightingWeapon(Inventory? inventory) =>
        inventory?.Items.Where(i => i.Type == FcsRecordType.Weapon && Eligible(i) && WeaponSections.Contains(i.Section))
            .OrderBy(i => Array.IndexOf(WeaponSections, i.Section)).FirstOrDefault();
}
