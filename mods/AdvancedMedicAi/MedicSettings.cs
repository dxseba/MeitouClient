using Meitou.Modding;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// The original's in-game settings (its KEP tab, saved to <c>AdvancedMedicAI_TargetFilters.ini</c>), in its order and with its labels and slider
/// ranges; the defaults are the DLL's own (README "Facts"). The faction rules are kept as text (<see cref="FactionRules"/>), edited in the rules
/// window. The page sets these on the interface's thread; the system copies them in its serial step (<see cref="MedicRules.From"/>).
/// </summary>
public sealed class MedicSettings
{
    [Setting("Player characters", SubGroup = "Treat")]
    public bool TreatPlayerCharacters { get; set; } = true;

    [Setting("Allies", SubGroup = "Treat")]
    public bool TreatAllies { get; set; } = true;

    [Setting("Neutrals", SubGroup = "Treat")]
    public bool TreatNeutrals { get; set; } = true;

    [Setting("Enemies", SubGroup = "Treat")]
    public bool TreatEnemies { get; set; }

    [Setting("Wild animals", SubGroup = "Treat")]
    public bool TreatAnimals { get; set; }

    [Setting("Whole base in reach", Hint = "At your own outpost the whole town is in reach when the medic and the patient are both in it.")]
    public bool WholeBaseReach { get; set; } = true;

    [Setting("Field radius, m", Min = 10, Max = 300, Step = 1)]
    public float FieldRadiusMeters { get; set; } = 50;

    [Setting("Robot repair radius, m", Min = 10, Max = 300, Step = 1)]
    public float FieldRobotRadiusMeters { get; set; } = 100;

    [Setting("Combat medic: treat own and allies nearby during fights")]
    public bool CombatMedic { get; set; }

    [Setting("Combat medic radius, m", Min = 5, Max = 50, Step = 1, VisibleWhen = nameof(CombatMedic))]
    public float CombatRadiusMeters { get; set; } = 15;

    [Setting("Avoid acid rain, gas and fire")]
    public bool AvoidHazards { get; set; } = true;

    [Setting("Respect manual orders, s", Min = 0, Max = 300, Step = 1)]
    public float ManualOrderGraceSeconds { get; set; } = 60;

    [Setting("Show notifications")]
    public bool ShowNotifications { get; set; } = true;

    [Setting("Faction rules...", Button = "Faction rules...")]
    public Action OpenFactionRules { get; } = FactionRulesWindow.Open;

    /// <summary>
    /// The per-faction rules as <c>id:always;id:never</c> (faction string ids); never shown on the page, set by the rules window.
    /// </summary>
    [Setting("Faction rules", MaxLength = ModSetting.MaxTextLength, VisibleWhen = nameof(Never))]
    public string FactionRules { get; set; } = "";

    public bool Never => false;
}
