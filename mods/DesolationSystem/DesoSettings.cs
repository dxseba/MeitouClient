using System.Globalization;
using Meitou.Modding;

namespace Meitou.DesolationSystem;

/// <summary>
/// The original's <c>DesolationSystem.ini</c> (<c>[Settings]</c>, <c>[Thresholds]</c>, <c>[Synergy]</c>, <c>[PerkSwitch]</c>) under Mod settings, with
/// the release's defaults. Each perk switch names its ini key in the hint. The cards set the scope, cap, realm switch and language too.
/// </summary>
public sealed class DesoSettings
{
    public const string English = "English", Chinese = "Chinese";
    public const string Global = "Global", PlayerOnly = "Player only", NpcOnly = "NPC only";

    /// <summary>The original's cap tiers (F8 / the Speed Cap card cycle them).</summary>
    internal static readonly string[] CapTiers = ["1.2", "2.4", "3.6", "4.8", "9.6"];

    internal static readonly string[] Scopes = [Global, PlayerOnly, NpcOnly];

    [Setting("Language", Options = [English, Chinese], SubGroup = "General")]
    public string Language { get; set; } = English;

    [Setting("Scope", Hint = "TargetScope: who the base perks apply to (F6 cycles it). Supreme Realms are player-only.",
        Options = [Global, PlayerOnly, NpcOnly], SubGroup = "General")]
    public string Scope { get; set; } = Global;

    [Setting("Attack speed cap", Hint = "AttackSpeedCap: the fastest an attack may play (blocks: 4, or 8 at 9.6).", Options = ["1.2", "2.4", "3.6", "4.8", "9.6"],
        SubGroup = "General")]
    public string Cap { get; set; } = "4.8";

    [Setting("Custom cap", Hint = "CustomAttackSpeedEnabled: use the custom value below instead of the tier.", SubGroup = "General")]
    public bool CustomCap { get; set; }

    [Setting("Custom cap value", Hint = "CustomAttackSpeedCap (0.1 to 100).", Min = 0.1, Max = 100, Step = 0.1, SubGroup = "General",
        VisibleWhen = nameof(CustomCap))]
    public float CustomCapValue { get; set; } = 4.8f;

    [Setting("Supreme Realms", Hint = "EnableAscension: the five realms (player characters with every listed stat at 90+). 9 + 0 toggles it.",
        SubGroup = "General")]
    public bool Ascension { get; set; } = true;

    [Setting("Hide the cards", SubGroup = "General")]
    public bool Hidden { get; set; }

    [Setting("Mastery threshold", Hint = "MasteryThreshold: the weapon level the mastery perks start above.", Min = 1, Max = 100, Step = 1,
        SubGroup = "Thresholds")]
    public float MasteryThreshold { get; set; } = 60;

    [Setting("Strength gap", Hint = "StrengthGapThreshold: the Strength gap a block recoil starts above.", Min = 0, Max = 100, Step = 1,
        SubGroup = "Thresholds")]
    public float StrengthGap { get; set; } = 30;

    [Setting("Recoil per 10", Hint = "RecoilDamagePer10: blunt damage to each arm per 10 Strength gap above it.", Min = 0, Max = 50, Step = 0.5,
        SubGroup = "Thresholds")]
    public float RecoilPer10 { get; set; } = 5;

    [Setting("Defence mastery", Hint = "DefenseMastery: melee defence above which recoil never hurts.", Min = 0, Max = 200, Step = 1,
        SubGroup = "Thresholds")]
    public float DefenceMastery { get; set; } = 105;

    [Setting("Phantom Movement dexterity", Hint = "CounterThreshold.", Min = 0, Max = 200, Step = 1, SubGroup = "Thresholds")]
    public float CounterThreshold { get; set; } = 100;

    [Setting("Hyper Armor strength", Hint = "HyperArmorStrReq.", Min = 0, Max = 200, Step = 1, SubGroup = "Thresholds")]
    public float HyperArmorStrength { get; set; } = 85;

    [Setting("Shockwave strength", Hint = "ShockwaveStrReq.", Min = 0, Max = 300, Step = 1, SubGroup = "Thresholds")]
    public float ShockwaveStrength { get; set; } = 120;

    [Setting("Master Porter labouring / engineering", Hint = "TitanLabour, TitanEng.", Min = 0, Max = 100, Step = 1, SubGroup = "Thresholds")]
    public float Porter { get; set; } = 70;

