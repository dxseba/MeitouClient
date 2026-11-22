using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Economy;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.FixShoppingWages;

/// <summary>
/// Port of Fix Shopping Wages (workshop 3775703318): every in-game day each NPC gets its wage added to its money, up to a multiple of it, so
/// shoppers keep money to buy from the player's shop counters. The wage is the character's <c>wages</c>, else a roll in its starting money range,
/// else Dried Meat's average price (or a fixed override, or a fallback).
/// </summary>
public sealed class FixShoppingWagesMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new WageSettings();
        context.AddSettings(settings);

        int driedMeat = DriedMeatPrice(context.Data);
        context.Log(driedMeat > 0 ? $"Fix Shopping Wages: Dried Meat average price {driedMeat}" : "Fix Shopping Wages: no Dried Meat record");
        context.AddSystem("wages", _ => new WageSystem(settings, context.Data, context.RngPurpose, driedMeat), SystemAnchor.End);
    }

    /// <summary>
    /// The average price of the ITEM named Dried Meat (the original prices a fresh instance with <c>getAvgPrice</c>: its value at level 0 with the
    /// category rows); 0 when there is none.
    /// </summary>
    static int DriedMeatPrice(GameDatabase data)
    {
        var meat = data.OfType(FcsRecordType.Item)
            .Where(r => r.Name == "Dried Meat")
            .OrderBy(r => r.StringId, StringComparer.Ordinal)
            .FirstOrDefault();
        return meat is null ? 0 : ItemValue.Of(data, meat, 0, true, PriceConstants.FromDatabase(data));
    }
}

/// <summary>
/// The original's options (its <c>mod-config.json</c> keys and Mod Hub labels), with the values its shipped <c>mod-config.json</c> holds.
/// </summary>
public sealed class WageSettings
{
    [Setting("Enabled", Hint = "Enable the mod.")]
    public bool Enabled { get; set; } = true;

    [Setting("Base wage fallback", Hint = "Fallback wage if Dried Meat price can't be found (Not used if override is active).", Min = 0, Max = 100000,
        Step = 1)]
    public int BaseWageFallback { get; set; } = 78;

    [Setting("Override base wage", Hint = "Skip Dried Meat local value and use override value (disables fallback).")]
    public bool BaseWageOverride { get; set; } = true;

    [Setting("Override wage value", Hint = "Static wage used when override is active.", Min = 0, Max = 100000, Step = 1)]
    public int BaseWageOverrideValue { get; set; }

    [Setting("Max savings multiplier", Hint = "Max days' wages an NPC can accumulate.", Min = 0, Max = 1000, Step = 1)]
    public int MaxSavingsMultiplier { get; set; } = 10;
}
