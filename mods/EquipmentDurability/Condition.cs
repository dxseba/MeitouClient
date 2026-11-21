using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Simulation.Items;

namespace Meitou.EquipmentDurability;

/// <summary>
/// An item's condition, 0 (broken) to 1 (new), kept in its <see cref="ItemInstance.ModTag"/> (saved and copied with the item) as
/// <c>dxseba.durability:0.8734</c>. An item another mod tagged is left alone; an untagged weapon or armour counts as new.
/// </summary>
static class Condition
{
    const string Prefix = "dxseba.durability:";

    /// <summary>The armour sections (worn gear the blows wear) and the weapon sections.</summary>
    public static readonly string[] ArmourSections = ["head", "shirt", "armour", "legs", "boots"];
    public static readonly string[] WeaponSections = ["hip", "back"];

    /// <summary>Whether this mod tracks the item: a weapon or armour without another mod's tag.</summary>
    public static bool Tracked(ItemInstance item) =>
        item.Type is FcsRecordType.Weapon or FcsRecordType.Armour && (item.ModTag.Length == 0 || item.ModTag.StartsWith(Prefix, StringComparison.Ordinal));

    /// <summary>The condition of a tracked item (1 untagged).</summary>
    public static float Of(ItemInstance item) =>
        item.ModTag.StartsWith(Prefix, StringComparison.Ordinal)
        && float.TryParse(item.ModTag.AsSpan(Prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? Math.Clamp(value, 0, 1) : 1;

    public static void Set(ItemInstance item, float value) =>
        item.ModTag = Prefix + Math.Clamp(value, 0, 1).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// The damage factor of gear in <paramref name="condition"/>: 1 from <paramref name="threshold"/> up, falling linearly to 1 - <paramref name="broken"/>
    /// at 0 (the original's debuff below 39%, -70% when broken).
    /// </summary>
    public static float Factor(float condition, float threshold, float broken) =>
        condition >= threshold ? 1 : 1 - broken * (threshold - condition) / threshold;

    /// <summary>The weapon the character fights with: its first tracked weapon on the hip, else on the back.</summary>
    public static ItemInstance? Weapon(Inventory? inventory) =>
        inventory?.Items.Where(i => i.Type == FcsRecordType.Weapon && Tracked(i) && WeaponSections.Contains(i.Section))
            .OrderBy(i => Array.IndexOf(WeaponSections, i.Section)).FirstOrDefault();

    public static IEnumerable<ItemInstance> Worn(Inventory? inventory) =>
        inventory?.Items.Where(i => i.Type == FcsRecordType.Armour && Tracked(i) && ArmourSections.Contains(i.Section)) ?? [];
}