    [Setting("Field Rations farming / cooking", Hint = "ChefFarm, ChefCook.", Min = 0, Max = 100, Step = 1, SubGroup = "Thresholds")]
    public float Chef { get; set; } = 70;

    [Setting("Master Key lockpicking / engineering / science", Hint = "MasterKeyLock, MasterKeyEng, MasterKeySci.", Min = 0, Max = 100, Step = 1,
        SubGroup = "Thresholds")]
    public float MasterKeyLevel { get; set; } = 80;

    [Setting("Silent Touch stealth / assassination", Hint = "SilentStealth, SilentAssassin.", Min = 0, Max = 100, Step = 1, SubGroup = "Thresholds")]
    public float SilentLevel { get; set; } = 80;

    [Setting("Pickpocket stealth / thieving", Hint = "PickpocketStealth, PickpocketThieving.", Min = 0, Max = 100, Step = 1, SubGroup = "Thresholds")]
    public float PickpocketLevel { get; set; } = 80;

    [Setting("Pickpocket reach", Hint = "PickpocketMaxDist.", Min = 5, Max = 200, Step = 1, SubGroup = "Thresholds")]
    public float PickpocketReach { get; set; } = 35;

    [Setting("Prosthetic Tuning medic / science / robotics", Hint = "CyberMedic, CyberSci, CyberRobotics.", Min = 0, Max = 100, Step = 1,
        SubGroup = "Thresholds")]
    public float CyberLevel { get; set; } = 80;

    [Setting("Effect scale", Hint = "The original's global effect scale: every mastery perk's value times this.", Min = 0.1, Max = 5, Step = 0.05,
        SubGroup = "Thresholds")]
    public float EffectScale { get; set; } = 1;

    [Setting("Strength Armor Penetration", Hint = "P1", SubGroup = "Base stats")] public bool P1 { get; set; } = true;
    [Setting("Strength Gap Recoil", Hint = "P2", SubGroup = "Base stats")] public bool P2 { get; set; } = true;
    [Setting("Defense Mastery Immunity", Hint = "P3", SubGroup = "Base stats")] public bool P3 { get; set; } = true;
    [Setting("Hyper Armor", Hint = "P5", SubGroup = "Base stats")] public bool P5 { get; set; } = true;
    [Setting("Titan Shockwave", Hint = "P6", SubGroup = "Base stats")] public bool P6 { get; set; } = true;
    [Setting("Shockwave Resist", Hint = "P6_RESIST", SubGroup = "Base stats")] public bool P6Resist { get; set; } = true;
    [Setting("Perception Synergy", Hint = "P17", SubGroup = "Base stats")] public bool P17 { get; set; } = true;
    [Setting("Last Light", Hint = "P18", SubGroup = "Base stats")] public bool P18 { get; set; } = true;
    [Setting("Toughness Reduction", Hint = "P206", SubGroup = "Base stats")] public bool P206 { get; set; } = true;
    [Setting("Lightning Counter", Hint = "P209", SubGroup = "Base stats")] public bool P209 { get; set; } = true;
    [Setting("Athletics Momentum", Hint = "P210", SubGroup = "Base stats")] public bool P210 { get; set; } = true;
    [Setting("Aquatic Dragon", Hint = "P211 / P23 two-state switch", SubGroup = "Base stats")] public bool P211 { get; set; } = true;
    [Setting("Tough Vitality", Hint = "TOUGHPHP", SubGroup = "Base stats")] public bool ToughHp { get; set; } = true;
    [Setting("Wind-Eroded Wall", Hint = "P222 (off by default, as the original)", SubGroup = "Base stats")] public bool P222 { get; set; }
    [Setting("Evasion Master", Hint = "P205", SubGroup = "Base stats")] public bool P205 { get; set; } = true;
    [Setting("Iron Guard", Hint = "P16", SubGroup = "Base stats")] public bool P16 { get; set; } = true;

