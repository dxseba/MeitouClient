using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Simulation.Items;
using AttachSlot = Meitou.Data.Characters.AttachSlot;

namespace Meitou.KenshiAffixes;

/// <summary>The gear an affix goes on: melee weapons, wearable armour, or both (the original's kinds 0, 1, 2).</summary>
enum AffixGear { Melee, Armour, Both }

/// <summary>The item value an affix changes (the original's fields: the gear's own numbers, then the virtual combat and skill effects).</summary>
enum AffixField
{
    CutDamage, BluntDamage, BleedDamage, ModAttack, ModDefence, ArmourDefence, ArmourAttack, AthleticsMult, StealthMult, DexterityMult,
    CutResistance, BluntResistance, LifeSteal, CritChance, CritDamage, DodgeBonus, UnarmedBonus,
}

/// <summary>How the value applies: x (1 + v), + whole v, + (1 - base) x v, a combat effect, or a skill bonus.</summary>
enum AffixOp { Multiply, AddInt, Resistance, Combat, Skill }

/// <summary>
/// One affix of the original's table (config/affixes.json): its stable id, shown name and effect, gear, field, operation, value range from item
/// level 1 (or <paramref name="MinLevel"/>) to 100, whether the gear's base value must be above 0, and its pick weights.
/// </summary>
sealed record AffixKind(string Id, string Name, string Effect, AffixGear Gear, AffixField Field, AffixOp Op, float Low, float High,
    bool Positive = false, int MinLevel = 1, int Weight = 100, int ArmourWeight = 100)
{
    public bool Fits(bool armour) => Gear == AffixGear.Both || Gear == (armour ? AffixGear.Armour : AffixGear.Melee);

    public int WeightFor(bool armour) => armour ? ArmourWeight : Weight;

    public bool Whole => Op is AffixOp.AddInt or AffixOp.Skill;
}

