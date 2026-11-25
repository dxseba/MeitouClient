using Meitou.Modding;

namespace Meitou.DynamicWasteland;

/// <summary>
/// The original's <c>DynamicWasteland.ini</c> keys that this port runs, with the shipped defaults (<c>[World]</c>, <c>[Diplomacy]</c>,
/// <c>[Economy]</c>, <c>[Map]</c>, <c>[UI]</c>). The simulation copies them in its serial step.
/// </summary>
public sealed class DwSettings
{
    [Setting("Enabled", Hint = "[World] Enabled: master switch of the political simulation.")]
    public bool Enabled { get; set; } = true;

    [Setting("Tick hours", Hint = "[World] TickHours: every X in-game hours the political simulation advances (12 = twice a day).", Min = 1,
        Max = 72)]
    public int TickHours { get; set; } = 12;

    [Setting("War intensity", Hint = "[World] WarIntensity: controls declarations, offensives and peace; marches and battles remain real.",
        Options = ["calm", "balanced", "aggressive", "relentless"])]
    public string WarIntensity { get; set; } = "aggressive";

    [Setting("Dynamic wars", Hint = "[World] DynamicWars: nations declare wars on their own.")]
    public bool DynamicWars { get; set; } = true;

    [Setting("Territory changes", Hint = "[World] TerritoryChanges: towns change owner after a won assault.")]
    public bool TerritoryChanges { get; set; } = true;

    [Setting("World battles only", Hint = "[World] WorldBattlesOnly: a town changes hands only after a real army marched on it.")]
    public bool WorldBattlesOnly { get; set; } = true;

    [Setting("Conquest garrison", Hint = "[World] ConquestGarrison: a captured town gets garrison squads of its new owner.")]
    public bool ConquestGarrison { get; set; } = true;

    [Setting("Garrison squads", Hint = "[World] ConquestGarrisonMax: garrison squads spawned per conquest.", Min = 0, Max = 6,
        VisibleWhen = nameof(ConquestGarrison))]
    public int ConquestGarrisonMax { get; set; } = 3;

    [Setting("News popups", Hint = "[World] NewsPopups: major events as on-screen messages.")]
    public bool NewsPopups { get; set; } = true;

    [Setting("Battle popups", Hint = "[World] NewsPopupBattles: also a message for battles (off: news feed only).")]
    public bool NewsPopupBattles { get; set; }

    [Setting("News kept", Hint = "[World] MaxNews: entries kept in the panel's news feed.", Min = 4, Max = 200)]
    public int MaxNews { get; set; } = 24;

    [Setting("Alliances", Hint = "[Diplomacy] Alliances: friendly nations swear mutual defence; allies may join a war.", SubGroup = "Diplomacy")]
    public bool Alliances { get; set; } = true;

    [Setting("Negotiated peace", Hint = "[Diplomacy] NegotiatedPeace: worn-out nations sign peace and a truce.", SubGroup = "Diplomacy")]
    public bool NegotiatedPeace { get; set; } = true;

    [Setting("Truce days", Hint = "[Diplomacy] TruceDays: base truce length after a treaty.", Min = 10, Max = 365, SubGroup = "Diplomacy")]
    public int TruceDays { get; set; } = 60;

    [Setting("Economy", Hint = "[Economy] Enabled: national treasuries from taxes and trade, minus army upkeep.", SubGroup = "Economy")]
    public bool Economy { get; set; } = true;

    [Setting("Tax per worker", Hint = "[Economy] TaxPerWorker: Cats a day per working citizen.", Min = 0, Max = 20, SubGroup = "Economy")]
    public int TaxPerWorker { get; set; } = 2;

    [Setting("Trade income", Hint = "[Economy] TradeIncomePerEconomyPoint: Cats a day per economy point.", Min = 0, Max = 200,
        SubGroup = "Economy")]
    public int TradeIncomePerEconomyPoint { get; set; } = 20;

    [Setting("Soldier wage", Hint = "[Economy] SoldierWage: Cats a day per soldier.", Min = 0, Max = 30, SubGroup = "Economy")]
    public int SoldierWage { get; set; } = 3;

    [Setting("Political map", Hint = "[Map] Overlay: owner colours on the world map's towns.", SubGroup = "Map")]
    public bool MapOverlay { get; set; } = true;

    [Setting("Territory transparency", Hint = "[Map] BlobAlpha: 0.10 almost solid, 0.80 very faint.", Min = 0.1, Max = 0.8, Step = 0.01,
        SubGroup = "Map")]
    public float BlobAlpha { get; set; } = 0.38f;

    [Setting("Excluded factions", Hint = "[ExcludedFactions]: faction string ids never simulated, separated by commas.", MaxLength = 1024)]
    public string ExcludedFactions { get; set; } = "";

    /// <summary>The intensity's table column (the original's <c>calm</c> 0, <c>balanced</c> 1, <c>relentless</c> 3, anything else 2).</summary>
    public int IntensityLevel => WarIntensity switch
    {
        "calm" => 0,
        "balanced" => 1,
        "relentless" => 3,
        _ => 2,
    };
}