    [Setting("Katana Tear", Hint = "P7", SubGroup = "Weapon mastery")] public bool P7 { get; set; } = true;
    [Setting("Saber Guard", Hint = "P8", SubGroup = "Weapon mastery")] public bool P8 { get; set; } = true;
    [Setting("Hackers Mastery", Hint = "P9", SubGroup = "Weapon mastery")] public bool P9 { get; set; } = true;
    [Setting("Blunt Impact", Hint = "P10", SubGroup = "Weapon mastery")] public bool P10 { get; set; } = true;
    [Setting("Heavy Onslaught", Hint = "P11", SubGroup = "Weapon mastery")] public bool P11 { get; set; } = true;
    [Setting("Polearm Supremacy", Hint = "P12", SubGroup = "Weapon mastery")] public bool P12 { get; set; } = true;
    [Setting("Martial Burst", Hint = "P13", SubGroup = "Weapon mastery")] public bool P13 { get; set; } = true;
    [Setting("Crossbow Mastery", Hint = "P207", SubGroup = "Weapon mastery")] public bool P207 { get; set; } = true;
    [Setting("Squad Volley", Hint = "P15", SubGroup = "Weapon mastery")] public bool P15 { get; set; } = true;
    [Setting("Master Ryu", Hint = "RYU_SYS", SubGroup = "Weapon mastery")] public bool Ryu { get; set; } = true;

    [Setting("Hemostasis Miracle", Hint = "P20", SubGroup = "Professions")] public bool P20 { get; set; } = true;
    [Setting("Master Hemopoiesis", Hint = "P21", SubGroup = "Professions")] public bool P21 { get; set; } = true;
    [Setting("Battlefield Healing", Hint = "P204", SubGroup = "Professions")] public bool P204 { get; set; } = true;
    [Setting("Field Rations", Hint = "P22", SubGroup = "Professions")] public bool P22 { get; set; } = true;
    [Setting("Field Nutrition", Hint = "P202", SubGroup = "Professions")] public bool P202 { get; set; } = true;
    [Setting("Nano-Repair", Hint = "P23", SubGroup = "Professions")] public bool P23 { get; set; } = true;
    [Setting("Master Porter", Hint = "P27", SubGroup = "Professions")] public bool P27 { get; set; } = true;
    [Setting("Blade Unity", Hint = "P213", SubGroup = "Professions")] public bool P213 { get; set; } = true;
    [Setting("Steel Body", Hint = "P214", SubGroup = "Professions")] public bool P214 { get; set; } = true;

    [Setting("Physical Sanctification: damage -10%", Hint = "ASC_PHYS_DR", SubGroup = "Supreme Realms")] public bool AscPhysDr { get; set; } = true;
    [Setting("Physical Sanctification: flesh regen", Hint = "ASC_PHYS_REGEN", SubGroup = "Supreme Realms")] public bool AscPhysRegen { get; set; } = true;
    [Setting("Physical Sanctification: blood floor", Hint = "ASC_PHYS_BLOOD", SubGroup = "Supreme Realms")] public bool AscPhysBlood { get; set; } = true;
    [Setting("Physical Sanctification: no injury penalties", Hint = "ASC_PHYS_INJURY", SubGroup = "Supreme Realms")]
    public bool AscPhysInjury { get; set; } = true;
    [Setting("Universal Mastery: speed", Hint = "ASC_MART_SWIFT", SubGroup = "Supreme Realms")] public bool AscMartSwift { get; set; } = true;
    [Setting("Universal Mastery: damage", Hint = "ASC_MART_DMG", SubGroup = "Supreme Realms")] public bool AscMartDmg { get; set; } = true;
    [Setting("Universal Mastery: parry", Hint = "ASC_MART_BLOCK (parry +50%)", SubGroup = "Supreme Realms")] public bool AscMartBlock { get; set; } = true;
    [Setting("Wilderness Sovereign: pack sync", Hint = "ASC_WLD_SYNC", SubGroup = "Supreme Realms")] public bool AscWldSync { get; set; } = true;
    [Setting("Wilderness Sovereign: pack buff", Hint = "ASC_WLD_BUFF", SubGroup = "Supreme Realms")] public bool AscWldBuff { get; set; } = true;
    [Setting("Wilderness Sovereign: pack boon", Hint = "ASC_WLD_BOON", SubGroup = "Supreme Realms")] public bool AscWldBoon { get; set; } = true;
    [Setting("Wilderness Sovereign: carry to recruit", Hint = "ASC_WLD_RECRUIT", SubGroup = "Supreme Realms")] public bool AscWldRecruit { get; set; } = true;
    [Setting("Wilderness Sovereign: beasts stay peaceful", Hint = "ASC_WLD_FEAR", SubGroup = "Supreme Realms")] public bool AscWldFear { get; set; } = true;
    [Setting("Physical Sanctification: limb lock", Hint = "ASC_PHYS_LIMB", SubGroup = "Supreme Realms")] public bool AscPhysLimb { get; set; } = true;
    [Setting("Physical Sanctification: no KO", Hint = "ASC_PHYS_KO", SubGroup = "Supreme Realms")] public bool AscPhysKo { get; set; } = true;
    [Setting("Universal Mastery: reach", Hint = "ASC_MART_REACH", SubGroup = "Supreme Realms")] public bool AscMartReach { get; set; } = true;
    [Setting("Universal Mastery: true damage", Hint = "ASC_MART_TRUE", SubGroup = "Supreme Realms")] public bool AscMartTrue { get; set; } = true;
    [Setting("Shadow Sovereign: sure kill", Hint = "ASC_SHD_KILL", SubGroup = "Supreme Realms")] public bool AscShdKill { get; set; } = true;
    [Setting("Shadow Sovereign: blue eye", Hint = "ASC_SHD_EYE", SubGroup = "Supreme Realms")] public bool AscShdEye { get; set; } = true;
    [Setting("Shadow Sovereign: shake off attackers", Hint = "ASC_SHD_WATCH", SubGroup = "Supreme Realms")] public bool AscShdWatch { get; set; } = true;
    [Setting("Shadow Sovereign: no crime", Hint = "ASC_SHD_CRIME", SubGroup = "Supreme Realms")] public bool AscShdCrime { get; set; } = true;
    [Setting("Cybernetic Overlord: machine upkeep", Hint = "ASC_CYB_MAINT", SubGroup = "Supreme Realms")] public bool AscCybMaint { get; set; } = true;
    [Setting("Cybernetic Overlord: disable machines", Hint = "ASC_CYB_STUN", SubGroup = "Supreme Realms")] public bool AscCybStun { get; set; } = true;
    [Setting("Cybernetic Overlord: carry to recruit", Hint = "ASC_CYB_RECRUIT", SubGroup = "Supreme Realms")] public bool AscCybRecruit { get; set; } = true;
    [Setting("Heavenly Artifice", Hint = "ASC_ART_* (craft speed, quality, output)", SubGroup = "Supreme Realms")] public bool AscArtifice { get; set; } = true;

