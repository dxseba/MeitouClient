using Meitou.Modding;

namespace Meitou.SlaverMod;

/// <summary>
/// The original's <c>SlaverMod.ini</c> keys the port carries, with its defaults (README "Facts"), on the Mods settings page.
/// </summary>
public sealed class SlaverSettings
{
    [Setting("Enabled", Hint = "The mod is active: menu rows, submission, escapes and protection.")]
    public bool Enabled { get; set; } = true;

    [Setting("Squad name", Hint = "Squad new slaves are put in (made when missing).")]
    public string SquadName { get; set; } = "Slaves";

    [Setting("Give shackles", Hint = "The enslaver is handed a pair of locked shackles to put on when it carries none.")]
    public bool GiveShackles { get; set; } = true;

    [Setting("Shackles: no running", Hint = "Your shackled slaves can only walk.")]
    public bool ShacklesNoRunning { get; set; } = true;

    [Setting("Shackle item", Hint = "Restraint put on new slaves (item string id).")]
    public string ShackleItem { get; set; } = "SHACKLES";

    [Setting("Animals can be slaves")]
    public bool AnimalSlaves { get; set; }

    [Setting("Allow stealing slaves", Hint = "Enslave other factions' slaves (runaways and free people always).")]
    public bool AllowStealingSlaves { get; set; }

    [Setting("Starting submission", Min = 0, Max = 100, Step = 1)]
    public float SubmissionStart { get; set; } = 30;

    [Setting("Prisoner bonus", Hint = "Extra starting submission for someone caged or chained.", Min = 0, Max = 50, Step = 1)]
    public float SubmissionPrisonerBonus { get; set; } = 10;

    [Setting("Daily gain (watched)", Min = 0, Max = 20, Step = 0.5)]
    public float SubmissionDailyGain { get; set; } = 2;

    [Setting("Gain cap", Hint = "Time alone never takes submission above this.", Min = 0, Max = 100, Step = 1)]
    public float SubmissionGainCap { get; set; } = 60;

    [Setting("Supervision range", Min = 50, Max = 3000, Step = 50)]
    public float SupervisionRange { get; set; } = 500;

    [Setting("Daily loss (unwatched)", Min = 0, Max = 20, Step = 0.5)]
    public float SubmissionUnsupervisedLoss { get; set; } = 5;

    [Setting("Cage daily gain", Min = 0, Max = 30, Step = 1)]
    public float CageDailyGain { get; set; } = 10;

    [Setting("Cage submission cap", Min = 0, Max = 100, Step = 1)]
    public float CageSubmissionCap { get; set; } = 75;

    [Setting("Freed stay at", Hint = "Freed slaves with this submission or more stay in your faction.", Min = 0, Max = 101, Step = 1)]
    public float FreeStayMinSubmission { get; set; } = 90;

    [Setting("Escapes")]
    public bool EscapeEnabled { get; set; } = true;

    [Setting("Escape below", Min = 0, Max = 100, Step = 1)]
    public float EscapeMaxSubmission { get; set; } = 50;

    [Setting("Daily escape chance", Min = 0, Max = 1, Step = 0.05)]
    public float EscapeDailyChance { get; set; } = 0.5f;

    [Setting("Escape x when watched", Min = 0, Max = 1, Step = 0.05)]
    public float EscapeSupervisedMult { get; set; } = 0.1f;

    [Setting("Escape x when shackled", Min = 0, Max = 1, Step = 0.05)]
    public float EscapeShackledMult { get; set; } = 0.5f;

    [Setting("Escape faction", Hint = "Where a runaway of unknown origin goes (faction string id).")]
    public string EscapeFaction { get; set; } = "51707-rebirth.mod";

    [Setting("Strength to break shackles", Min = 0, Max = 100, Step = 1)]
    public float ShackleBreakStrength { get; set; } = 50;

    [Setting("Lockpicking to slip shackles", Min = 0, Max = 100, Step = 1)]
    public float ShackleBreakLockpicking { get; set; } = 40;

