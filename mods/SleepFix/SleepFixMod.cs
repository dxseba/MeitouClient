using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SleepFix;

/// <summary>
/// Port of SleepFix (workshop 3787375792): a player character that is not fully healed gets the player's own bed order (<c>World.GiveOrder</c>,
/// <c>TaskType.UseBed</c>) to a free bed of its faction, a robot due for repair to a Skeleton Repair Bed; the order is released once it is healed,
/// when it cannot reach the bed, or when it stops healing.
/// </summary>
public sealed class SleepFixMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new SleepSettings();
        context.AddSettings(settings);
        context.AddSystem("sleepfix", _ => new SleepSystem(settings, context.Population), SystemAnchor.End);
        context.Log("SleepFix initialized");
    }
}

/// <summary>
/// The original's SleepFix.cfg keys and defaults (each hint names its key).
/// </summary>
public sealed class SleepSettings
{
    /// <summary>
    /// <c>autoBedOrder=1</c>.
    /// </summary>
    [Setting("Send wounded to bed", Hint = "autoBedOrder: send characters who are not fully healed to bed.")]
    public bool AutoBedOrder { get; set; } = true;

    /// <summary>
    /// <c>healThreshold=1.0</c>: below it (less 0.0005) a character is wounded, at or above it healed.
    /// </summary>
    [Setting("Healed at", Hint = "healThreshold: every part at this share of its health counts as healed (1 = 100%).", Min = 0.5, Max = 1,
        Step = 0.01)]
    public float HealThreshold { get; set; } = 1;

    /// <summary>
    /// <c>clearBedOrder=1</c>.
    /// </summary>
    [Setting("Release bed order when healed", Hint = "clearBedOrder: healed characters get up and go back to work.")]
    public bool ClearBedOrder { get; set; } = true;

    /// <summary>
    /// <c>waitForTreatment=1</c>.
    /// </summary>
    [Setting("Wait for treatment", Hint = "waitForTreatment: no bed order while unbandaged cuts remain (they don't heal in bed).")]
    public bool WaitForTreatment { get; set; } = true;

    /// <summary>
    /// <c>ownBedsOnly=1</c>.
    /// </summary>
    [Setting("Own beds only", Hint = "ownBedsOnly: only beds your faction owns (no rented or town beds).")]
    public bool OwnBedsOnly { get; set; } = true;

    /// <summary>
    /// <c>bedReachTimeout=60</c> seconds.
    /// </summary>
    [Setting("Bed reach timeout", Hint = "bedReachTimeout: seconds to lie down before another bed is tried (0 off).", Min = 0, Max = 300)]
    public int BedReachTimeout { get; set; } = 60;

    /// <summary>
    /// <c>stuckRelease=300</c> seconds.
    /// </summary>
    [Setting("Stuck release", Hint = "stuckRelease: release the bed order when nothing heals for this many seconds (0 off).", Min = 0, Max = 1800)]
    public int StuckRelease { get; set; } = 300;

    /// <summary>
    /// <c>bedOrderCooldownMs=15000</c>.
    /// </summary>
    [Setting("Order cooldown", Hint = "bedOrderCooldownMs: how soon the same character can be ordered again, in ms.", Min = 1000, Max = 60000,
        Step = 1000)]
    public int BedOrderCooldownMs { get; set; } = 15000;

    /// <summary>
    /// <c>respectMoveOrder=1</c>.
    /// </summary>
    [Setting("Respect move orders", Hint = "respectMoveOrder: nobody is sent while a move order you gave is running.")]
    public bool RespectMoveOrder { get; set; } = true;

    /// <summary>
    /// <c>skipCombat=1</c>.
    /// </summary>
    [Setting("Skip combat", Hint = "skipCombat: nobody is sent to bed mid-fight.")]
    public bool SkipCombat { get; set; } = true;

    /// <summary>
    /// <c>ignoreRobotParts=1</c>.
    /// </summary>
    [Setting("Ignore robot parts", Hint = "ignoreRobotParts: robot parts are left out of the healed check (normal beds don't fix them).")]
    public bool IgnoreRobotParts { get; set; } = true;

    /// <summary>
    /// <c>wearThreshold=5.0</c>.
    /// </summary>
    [Setting("Repair bed wear", Hint = "wearThreshold: wear on a robot part that sends it to a Skeleton Repair Bed (0 off).", Min = 0, Max = 50,
        Step = 0.5)]
    public float WearThreshold { get; set; } = 5;

    /// <summary>
    /// <c>repairRobotHp=0.90</c>.
    /// </summary>
    [Setting("Repair bed health", Hint = "repairRobotHp: also go when a robot part's health is below this share (0 off; needs the wear setting).",
        Min = 0, Max = 1, Step = 0.01)]
    public float RepairRobotHp { get; set; } = 0.9f;
}
