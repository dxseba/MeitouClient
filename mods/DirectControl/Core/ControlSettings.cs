using Meitou.Modding;

namespace Meitou.DirectControl;

/// <summary>
/// The settings tab (the original's Options > Direct Control, its <c>WASDCombatPlugin.ini</c> keys and defaults; README "Facts"), grouped by mode.
/// The Action RPG port starts from its own defaults (<see cref="ForActionRpg"/>) and hides the rows its scheme has no use for.
/// </summary>
public sealed class ControlSettings
{
    /// <summary>
    /// The Direct Control scheme (keyboard combat, P / Ctrl views); false: the Action RPG scheme (mouse combat, one shoulder/first-person view).
    /// </summary>
    public bool Classic { get; set; } = true;

    /// <summary>
    /// The Action RPG scheme (for its own rows).
    /// </summary>
    public bool ActionScheme => !Classic;

    [Setting("Attack key", Hint = "One press, one swing at the enemy you point at (a key name such as E).", SubGroup = "Keys", MaxLength = 16,
        VisibleWhen = nameof(Classic))]
    public string AttackKey { get; set; } = "E";

    [Setting("Block key", Hint = "Hold to guard; tap as a hit lands for a perfect block.", SubGroup = "Keys", MaxLength = 16,
        VisibleWhen = nameof(Classic))]
    public string BlockKey { get; set; } = "F";

    [Setting("Dodge key", Hint = "Press as an attack comes in.", SubGroup = "Keys", MaxLength = 16, VisibleWhen = nameof(Classic))]
    public string DodgeKey { get; set; } = "Q";

    [Setting("Cap WASD speed", Hint = "Keep WASD speed under the character's real top speed (injuries, loads, shackles).", SubGroup = "Movement")]
    public bool SpeedCap { get; set; } = true;

    [Setting("WASD speed multiplier", Hint = "Scales WASD movement speed.", Min = 0.25, Max = 3, Step = 0.05, SubGroup = "Movement")]
    public float SpeedMult { get; set; } = 1;

    [Setting("Camera faces character in inventory", Hint = "The camera turns to face your character while its inventory is open (not in combat); "
        + "off: keep moving with WASD.", SubGroup = "Movement")]
    public bool InventoryFaceCam { get; set; } = true;

    [Setting("Character still blocks on their own", Hint = "In manual combat your character also blocks by itself.", SubGroup = "Manual combat")]
    public bool AiBlocks { get; set; }

    [Setting("Character still dodges on their own", Hint = "In manual combat your character also dodges by itself.", SubGroup = "Manual combat",
        VisibleWhen = nameof(Classic))]
    public bool AiDodges { get; set; }

    [Setting("Longest held block (ms)", Hint = "A held Block guards this long, then drops.", Min = 200, Max = 5000, Step = 50,
        SubGroup = "Manual combat", VisibleWhen = nameof(Classic))]
    public int BlockHoldMs { get; set; } = 1200;

    [Setting("Block cooldown (ms)", Hint = "Wait before Block counts again.", Min = 0, Max = 3000, Step = 50, SubGroup = "Manual combat",
        VisibleWhen = nameof(Classic))]
    public int BlockCooldownMs { get; set; } = 800;

    [Setting("Perfect-block window (ms)", Hint = "After a Block tap, hits from the front are stopped whatever the skill.", Min = 100, Max = 3000,
        Step = 50, SubGroup = "Manual combat", VisibleWhen = nameof(Classic))]
    public int PerfectBlockWindowMs { get; set; } = 1000;

    [Setting("Perfect-block reward (ms)", Hint = "After a perfect block, how long the next press is free (blue glow).", Min = 0, Max = 5000,
        Step = 50, SubGroup = "Manual combat", VisibleWhen = nameof(Classic))]
    public int PerfectBlockRewardMs { get; set; } = 1200;

    [Setting("Dodge window (ms)", Hint = "How long a Dodge press is remembered.", Min = 100, Max = 3000, Step = 50, SubGroup = "Manual combat",
        VisibleWhen = nameof(Classic))]
    public int DodgeWindowMs { get; set; } = 600;

    [Setting("Dodge skill bonus", Hint = "Dodge skill added for a timed press.", Min = 0, Max = 100, Step = 1, SubGroup = "Manual combat",
        VisibleWhen = nameof(Classic))]
    public int DodgeSkillBonus { get; set; } = 20;

    [Setting("Dodge cooldown (ms)", Hint = "Extra wait after a dodge.", Min = 0, Max = 3000, Step = 50, SubGroup = "Manual combat",
        VisibleWhen = nameof(Classic))]
    public int DodgeCooldownMs { get; set; } = 300;

    [Setting("Attack window (ms)", Hint = "How long an Attack press is remembered.", Min = 100, Max = 3000, Step = 50, SubGroup = "Manual combat")]
    public int AttackWindowMs { get; set; } = 400;

    [Setting("Attack step-in (ms)", Hint = "Step-in time for an out-of-reach attack.", Min = 0, Max = 5000, Step = 50, SubGroup = "Manual combat")]
    public int AttackApproachMs { get; set; } = 1500;

    [Setting("Attack recovery (ms)", Hint = "Pause after a swing before the next Attack press counts.", Min = 0, Max = 3000, Step = 50,
        SubGroup = "Manual combat")]
    public int AttackRecoveryMs { get; set; } = 400;

    [Setting("Dodge recovery (ms)", Hint = "Pause after a dodge before the next Dodge press counts.", Min = 0, Max = 3000, Step = 50,
        SubGroup = "Manual combat", VisibleWhen = nameof(Classic))]
    public int DodgeRecoveryMs { get; set; } = 400;