    [Setting("Escape recapture penalty", Min = 0, Max = 50, Step = 1)]
    public float EscapeRecapturePenalty { get; set; } = 10;

    [Setting("Punish")]
    public bool PunishEnabled { get; set; } = true;

    [Setting("Punish submission", Min = 0, Max = 50, Step = 1)]
    public float PunishSubmission { get; set; } = 15;

    [Setting("Punish cooldown (days)", Min = 0, Max = 10, Step = 0.5)]
    public float PunishCooldownDays { get; set; } = 1;

    [Setting("Reward")]
    public bool RewardEnabled { get; set; } = true;

    [Setting("Reward submission", Min = 0, Max = 50, Step = 1)]
    public float RewardSubmission { get; set; } = 5;

    [Setting("Reward cost", Min = 0, Max = 5000, Step = 10)]
    public float RewardCost { get; set; } = 100;

    [Setting("Reward cooldown (days)", Min = 0, Max = 10, Step = 0.5)]
    public float RewardCooldownDays { get; set; } = 1;

    [Setting("Spared by neutrals", Hint = "Factions not at war with you never fight your slaves.")]
    public bool SparedByNeutrals { get; set; } = true;

    [Setting("Speech", Hint = "Slavers and slaves say a line (SlaverMod_lines.ini).")]
    public bool Speech { get; set; } = true;

    [Setting("Punish damage", Hint = "Blunt damage on a random limb; each punishment within the cooldown hurts more.", Min = 0, Max = 50, Step = 1)]
    public float PunishDamage { get; set; } = 10;

    [Setting("Protect slaves", Hint = "Slavers of other factions leave your slaves alone (no processing).")]
    public bool ProtectSlaves { get; set; } = true;

    [Setting("No fixed sale of carried people", Hint = "Traders no longer pay a fixed 400 for someone you carry.", SubGroup = "Slave trade")]
    public bool DisableCarriedSale { get; set; } = true;

    [Setting("Trade window", Hint = "Slave traders open SlaverMod's trade window instead of the game's.", SubGroup = "Slave trade")]
    public bool TradeWindow { get; set; } = true;

    [Setting("Base value", Min = 0, Max = 5000, Step = 50, SubGroup = "Slave trade")]
    public float TradeBaseValue { get; set; } = 300;

    [Setting("Value per stat point", Min = 0, Max = 100, Step = 1, SubGroup = "Slave trade")]
    public float TradeStatValue { get; set; } = 12;

    [Setting("Buy price x", Min = 0.1, Max = 5, Step = 0.1, SubGroup = "Slave trade")]
    public float TradeBuyMult { get; set; } = 2;

    [Setting("Sell price x", Min = 0.1, Max = 5, Step = 0.1, SubGroup = "Slave trade")]
    public float TradeSellMult { get; set; } = 1;

    [Setting("Missing limb penalty", Min = 0, Max = 0.5, Step = 0.05, SubGroup = "Slave trade")]
    public float TradeMissingLimbPenalty { get; set; } = 0.25f;

    [Setting("Bought slaves' bonus", Min = 0, Max = 50, Step = 1, SubGroup = "Slave trade")]
    public float TradeBoughtBonus { get; set; } = 15;

    [Setting("Trade radius", Min = 300, Max = 5000, Step = 100, SubGroup = "Slave trade")]
    public float TradeRadius { get; set; } = 1500;

    [Setting("Enslaving needs a shackle kit", SubGroup = "Slave trade")]
    public bool RequireShackles { get; set; }

    [Setting("Shackle kit price", Min = 0, Max = 2000, Step = 10, SubGroup = "Slave trade")]
    public float ShackleKitPrice { get; set; } = 150;

    [Setting("Shackle kit item", SubGroup = "Slave trade")]
    public string ShackleKitItem { get; set; } = "13-SlaverMod.mod";

    [Setting("Rebellions", SubGroup = "Rebellion")]
    public bool RebellionEnabled { get; set; } = true;

    [Setting("Days between checks", Min = 1, Max = 30, Step = 1, SubGroup = "Rebellion")]
    public float RebellionDays { get; set; } = 7;

