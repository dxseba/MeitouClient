using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.FixShoppingWages;

/// <summary>
/// Port of Fix Shopping Wages (workshop 3775703318), rebuilt from its description: every in-game day NPC squads get a daily allowance for their
/// members, so shoppers keep money to buy from the player's shop counters. The engine already pays CHARACTER <c>wages</c> (it sets the squad's
/// money, as the original's daily update); this mod covers the members without: <c>money min</c>..<c>money max</c>, else the Dried Meat price,
/// else a fallback.
/// </summary>
public sealed class FixShoppingWagesMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new WageSettings();
        context.AddSettings(settings);

        int driedMeat = DriedMeatValue(context.Data);
        context.Log(driedMeat > 0 ? $"Fix Shopping Wages: Dried Meat value {driedMeat}" : "Fix Shopping Wages: no Dried Meat record, the fallback pays");
        context.AddSystem("wages", _ => new WageSystem(settings, context.Data, context.Population, context.RngPurpose, driedMeat),
            SystemAnchor.End);
        context.Log("Fix Shopping Wages initialized");
    }

    /// <summary>The ITEM named Dried Meat's <c>value</c>; 0 when there is none.</summary>
    static int DriedMeatValue(GameDatabase data)
    {
        var meat = data.OfType(FcsRecordType.Item).Where(r => r.Name == "Dried Meat").OrderBy(r => r.StringId, StringComparer.Ordinal);
        return meat.FirstOrDefault()?.GetInt("value") ?? 0;
    }
}

/// <summary>The original's JSON options, under Mod settings.</summary>
public sealed class WageSettings
{
    [Setting("Enable Fix Shopping Wages", Hint = "NPC squads get a daily allowance for shopping.")]
    public bool Enabled { get; set; } = true;

    [Setting("Fallback wage", Hint = "Daily cats per member when the Dried Meat price cannot be found.", Min = 1, Max = 1000)]
    public int FallbackWage { get; set; } = 78;

    [Setting("Use a fixed wage", Hint = "On: every member without its own wage range gets the fixed wage below instead of the Dried Meat price.")]
    public bool UseOverride { get; set; }

    [Setting("Fixed wage", Hint = "Daily cats per member with 'Use a fixed wage' on.", Min = 1, Max = 5000)]
    public int OverrideWage { get; set; } = 100;

    [Setting("Savings cap (days)", Hint = "A squad's money stops growing at this many days of its members' wages.", Min = 1, Max = 30)]
    public int SavingsDays { get; set; } = 5;
}
