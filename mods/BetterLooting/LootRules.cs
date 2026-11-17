using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation.Items;

namespace Meitou.BetterLooting;

public enum LootCategory : byte
{
    Weapons, Armour, Food, Medicine, Tools, Materials, Blueprints, Ammo, Robotics, Books, Narcotics, SeveredLimbs, Other,
}

public enum LootDecision : byte { Loot, SkipCategory, SkipStolen, SkipMinValue, SkipValuePerKg }

/// <summary>
/// The original's rules (BetterLootingRules.h <c>IsAllowedLootCategory</c>, <c>EvaluateLootDecision</c>) over Meitou's item records: the category
/// from the item function first (docs/formats/fcs-mod.md "Item categories"), a research artifact counting as a book, then materials, then the
/// record type.
/// </summary>
static class LootRules
{
    /// <summary>An ITEM's <c>item function</c> codes.</summary>
    const int FirstAid = 1, MedRigging = 2, Food = 3, Weapon = 5, Clothing = 6, Narcotic = 8, Tool = 9, Blueprint = 11, RobotRepair = 12, Book = 13,
        RestrictedFood = 15, Ammo = 16, SeveredLimb = 17;

    public static LootCategory CategoryOf(ItemInstance item, GameDatabase db)
    {
        int function = item.Type == FcsRecordType.Item && db.Find(item.Record)?.GetBool("artifact") == true ? Book : item.ItemFunction;
        return FunctionCategory(function) ?? (IsMaterial(item) ? LootCategory.Materials : TypeCategory(item.Type));
    }

    static LootCategory? FunctionCategory(int function) => function switch
    {
        Weapon => LootCategory.Weapons,
        Clothing => LootCategory.Armour,
        Food or RestrictedFood => LootCategory.Food,
        FirstAid or MedRigging => LootCategory.Medicine,
        Tool => LootCategory.Tools,
        Blueprint => LootCategory.Blueprints,
        Ammo => LootCategory.Ammo,
        RobotRepair => LootCategory.Robotics,
        Book => LootCategory.Books,
        Narcotic => LootCategory.Narcotics,
        SeveredLimb => LootCategory.SeveredLimbs,
        _ => null,
    };

    /// <summary>
    /// The stand-in for the original's resource test (<c>Inventory::getResourceItems</c>): an ITEM with no function, the raw materials, trade goods
    /// and building materials.
    /// </summary>
    static bool IsMaterial(ItemInstance item) => item.Type == FcsRecordType.Item && item.ItemFunction == 0;

    static LootCategory TypeCategory(FcsRecordType type) => type switch
    {
        FcsRecordType.Weapon or FcsRecordType.Crossbow => LootCategory.Weapons,
        FcsRecordType.Armour => LootCategory.Armour,
        FcsRecordType.Blueprint => LootCategory.Blueprints,
        FcsRecordType.LimbReplacement => LootCategory.Robotics,
        _ => LootCategory.Other,
    };

    /// <summary>The rules in the original's order: category, stolen, value per unit, value per kilogram (only above 0.001 kg).</summary>
    public static LootDecision Decide(LootSettings settings, LootCategory category, bool stolen, int value, float weight)
    {
        if (!settings.Allows(category)) return LootDecision.SkipCategory;
        if (stolen && !settings.AllowStolen) return LootDecision.SkipStolen;
        if (value < settings.MinValue) return LootDecision.SkipMinValue;
        if (weight > 0.001f && value / weight < settings.MinValuePerKg) return LootDecision.SkipValuePerKg;

        return LootDecision.Loot;
    }

    /// <summary>The Best items first order's key, highest first: value per kilogram, a weightless item as its value times a million.</summary>
    public static float Rank(int value, float weight) => weight > 0.001f ? value / weight : value * 1_000_000f;
}