    [Setting("Chance x", Min = 0, Max = 5, Step = 0.1, SubGroup = "Rebellion")]
    public float RebellionChanceMult { get; set; } = 1;

    [Setting("Maximum chance", Min = 0, Max = 1, Step = 0.05, SubGroup = "Rebellion")]
    public float RebellionMaxChance { get; set; } = 0.9f;

    [Setting("Rebel strength", Min = 0, Max = 100, Step = 1, SubGroup = "Rebellion")]
    public float RebelMinStrength { get; set; } = 20;

    [Setting("Rebel strength (defiant)", Min = 0, Max = 100, Step = 1, SubGroup = "Rebellion")]
    public float RebelMinStrengthDefiant { get; set; } = 10;

    [Setting("Never rebel from", Min = 0, Max = 100, Step = 1, SubGroup = "Rebellion")]
    public float RebelMaxSubmission { get; set; } = 50;

    [Setting("Rebel faction", SubGroup = "Rebellion")]
    public string RebelFaction { get; set; } = "58229-Dialogue.mod";

    [Setting("Rebel weapon", SubGroup = "Rebellion")]
    public string RebelWeapon { get; set; } = "52295-rebirth.mod";

    [Setting("Rebellion means war", SubGroup = "Rebellion")]
    public bool RebellionHostile { get; set; } = true;

    [Setting("Rebels never rejoin", SubGroup = "Rebellion")]
    public bool RebelsDistrust { get; set; } = true;

    [Setting("Slave caravan", SubGroup = "Slave caravan")]
    public bool SlaveCaravan { get; set; } = true;

    [Setting("Days between visits", Min = 1, Max = 60, Step = 1, SubGroup = "Slave caravan")]
    public float SlaveCaravanDays { get; set; } = 7;

    [Setting("Hours it stays", Min = 1, Max = 48, Step = 1, SubGroup = "Slave caravan")]
    public float SlaveCaravanStayHours { get; set; } = 6;

    [Setting("Spawn distance", Min = 500, Max = 6000, Step = 100, SubGroup = "Slave caravan")]
    public float SlaveCaravanSpawnDistance { get; set; } = 2000;

    [Setting("Arrive distance", Min = 100, Max = 3000, Step = 50, SubGroup = "Slave caravan")]
    public float SlaveCaravanArriveDistance { get; set; } = 600;

    [Setting("Leash", Min = 500, Max = 10000, Step = 100, SubGroup = "Slave caravan")]
    public float SlaveCaravanLeash { get; set; } = 3000;

    [Setting("Reprogram skeletons", SubGroup = "Skeletons")]
    public bool SkeletonThrall { get; set; }

    [Setting("Base chance (%)", Min = 0, Max = 100, Step = 1, SubGroup = "Skeletons")]
    public float ThrallBaseChance { get; set; } = 20;

    [Setting("% per robotics level", Min = 0, Max = 5, Step = 0.05, SubGroup = "Skeletons")]
    public float ThrallChancePerLevel { get; set; } = 0.75f;

    [Setting("Maximum chance (%)", Min = 0, Max = 100, Step = 1, SubGroup = "Skeletons")]
    public float ThrallMaxChance { get; set; } = 95;

    [Setting("Race after reprogramming", Hint = "Race string id; empty keeps the race (95870-Newwworld.mod: headless).", SubGroup = "Skeletons")]
    public string ThrallRace { get; set; } = "";

    [Setting("Slaves sleep in cages", SubGroup = "Cages")]
    public bool CageAtNight { get; set; }

    [Setting("Into the cages at (hour)", Min = 0, Max = 23, Step = 1, SubGroup = "Cages")]
    public int CageNightHour { get; set; } = 21;

    [Setting("Out of the cages at (hour)", Min = 0, Max = 23, Step = 1, SubGroup = "Cages")]
    public int CageMorningHour { get; set; } = 7;

    [Setting("Cage search range", Min = 500, Max = 10000, Step = 100, SubGroup = "Cages")]
    public float CageSearchRange { get; set; } = 3000;
}