/// <summary>A rolled affix on an item: its kind and value (a fraction for a share, a whole number for points).</summary>
readonly record struct Affix(AffixKind Kind, float Value)
{
    /// <summary>The original's tooltip value: "+7" for points, "+12.3%" for a share.</summary>
    public string ValueText => Kind.Whole
        ? "+" + ((int)Value).ToString(CultureInfo.InvariantCulture)
        : "+" + (Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The original's affix line: "[RARE] " for a combat effect, the name and the value.</summary>
    public string Text => (Kind.Op == AffixOp.Combat ? "[RARE] " : "") + Kind.Name + " " + ValueText;
}

/// <summary>The affix table, in the original's order (Kenshi Affixes v0.6.0 config/affixes.json; the English halves of its names).</summary>
static class AffixTable
{
    public static readonly AffixKind[] All =
    [
        new("keen", "Keen", "Cut damage", AffixGear.Melee, AffixField.CutDamage, AffixOp.Multiply, 0.03f, 0.5f, Positive: true),
        new("crushing", "Crushing", "Blunt damage", AffixGear.Melee, AffixField.BluntDamage, AffixOp.Multiply, 0.03f, 0.5f, Positive: true),
        new("serrated", "Serrated", "Bleed multiplier", AffixGear.Melee, AffixField.BleedDamage, AffixOp.Multiply, 0.04f, 0.6f, Positive: true),
        new("precise", "Precise", "Melee attack", AffixGear.Melee, AffixField.ModAttack, AffixOp.AddInt, 1, 15),
        new("parrying", "Parrying", "Melee defence", AffixGear.Melee, AffixField.ModDefence, AffixOp.AddInt, 1, 15),
        new("guarding", "Guarding", "Melee defence", AffixGear.Armour, AffixField.ArmourDefence, AffixOp.AddInt, 1, 10),
        new("aggressive", "Aggressive", "Melee attack", AffixGear.Armour, AffixField.ArmourAttack, AffixOp.AddInt, 1, 10),
        new("fleet", "Fleet", "Athletics multiplier", AffixGear.Armour, AffixField.AthleticsMult, AffixOp.Multiply, 0.01f, 0.15f, Positive: true),
        new("silent", "Silent", "Stealth multiplier", AffixGear.Armour, AffixField.StealthMult, AffixOp.Multiply, 0.02f, 0.3f, Positive: true),
        new("nimble", "Nimble", "Dexterity multiplier", AffixGear.Armour, AffixField.DexterityMult, AffixOp.Multiply, 0.01f, 0.15f, Positive: true),
        new("reinforced", "Reinforced", "Remaining cut resistance", AffixGear.Armour, AffixField.CutResistance, AffixOp.Resistance, 0.02f, 0.25f,
            Positive: true),
        new("cushioned", "Cushioned", "Remaining blunt resistance", AffixGear.Armour, AffixField.BluntResistance, AffixOp.Resistance, 0.02f, 0.25f,
            Positive: true),
        new("leeching", "Leeching", "Melee damage restores stun only (no cut healing, total cap 10%)", AffixGear.Both, AffixField.LifeSteal,
            AffixOp.Combat, 0, 0.1f, MinLevel: 61, Weight: 15, ArmourWeight: 15),
        new("deadly", "Deadly", "Melee critical chance (base 0%, total cap 50%)", AffixGear.Both, AffixField.CritChance, AffixOp.Combat, 0, 0.1f,
            MinLevel: 61, Weight: 15, ArmourWeight: 3),
        new("devastating", "Devastating", "Melee critical damage bonus (base x1.5, total cap x2)", AffixGear.Both, AffixField.CritDamage,
            AffixOp.Combat, 0, 0.5f, MinLevel: 61, Weight: 15, ArmourWeight: 3),
        new("evasive", "Evasive", "Dodge bonus (stacks across worn armour)", AffixGear.Armour, AffixField.DodgeBonus, AffixOp.Skill, 1, 10),
        new("brawler", "Brawler", "Martial arts bonus (stacks across worn armour)", AffixGear.Armour, AffixField.UnarmedBonus, AffixOp.Skill, 1, 10),
    ];

    /// <summary>The affix count range of each of the six quality bands.</summary>
    public static readonly (int Low, int High)[] Slots = [(0, 1), (0, 1), (1, 2), (1, 2), (2, 3), (3, 3)];

    /// <summary>The ordinary roll's item level jitter around its source level.</summary>
    public const int LevelLow = -5, LevelHigh = 3;

    public static AffixKind? Find(string id) => All.FirstOrDefault(k => k.Id == id);
}

/// <summary>
/// An item's roll as kept in its tag (<see cref="ItemTag"/>): <c>affix</c> as <c>id:value,id:value</c> or <c>none</c>, <c>ilvl</c>, and
/// <c>aprof</c> for an authored profile (a fixed NPC's or a Meitou's). <c>aplain</c> marks a plain item seen in the player's hands, which is
/// never rolled later (the original keeps such gear plain when it is sold or stored). Rolls of the first port (<c>Keen:12</c>, percent) still read.
/// </summary>
static class AffixTag
{
    const string Key = "affix", LevelKey = "ilvl", ProfileKey = "aprof", PlainKey = "aplain", TestKey = "atest";

    public static bool Rolled(string tag) => ItemTag.Get(tag, Key) is not null;

    public static bool Plain(string tag) => ItemTag.Get(tag, PlainKey) is not null;

    public static string? Profile(string tag) => ItemTag.Get(tag, ProfileKey);

    /// <summary>The level a tag records; null when it records no roll.</summary>
    public static int? Level(string tag) => int.TryParse(ItemTag.Get(tag, LevelKey), CultureInfo.InvariantCulture, out int level) ? level : null;

    public static List<Affix> Read(string tag)
    {
        var affixes = new List<Affix>();
        if (ItemTag.Get(tag, Key) is not { } text || text == "none") return affixes;

        foreach (var entry in text.Split(','))
        {
            if (Parse(entry) is { } affix) affixes.Add(affix);
        }

        return affixes;
    }

    static Affix? Parse(string entry)
    {
        var pair = entry.Split(':');
        if (pair.Length != 2 || !float.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return null;
        if (AffixTable.Find(pair[0]) is { } kind) return new(kind, value);

        // The first port wrote the English name and shares in percent.
        if (AffixTable.All.FirstOrDefault(k => k.Name == pair[0]) is not { } old) return null;
        return new(old, old.Whole ? value : value / 100);
    }

    public static void Write(ItemInstance item, int level, IReadOnlyList<Affix> affixes, string? profile = null)
    {
        string text = affixes.Count == 0 ? "none"
            : string.Join(',', affixes.Select(a => a.Kind.Id + ":" + a.Value.ToString("0.######", CultureInfo.InvariantCulture)));
        string tag = ItemTag.Put(ItemTag.Put(item.ModTag, Key, text), LevelKey, level.ToString(CultureInfo.InvariantCulture));
        item.ModTag = profile is null ? ItemTag.Remove(tag, ProfileKey) : ItemTag.Put(tag, ProfileKey, profile);
    }

    public static void MarkPlain(ItemInstance item) => item.ModTag = ItemTag.Put(item.ModTag, PlainKey, "1");

    /// <summary>A test roll (the original's test button): the tooltip shows "[TEST] "; the plain mark goes.</summary>
    public static void MarkTest(ItemInstance item) => item.ModTag = ItemTag.Remove(ItemTag.Put(item.ModTag, TestKey, "1"), PlainKey);

    public static bool Test(string tag) => ItemTag.Get(tag, TestKey) is not null;
}

/// <summary>Which items take affixes and where a character wears them.</summary>
static class Wearing
{
    /// <summary>The original's wearable armour slots (its <c>kindOf</c>): body, legs, hat, boots, shirt, belt, eyes, gloves, neck.</summary>
    static readonly AttachSlot[] ArmourSlots =
        [AttachSlot.Body, AttachSlot.Legs, AttachSlot.Hat, AttachSlot.Boots, AttachSlot.Shirt, AttachSlot.Belt, AttachSlot.Eyes, AttachSlot.Gloves,
            AttachSlot.Neck];

    public static readonly string[] WeaponSections = ["hip", "back"];

    /// <summary>
    /// A melee weapon or a wearable armour piece, single, not locked, not a death item, whose tag this mod may read and write (the original's
    /// <c>kindOf</c>).
    /// </summary>
    public static bool Eligible(ItemInstance item) =>
        item.Quantity == 1 && !item.IsLocked && !item.Death && ItemTag.Ours(item.ModTag)
        && (item.Type == FcsRecordType.Weapon || item.Type == FcsRecordType.Armour && ArmourSlots.Contains((AttachSlot)item.AttachSlot));

    public static bool Equipped(ItemInstance item) => InventoryCommands.IsEquipmentSection(item.Section);

    /// <summary>The character's worn armour pieces.</summary>
    public static IEnumerable<ItemInstance> WornArmour(Inventory? inventory) =>
        inventory?.Items.Where(i => i.Type == FcsRecordType.Armour && Eligible(i) && Equipped(i)) ?? [];

    /// <summary>The weapon the character fights with: the first melee weapon on the hip, else on the back.</summary>
    public static ItemInstance? FightingWeapon(Inventory? inventory) =>
        inventory?.Items.Where(i => i.Type == FcsRecordType.Weapon && Eligible(i) && WeaponSections.Contains(i.Section))
            .OrderBy(i => Array.IndexOf(WeaponSections, i.Section)).FirstOrDefault();

    /// <summary>The worn armour and the fighting weapon.</summary>
    public static IEnumerable<ItemInstance> Equipment(Inventory? inventory)
    {
        foreach (var item in WornArmour(inventory)) yield return item;
        if (FightingWeapon(inventory) is { } weapon) yield return weapon;
    }
}
