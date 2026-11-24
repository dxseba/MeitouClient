using Meitou.Modding;

namespace Meitou.BetterLooting;

/// <summary>
/// The original's BetterLooting.ini rules and the fork's extras, same defaults (BetterLootingRules.h <c>g_betterLootingRules</c>); labels as the
/// original's catalogue names them, so its translations apply.
/// </summary>
public sealed partial class LootSettings
{
    [Setting("Better Looting", Hint = "The looting key (Controls page) with the inventory open: loot nearby bodies. With Alt: store your items nearby.")]
    public bool Enabled { get; set; } = true;

    [Setting("Deposit only by looting rules", Hint = "Off: every item that is not worn is put away. On: only items that pass the looting rules.")]
    public bool DepositByRules { get; set; }

    [Setting("Loot radius (m)", Hint = "Metres round the character (10 world units each).", Min = 1, Max = 100, Step = 1)]
    public float LootRadius { get; set; } = 20;

    [Setting("Minimum value", Hint = "Skip items worth less than this each.", Min = 0, Max = 100000, Step = 50)]
    public int MinValue { get; set; } = 500;

    [Setting("Minimum value per kg", Hint = "Skip items worth less than this per kilogram (weightless items always pass).", Min = 0, Max = 100000,
        Step = 50)]
    public float MinValuePerKg { get; set; } = 300;

    [Setting("Allow stolen items", Hint = "Also take items that are, or would become, stolen goods.")]
    public bool AllowStolen { get; set; }

    [Setting("Best items first", Hint = "Take the most valuable per kilogram first.")]
    public bool BestItemsFirst { get; set; } = true;

    [Setting("Prefer backpack", Hint = "Put loot in the worn backpack before the main inventory.")]
    public bool PreferBackpack { get; set; } = true;

    [Setting("Protect player storage", Hint = "Never loot your own storage buildings and containers.")]
    public bool ProtectPlayerStorage { get; set; } = true;

    [Setting("Pick up ground items", Hint = "Also queue pick-up orders for up to 32 qualifying items on the ground, nearest first.")]
    public bool GroundPickup { get; set; }

    [Setting("Always take these items", Hint = "Names or record ids split by ; or | (name: or id: to say which); they skip the category and price rules.",
        MaxLength = ModSetting.MaxTextLength)]
    public string RequiredItems { get; set; } = "";

    [Setting("Weapons", SubGroup = "Categories")]
    public bool Weapons { get; set; } = true;

    [Setting("Armour", SubGroup = "Categories")]
    public bool Armour { get; set; } = true;

    [Setting("Food", SubGroup = "Categories")]
    public bool Food { get; set; } = true;

    [Setting("Medicine", SubGroup = "Categories")]
    public bool Medicine { get; set; } = true;

    [Setting("Tools", SubGroup = "Categories")]
    public bool Tools { get; set; } = true;

    [Setting("Materials and resources", SubGroup = "Categories")]
    public bool Materials { get; set; } = true;

    [Setting("Blueprints", SubGroup = "Categories")]
    public bool Blueprints { get; set; } = true;

    [Setting("Ammo", SubGroup = "Categories")]
    public bool Ammo { get; set; } = true;

    [Setting("Robotics", SubGroup = "Categories")]
    public bool Robotics { get; set; } = true;

    [Setting("Books", SubGroup = "Categories")]
    public bool Books { get; set; } = true;

    [Setting("Narcotics", SubGroup = "Categories")]
    public bool Narcotics { get; set; }

    [Setting("Severed limbs", SubGroup = "Categories")]
    public bool SeveredLimbs { get; set; }

    [Setting("Other", SubGroup = "Categories")]
    public bool Other { get; set; } = true;

    [Setting("Debug logging", Hint = "Write every looting step to the mod log.", SubGroup = "Advanced")]
    public bool DebugLogging { get; set; }

    /// <summary>The radius in world units.</summary>
    public float RadiusUnits => Math.Clamp(LootRadius, 1, 100) * 10;

    public bool Allows(LootCategory category) => category switch
    {
        LootCategory.Weapons => Weapons,
        LootCategory.Armour => Armour,
        LootCategory.Food => Food,
        LootCategory.Medicine => Medicine,
        LootCategory.Tools => Tools,
        LootCategory.Materials => Materials,
        LootCategory.Blueprints => Blueprints,
        LootCategory.Ammo => Ammo,
        LootCategory.Robotics => Robotics,
        LootCategory.Books => Books,
        LootCategory.Narcotics => Narcotics,
        LootCategory.SeveredLimbs => SeveredLimbs,
        _ => Other,
    };
}
