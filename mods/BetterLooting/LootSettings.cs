using Meitou.Modding;

namespace Meitou.BetterLooting;

/// <summary>The original's BetterLooting.ini rules and the fork's extras, same defaults (BetterLootingRules.h <c>g_betterLootingRules</c>).</summary>
public sealed class LootSettings
{
    [Setting("Better Looting", Hint = "Insert with the inventory open: loot nearby bodies. Alt+Insert: store your items in nearby storage.")]
    public bool Enabled { get; set; } = true;

    [Setting("Looting key", Hint = "Key that collects loot around the selected character; with Alt it deposits.",
        Options = ["Insert", "Delete", "Home", "End", "PageUp", "PageDown"])]
    public string LootingKey { get; set; } = "Insert";

    [Setting("Deposit only by looting rules", Hint = "Off: every item that is not worn is put away. On: only items that pass the looting rules.")]
    public bool DepositByRules { get; set; }

    [Setting("Loot radius (m)", Hint = "Metres round the character (10 world units each).", Min = 1, Max = 100, Step = 1)]
    public float LootRadius { get; set; } = 20;

    [Setting("Minimum value", Hint = "Skip items worth less than this each.", Min = 0, Max = 100000, Step = 50)]
    public int MinValue { get; set; } = 500;

    [Setting("Minimum value per kg", Hint = "Skip items worth less than this per kilogram (weightless items always pass).", Min = 0, Max = 100000,
        Step = 50)]
    public float MinValuePerKg { get; set; } = 300;

    [Setting("Allow stolen", Hint = "Also take items that are, or would become, stolen goods.")]
    public bool AllowStolen { get; set; }

    [Setting("Best items first", Hint = "Take the most valuable per kilogram first.")]
    public bool BestItemsFirst { get; set; } = true;

    [Setting("Prefer backpack", Hint = "Put loot in the worn backpack before the main inventory.")]
    public bool PreferBackpack { get; set; } = true;

    [Setting("Protect player storage", Hint = "Never loot your own storage buildings and containers.")]
    public bool ProtectPlayerStorage { get; set; } = true;

    [Setting("Ground pickup", Hint = "Also queue pick-up orders for up to 32 qualifying items on the ground, nearest first.")]
    public bool GroundPickup { get; set; }

    [Setting("Weapons")]
    public bool Weapons { get; set; } = true;

    [Setting("Armour")]
    public bool Armour { get; set; } = true;

    [Setting("Food")]
    public bool Food { get; set; } = true;

    [Setting("Medicine")]
    public bool Medicine { get; set; } = true;

    [Setting("Tools")]
    public bool Tools { get; set; } = true;

    [Setting("Materials")]
    public bool Materials { get; set; } = true;

    [Setting("Blueprints")]
    public bool Blueprints { get; set; } = true;

    [Setting("Ammo")]
    public bool Ammo { get; set; } = true;

    [Setting("Robotics")]
    public bool Robotics { get; set; } = true;

    [Setting("Books")]
    public bool Books { get; set; } = true;

    [Setting("Narcotics")]
    public bool Narcotics { get; set; }

    [Setting("Severed limbs")]
    public bool SeveredLimbs { get; set; }

    [Setting("Other")]
    public bool Other { get; set; } = true;

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