    [Setting("Shadow Step", Hint = "P19", SubGroup = "Extra perks")] public bool P19 { get; set; } = true;
    [Setting("Phantom Movement", Hint = "P208", SubGroup = "Extra perks")] public bool P208 { get; set; } = true;
    [Setting("Turret Barrage", Hint = "P215", SubGroup = "Extra perks")] public bool P215 { get; set; } = true;
    [Setting("Prosthetic Tuning", Hint = "P201", SubGroup = "Extra perks")] public bool P201 { get; set; } = true;
    [Setting("Grand Fletcher", Hint = "P212", SubGroup = "Extra perks")] public bool P212 { get; set; } = true;
    [Setting("Phantom Pickpocket", Hint = "P24 (V by default)", SubGroup = "Extra perks")] public bool P24 { get; set; } = true;
    [Setting("Silent Touch", Hint = "P25", SubGroup = "Extra perks")] public bool P25 { get; set; } = true;
    [Setting("Master Key", Hint = "P26", SubGroup = "Extra perks")] public bool P26 { get; set; } = true;
    [Setting("Silent Verdict", Hint = "ASSN_SYS: the Assassinate card's mode", SubGroup = "Extra perks")] public bool Assassinate { get; set; } = true;

    [Setting("Assassinate mode", Hint = "The Assassinate card: Sneak (270 s out) or Harvest (silent kill). Assassination above 60, player characters.",
        Options = [VerdictOff, VerdictSneak, VerdictHarvest], SubGroup = "Extra perks")]
    public string AssassinateMode { get; set; } = VerdictOff;

    public const string VerdictOff = "Off", VerdictSneak = "Sneak", VerdictHarvest = "Harvest";

    internal static readonly string[] VerdictModes = [VerdictOff, VerdictSneak, VerdictHarvest];

    internal bool InChinese => Language == Chinese;

    /// <summary>A copy for the tick: the simulation reads one fixed set of values per tick.</summary>
    internal DesoSettings Copy() => (DesoSettings)MemberwiseClone();

    internal int ScopeIndex => Math.Max(0, Array.IndexOf(Scopes, Scope));

    /// <summary>The attack cap in force: the custom value or the tier, clamped to [0.1, 100] and rounded to 0.01 as the original's.</summary>
    internal float AttackCap
    {
        get
        {
            float cap = CustomCap ? CustomCapValue : float.Parse(Cap, CultureInfo.InvariantCulture);
            if (!float.IsFinite(cap) || cap <= 0) return 4.8f;

            return MathF.Round(Math.Clamp(cap, 0.1f, 100) * 100) / 100;
        }
    }
}
