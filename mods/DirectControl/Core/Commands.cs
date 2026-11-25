using System.Numerics;
using Meitou.Simulation;

namespace Meitou.DirectControl;

/// <summary>
/// What the simulation side needs of the settings (copied in a serial step, as docs/modding.md "Settings" asks): the manual combat windows in
/// milliseconds, the dodge bonus, whether the character still blocks or dodges on its own, and the speed cap and multiplier.
/// </summary>
public sealed record Tuning(
    int AttackWindowMs, int AttackApproachMs, int AttackRecoveryMs, int BlockCooldownMs, int PerfectBlockWindowMs, int PerfectBlockRewardMs,
    int DodgeWindowMs, int DodgeCooldownMs, int DodgeRecoveryMs, float DodgeSkillBonus, bool AiBlocks, bool AiDodges, bool SpeedCap,
    float SpeedMult, bool Directional);

/// <summary>
/// Direct control of <paramref name="Character"/> (none: off) with manual combat on or off, under <paramref name="Tuning"/>.
/// </summary>
public sealed record ControlCommand(CharacterId Character, bool Manual, Tuning Tuning) : SimCommand;

/// <summary>
/// The controlled character's wanted step: a unit ground direction (zero: stand), the speed mode to walk it at (null: its own run speed), and
/// with <paramref name="Face"/> the yaw to turn to while standing (first person: the body follows the view).
/// </summary>
public sealed record SteerCommand(CharacterId Character, Vector2 Direction, byte? Mode, bool Face, float FaceYaw) : SimCommand;

/// <summary>
/// The kinds of manual combat press.
/// </summary>
public enum PressKind : byte
{
    /// <summary>
    /// One swing (Attack).
    /// </summary>
    Attack,

    /// <summary>
    /// Block went down: a tap opens the perfect-block window.
    /// </summary>
    Block,

    /// <summary>
    /// A timed dodge.
    /// </summary>
    Dodge,

    /// <summary>
    /// Block was let go (or held past its longest guard).
    /// </summary>
    BlockUp,
}

/// <summary>
/// A manual combat press by <paramref name="Character"/>. <paramref name="Direction"/> is the Action RPG scheme's chosen technique direction (0:
/// any); <paramref name="Approach"/> lengthens an attack press to the step-in time (the target is out of reach).
/// </summary>
public sealed record CombatPress(CharacterId Character, PressKind Kind, int Direction = 0, bool Approach = false) : SimCommand;

/// <summary>
/// The Action RPG scheme's stance: <paramref name="Sheathed"/> puts the weapon away (no swing or block until drawn), else draws it.
/// </summary>
public sealed record StanceCommand(CharacterId Character, bool Sheathed) : SimCommand;

/// <summary>
/// The Action RPG scheme's weapon slot switch: the weapon in hand goes from the back slot to the hip one, or back.
/// </summary>
public sealed record SwapWeaponCommand(CharacterId Character) : SimCommand;