    [Setting("Double-press guard (ms)", Hint = "Ignore presses closer together than this.", Min = 0, Max = 1000, Step = 10,
        SubGroup = "Manual combat")]
    public int InputRepeatGuardMs { get; set; } = 250;

    [Setting("Crosshair target cone (degrees)", Hint = "First and third person: the enemy nearest the crosshair within this cone is the target.",
        Min = 5, Max = 90, Step = 1, SubGroup = "Manual combat")]
    public int AimConeDeg { get; set; } = 35;

    [Setting("Target name and health bars", Hint = "Name and blood / head / chest / stomach bars over the target.", SubGroup = "Manual combat")]
    public bool AimNameTag { get; set; } = true;

    [Setting("Weapon glint", Hint = "An enemy's weapon glows while it swings at you.", SubGroup = "Manual combat", VisibleWhen = nameof(Classic))]
    public bool WeaponGlint { get; set; } = true;

    [Setting("Glint length (ms)", Hint = "How long a swing's glint lasts.", Min = 100, Max = 3000, Step = 50, SubGroup = "Manual combat",
        VisibleWhen = nameof(Classic))]
    public int GlintLeadMs { get; set; } = 700;

    [Setting("Camera distance", Hint = "Third-person camera: distance behind the shoulder.", Min = 5, Max = 120, Step = 1,
        SubGroup = "Third person")]
    public float OtsDistance { get; set; } = 20;

    [Setting("Camera height", Hint = "Third-person camera: height of the look point over the feet.", Min = 5, Max = 30, Step = 0.5,
        SubGroup = "Third person")]
    public float OtsHeight { get; set; } = 15;

    [Setting("Camera side", Hint = "Third-person camera: how far to the side of the character.", Min = 0, Max = 15, Step = 0.5,
        SubGroup = "Third person")]
    public float OtsSide { get; set; } = 4;

    [Setting("Third-person mouse sensitivity", Hint = "Mouse-look speed in the third-person camera.", Min = 0.1, Max = 5, Step = 0.05,
        SubGroup = "Third person")]
    public float OtsSensitivity { get; set; } = 1;

    [Setting("Mouse sensitivity", Hint = "First-person mouse-look speed.", Min = 0.1, Max = 5, Step = 0.05, SubGroup = "First person")]
    public float Sensitivity { get; set; } = 1;

    [Setting("Field of view", Hint = "First-person field of view in degrees.", Min = 50, Max = 110, Step = 1, SubGroup = "First person")]
    public float Fov { get; set; } = 75;

    [Setting("Clipping distance", Hint = "Lower shows more of your own body; higher keeps the view clear of it.", Min = 0.5, Max = 5, Step = 0.05,
        SubGroup = "First person")]
    public float NearClip { get; set; } = 1;

    [Setting("Hide your own head", Hint = "Hide your head, hair and headgear in first person.", SubGroup = "First person")]
    public bool HideHead { get; set; } = true;

    [Setting("Body is solid from the inside", Hint = "Draw your own body and clothes two-sided in first person.", SubGroup = "First person")]
    public bool BodySolidInside { get; set; } = true;

    [Setting("Grass range in first person", Hint = "Scales the grass draw range in first person (the original's GrassRangeMult).", Min = 0.25,
        Max = 2, Step = 0.05, SubGroup = "First person")]
    public float GrassRangeMult { get; set; } = 1;

    [Setting("Show floors below you in buildings", Hint = "First person: buildings show every storey, not only the viewed floor.",
        SubGroup = "First person")]
    public bool RevealFloors { get; set; } = true;

    [Setting("Show direction arrows", Hint = "Show the mouse direction and the locked attack direction around the crosshair.",
        SubGroup = "Manual combat", VisibleWhen = nameof(ActionScheme))]
    public bool ShowDirectionArrows { get; set; } = true;

    [Setting("Combat focus HUD", Hint = "In V mode hide the squad, orders, shortcut, floor and build panels while the view is held.",
        SubGroup = "Manual combat", VisibleWhen = nameof(ActionScheme))]
    public bool CombatFocusHud { get; set; } = true;

    [Setting("Eye height", Hint = "Raise or lower the eye from the head bone.", Min = -5, Max = 5, Step = 0.1, SubGroup = "First person")]
    public float EyeDrop { get; set; } = -0.2f;

    [Setting("Eye forward", Hint = "Move the eye forward of the head bone.", Min = -3, Max = 5, Step = 0.1, SubGroup = "First person")]
    public float EyeForward { get; set; }

    /// <summary>
    /// The Action RPG fork's defaults (its shipped <c>WASDCombatPlugin.ini</c>): its own scheme, auto-block off (<c>ManualBlock = true</c>), attack
    /// recovery 300 ms, double-press guard 180 ms, shoulder camera 23 / 18 / 3, first-person field of view 90.
    /// </summary>
    public static ControlSettings ForActionRpg() => new()
    {
        Classic = false,
        AttackRecoveryMs = 300,
        InputRepeatGuardMs = 180,
        OtsDistance = 23,
        OtsHeight = 18,
        OtsSide = 3,
        Fov = 90,
        EyeDrop = 0,
    };

    /// <summary>
    /// What the simulation copies of these settings.
    /// </summary>
    internal Tuning Tuning() => new(AttackWindowMs, AttackApproachMs, AttackRecoveryMs, BlockCooldownMs, PerfectBlockWindowMs, PerfectBlockRewardMs,
        DodgeWindowMs, DodgeCooldownMs, DodgeRecoveryMs, DodgeSkillBonus, AiBlocks, AiDodges, SpeedCap, SpeedMult, !Classic);
}
