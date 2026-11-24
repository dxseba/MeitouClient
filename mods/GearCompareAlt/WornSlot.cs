using Meitou.Data;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.GearCompareAlt;

/// <summary>
/// What the hovered item is compared with (the original's rule, README "Facts"): armour with what is worn in its slot; a weapon with a worn weapon
/// of the same type, else with the weapon in the slot the item would go to (Weapon I first), and when that slot is free with the weapon in the
/// other one.
/// </summary>
static class WornSlot
{
    /// <summary>The equipment sections of the armour slots and their labels (the inventory's words, the original's catalogue messages).</summary>
    static readonly (string Section, string Label)[] ArmourSections =
    [
        ("head", "Head"), ("armour", "Armour"), ("shirt", "Shirt"), ("legs", "Trousers"), ("boots", "Boots"), ("belt", "Belt"),
        ("backpack_attach", "Backpack"),
    ];

    /// <summary>The worn item to compare with (null: the slot is empty) and the slot's label; a null label: no slot takes the item.</summary>
    public static (InventoryItemView? Worn, string? Slot) For(GameDatabase db, CombatDatabase combat, GearKind kind, string record,
        IReadOnlyList<InventoryItemView> items, Func<string, string> tr)
    {
        if (kind == GearKind.Armour) return Armour(combat, record, items, tr);

        // Weapon I is the back section, Weapon II the hip (the original numbers them so, README "Facts").
        var one = items.FirstOrDefault(i => i.Section == "back");
        var two = items.FirstOrDefault(i => i.Section == "hip");
        string first = tr("Weapon") + " I", second = tr("Weapon") + " II";

        if (one is not null && SameType(db, combat, record, one.Record)) return (one, first);
        if (two is not null && SameType(db, combat, record, two.Record)) return (two, second);

        // The item goes to the first free slot (Weapon I first); a free slot compares with the other one's weapon.
        if (one is null) return two is not null ? (two, second) : (null, first);
        if (two is null) return (one, first);
        return (one, first);
    }

    static (InventoryItemView?, string?) Armour(CombatDatabase combat, string record, IReadOnlyList<InventoryItemView> items,
        Func<string, string> tr)
    {
        if (combat.Armour(record) is not { } armour) return (null, null);

        foreach (var (section, label) in ArmourSections)
        {
            if (InventoryCommands.SectionSlot(section) == armour.Slot) return (items.FirstOrDefault(i => i.Section == section), tr(label));
        }

        return (null, null);
    }

    /// <summary>Two crossbows, or two melee weapons of one skill class.</summary>
    static bool SameType(GameDatabase db, CombatDatabase combat, string a, string b)
    {
        var ka = GearReader.KindOf(db.Find(a));
        if (ka != GearReader.KindOf(db.Find(b))) return false;

        return ka == GearKind.Crossbow
            || combat.Weapon(a) is { } wa && combat.Weapon(b) is { } wb && wa.SkillCategory == wb.SkillCategory;
    }

    /// <summary>A worn item as compared.</summary>
    public static GearItem ItemOf(InventoryItemView v) =>
        new(v.Record, v.Material, v.Company, v.Level, v.Weight / Math.Max(v.Quantity, 1), v.Name);
}
