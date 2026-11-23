using System.Collections.Frozen;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Meitou.SquadAutonomy;

/// <summary>
/// The original's combat switches (its window's Combat category; defaults Attack Enemies, Protect Allies, Do Medic and Rescue Downed Allies on,
/// Man Turrets off).
/// </summary>
[Flags]
public enum CombatSwitches
{
    None = 0,
    AttackEnemies = 1,
    ProtectAllies = 2,
    DoMedic = 4,
    DoRescue = 8,
    ManTurrets = 16,

    /// <summary>
    /// The original's defaults.
    /// </summary>
    Default = AttackEnemies | ProtectAllies | DoMedic | DoRescue,
}

/// <summary>
/// What the desire filter knows of a member of an autonomous squad with a plan, as of the system's last schedule: its squad's switches, whether
/// it is off work (the original's <c>isRestTime</c>), and whether its squad leader lies in a bed.
/// </summary>
readonly record struct MemberRule(CombatSwitches Switches, bool OffWork, bool LeaderInBed);

/// <summary>
/// The original's task score hook (<c>Tasker::score</c>, its shipped source) as a chooser desire filter on the goals of an autonomous squad's
/// members (list goals only; the original leaves perma jobs alone):
/// <list type="bullet">
/// <item>Stay in home and sit around: x0.0001 at work.</item>
/// <item>Man a turret (on a building too): 0 with Man Turrets off or off work.</item>
/// <item>Bodyguard and follow the squad leader: 0 while the leader lies in a bed.</item>
/// <item>The gate, the guard nodes of the home building and the home town, territorial aggression, attack enemies and the mines: 0 off work;
/// territorial aggression and attack enemies 0 with Attack Enemies off.</item>
/// <item>Protect allies (staying in town too) and protect the own squad: 0 with Protect Allies off.</item>
/// <item>Medic and robot repair jobs: 0 with Do Medic off.</item>
/// <item>Find and rescue (if there are beds, the leader): 0 with Rescue off.</item>
/// <item>Acquiring food at the home base and grabbing one food: x10.</item>
/// </list>
/// A pure read of the published table, on any thread.
/// </summary>
sealed class AutonomyDesire(Func<FrozenDictionary<CharacterId, MemberRule>?> members) : IGoalDesireFilter
{
    public float Desire(World world, in GoalDesireAsk ask)
    {
        if (ask.Kind != GoalDesireKind.Goal || members() is not { } table || !table.TryGetValue(ask.Character, out var rule)) return ask.Desire;

        return Score(ask.Type, rule, ask.Desire);
    }

    /// <summary>
    /// The original's rule for <paramref name="type"/>.
    /// </summary>
    static float Score(TaskType type, MemberRule rule, float desire) => type switch
    {
        TaskType.StayInHome or TaskType.SitAround => rule.OffWork ? desire : desire * 0.0001f,
        TaskType.ManATurret or TaskType.ManATurretOnBuilding => !Has(rule, CombatSwitches.ManTurrets) || rule.OffWork ? 0 : desire,
        TaskType.Bodyguard or TaskType.FollowSquadleader => rule.LeaderInBed ? 0 : desire,
        TaskType.TerritorialAggressionButDontLeaveHome or TaskType.AttackEnemies =>
            !Has(rule, CombatSwitches.AttackEnemies) || rule.OffWork ? 0 : desire,
        TaskType.ManTheGate or TaskType.StandAtGuardNodeHomebuildingIndoorsOnly or TaskType.StandAtGuardNodeHomebuilding
            or TaskType.StandAtGuardNodeHometownOutside or TaskType.AutoLabouringMines or TaskType.AutoLabouringMinesPretend =>
            rule.OffWork ? 0 : desire,
        TaskType.ProtectAllies or TaskType.ProtectAlliesStayInTown or TaskType.ProtectOwnSquad =>
            Has(rule, CombatSwitches.ProtectAllies) ? desire : 0,
        TaskType.JobMedic or TaskType.JobRepairRobot => Has(rule, CombatSwitches.DoMedic) ? desire : 0,
        TaskType.FindAndRescue or TaskType.FindAndRescueIfTheresBeds or TaskType.FindAndRescueLeader =>
            Has(rule, CombatSwitches.DoRescue) ? desire : 0,
        TaskType.AquireFoodAtHomebase or TaskType.GrabOneFood => desire * 10,
        _ => desire,
    };

    static bool Has(MemberRule rule, CombatSwitches flag) => (rule.Switches & flag) != 0;
}
