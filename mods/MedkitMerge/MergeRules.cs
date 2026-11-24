using Meitou.Simulation.Items;

namespace Meitou.MedkitMerge;

/// <summary>
/// The original's eligibility and transfer rules (README "Facts", from its DLL): which items take part, which pairs match, and how much one pours
/// into another.
/// </summary>
static class MergeRules
{
    public const int FirstAid = 1, Splint = 2, RobotRepair = 12, Ammo = 16;

    /// <summary>The largest bolt capacity taken as a whole count (2^24, where a float stops holding every integer).</summary>
    const float MaxWholeCount = 16777216;

    public static bool IsSupply(ItemInstance item) => item.ItemFunction is FirstAid or Splint or RobotRepair or Ammo;

    /// <summary>The name the original's log gives the item's kind.</summary>
    public static string KindName(int function) => function switch
    {
        Ammo => "ammo",
        FirstAid => "firstaid",
        Splint => "splint",
        RobotRepair => "robot-repair",
        _ => "supply",
    };

    /// <summary>Why the item cannot give or take supplies (the original's reasons), null when it can: a partial single item, not worn, not unique.</summary>
    public static string? Refusal(ItemInstance item)
    {
        if (!IsSupply(item)) return "unsupported-supply-type";
        if (item.Record.Length == 0) return "missing-record";
        if (item.Quantity != 1) return "stack-quantity-not-one";
        if (InventoryCommands.IsEquipmentSection(item.Section)) return "equipped";
        if (item.Unique) return "unique-item";
        if (!float.IsFinite(item.Charges) || !float.IsFinite(item.MaxCharges)) return "nonfinite-charges";
        if (item.MaxCharges <= 0) return "invalid-capacity";
        if (item.ItemFunction == Ammo && !WholeCount(item.Charges, item.MaxCharges)) return "ammo-noninteger-or-invalid-count";
        if (item.Charges <= 0) return "empty";
        if (item.MaxCharges <= item.Charges) return "full-or-overfull";
        return null;
    }

    /// <summary>Same function, record, quality and capacity (any owner or stolen mark).</summary>
    public static bool Matches(ItemInstance a, ItemInstance b) =>
        a.ItemFunction == b.ItemFunction && a.Record == b.Record && float.IsFinite(a.Quality) && float.IsFinite(b.Quality)
        && a.MaxCharges > 0 && a.Quality == b.Quality && a.MaxCharges == b.MaxCharges;

    /// <summary>A kit's pour: what fits, at most what the donor has left; 0 when the values are not sound.</summary>
    public static float KitAmount(float left, float target, float capacity)
    {
        if (!float.IsFinite(left) || !float.IsFinite(target) || !float.IsFinite(capacity)) return 0;
        if (left <= 0 || target < 0 || capacity <= 0 || target >= capacity) return 0;

        return MathF.Min(left, capacity - target);
    }

    /// <summary>
    /// A bolt pack's pour: whole counts only, into a pack holding at least as many as the donor has left (an equal one only when it comes first),
    /// both partial; then as a kit.
    /// </summary>
    public static float AmmoAmount(float left, float target, float capacity, bool targetFirst)
    {
        if (!WholeCount(left, capacity) || !WholeCount(target, capacity)) return 0;
        if (left <= 0 || target <= 0 || capacity <= left || capacity <= target || target < left) return 0;
        if (target == left && !targetFirst) return 0;

        return KitAmount(left, target, capacity);
    }

    static bool WholeCount(float count, float capacity) =>
        float.IsFinite(count) && float.IsFinite(capacity) && capacity > 0 && capacity <= MaxWholeCount && count >= 0 && count <= capacity
        && MathF.Floor(count) == count && MathF.Floor(capacity) == capacity;
}
