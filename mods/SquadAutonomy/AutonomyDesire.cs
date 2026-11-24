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
/// A squad's rest rules (the original's Basic category; defaults Rest Until Healed When Wounded on, Rest Health Threshold 50, Healed Health
/// Threshold 90, Allow Sleep Outside Work Hours off, Allow Using Paid Beds off).
/// </summary>
public sealed record RestRules(bool UntilHealed = true, int RestHealth = 50, int HealedHealth = 90, bool SleepOffWork = false,
    bool PaidBeds = false)
{
    /// <summary>The original's defaults.</summary>
    public static readonly RestRules Default = new();
}

/// <summary>
/// A member's own state as of the system's last schedule: player orders queued (<c>hasOrders</c>), inside a building, asleep (lying in a bed),
/// under melee attack (<c>isLiterallyUnderMeleeAttackRightNowForSure</c>, <c>CharacterFacts.UnderMeleeAttack</c>), its current goal, an
/// organic race, its rested level (medical <c>restedState</c>), its organic first aid need and the one for its race's kit, and the game hours
/// since it last slept (<c>lastSlept</c>).
/// </summary>
readonly record struct MemberState(bool HasOrders, bool Inside, bool Asleep, bool Threatened, TaskType? Goal, bool Organic, float Rested,
    float AidNeed, float KitNeed, double HoursSinceSlept);

/// <summary>
/// What the filters know of a member of an autonomous squad with a plan, as of the system's last schedule: its squad's switches, rest rules and
/// labour priorities, whether it is off work (the original's <c>isRestTime</c>), whether its squad leader lies in a bed or relaxes in town (its
/// current goal <c>RELAX_IN_TOWN_PACKAGE</c>), whether it leads the squad itself, and its own state.
/// </summary>
readonly record struct MemberRule(CombatSwitches Switches, RestRules Rest, LabourPriorities Labour, bool OffWork, bool LeaderInBed,
    bool LeaderRelaxing, bool IsLeader, MemberState Self);

/// <summary>
/// The original's score hooks (its shipped source and DLL) as a chooser desire filter on an autonomous squad's members. <c>Tasker::score</c>, on
/// list goals only (it leaves perma jobs alone):
/// <list type="bullet">
/// <item>Stay in home and sit around: 0 while it has player orders; x0.0001 at work.</item>
/// <item>Man a turret (on a building too): 0 with Man Turrets off or off work.</item>
/// <item>Bodyguard and follow the squad leader: 0 while the leader lies in a bed or relaxes in town.</item>
/// <item>The gate, the guard nodes of the home building and the home town, territorial aggression, attack enemies and the mines: 0 off work;
/// territorial aggression and attack enemies 0 with Attack Enemies off.</item>
/// <item>Protect allies (staying in town too) and protect the own squad: 0 with Protect Allies off, and while it sleeps with no melee attacker
/// on it.</item>
/// <item>Medic and robot repair jobs: 0 with Do Medic off, else twice its own first aid need for its race's kit.</item>
/// <item>Patrolling the town: 0 off work inside a building, unless it is already its goal; while it is the current goal its held score decays
/// to max(0.0001, held x 0.95) each think (<see cref="GoalDesireKind.HeldScore"/>), so the member turns to something else.</item>
/// <item>Relaxing in town: 1 for a member whose leader relaxes in town.</item>
/// <item>Find and rescue (if there are beds, the leader): 0 with Rescue off.</item>
/// <item>Acquiring food at the home base and grabbing one food: x10.</item>
/// </list>
/// <c>AI::scoreGoToBed</c> (both bed goals) and <c>AI::scoreGetOutOfBed</c> (getting out of bed if it is an emergency), on goals and plan steps:
/// see <see cref="GoToBed"/> and <see cref="GetOutOfBed"/>. A pure read of the published table, on any thread.
/// </summary>
sealed class AutonomyDesire(Func<FrozenDictionary<CharacterId, MemberRule>?> members) : IGoalDesireFilter
{
    /// <summary>The first aid need under which a member counts as needing no treatment to rest (the original's 0.1).</summary>
    const float NoAidNeed = 0.1f;

    public float Desire(World world, in GoalDesireAsk ask)
    {
        if (ask.Kind is not (GoalDesireKind.Goal or GoalDesireKind.PlanStep or GoalDesireKind.HeldScore)) return ask.Desire;
        if (members() is not { } table || !table.TryGetValue(ask.Character, out var rule)) return ask.Desire;

        float score = ask.Kind == GoalDesireKind.HeldScore ? Held(ask) : ask.Type switch
        {
            TaskType.GoHomeAndGoToBed or TaskType.GoHomeAndGoToBedSecure => GoToBed(rule),
            TaskType.GetOutOfBedIfItsEmergency => GetOutOfBed(rule, ask.Desire),
            _ => ask.Kind == GoalDesireKind.Goal ? Score(ask.Type, rule, ask.Desire) : ask.Desire,
        };
        if (score != ask.Desire && AutonomyLog.On(LogSeverity.Debug, spam: true))
            AutonomyLog.Write(LogSeverity.Debug, $"score TaskType: {(int)ask.Type} - {ask.Desire} -> {score}", spam: true);

        return score;
    }

    /// <summary>
    /// The current goal's held score: patrolling the town decays (<c>currentGoalScore = max(0.0001, currentGoalScore x 0.95)</c>, <b>Verified</b>
    /// original DLL (decompiled)); the first think on it starts from this think's score. Any other goal keeps the game's.
    /// </summary>
    static float Held(in GoalDesireAsk ask)
    {
        if (ask.Type != TaskType.PatrolTown) return ask.Desire;

        float held = float.IsNaN(ask.HeldScore) ? ask.Desire : ask.HeldScore;
        return MathF.Max(PatrolFloor, held * PatrolDecay);
    }

    /// <summary>The patrol decay's factor and floor.</summary>
    const float PatrolDecay = 0.95f, PatrolFloor = 0.0001f;

    /// <summary>
    /// The original's <c>Tasker::score</c> rule for <paramref name="type"/>.
    /// </summary>
    static float Score(TaskType type, MemberRule rule, float desire) => type switch
    {
        TaskType.StayInHome or TaskType.SitAround => rule.Self.HasOrders ? 0 : rule.OffWork ? desire : desire * 0.0001f,
        TaskType.ManATurret or TaskType.ManATurretOnBuilding => !Has(rule, CombatSwitches.ManTurrets) || rule.OffWork ? 0 : desire,
        TaskType.Bodyguard or TaskType.FollowSquadleader => rule.LeaderInBed || rule.LeaderRelaxing ? 0 : desire,
        TaskType.TerritorialAggressionButDontLeaveHome or TaskType.AttackEnemies =>
            !Has(rule, CombatSwitches.AttackEnemies) || rule.OffWork ? 0 : desire,
        TaskType.ManTheGate or TaskType.StandAtGuardNodeHomebuildingIndoorsOnly or TaskType.StandAtGuardNodeHomebuilding
            or TaskType.StandAtGuardNodeHometownOutside or TaskType.AutoLabouringMines or TaskType.AutoLabouringMinesPretend =>
            rule.OffWork ? 0 : desire,
        TaskType.ProtectAllies or TaskType.ProtectAlliesStayInTown or TaskType.ProtectOwnSquad =>
            !Has(rule, CombatSwitches.ProtectAllies) || rule.Self.Asleep && !rule.Self.Threatened ? 0 : desire,
        TaskType.JobMedic or TaskType.JobRepairRobot => Has(rule, CombatSwitches.DoMedic) ? rule.Self.KitNeed * 2 : 0,
        TaskType.PatrolTown => rule.Self.Goal != TaskType.PatrolTown && rule.OffWork && rule.Self.Inside ? 0 : desire,
        TaskType.RelaxInTownPackage => !rule.IsLeader && rule.LeaderRelaxing ? 1 : desire,
        TaskType.FindAndRescue or TaskType.FindAndRescueIfTheresBeds or TaskType.FindAndRescueLeader =>
            Has(rule, CombatSwitches.DoRescue) ? desire : 0,
        TaskType.AquireFoodAtHomebase or TaskType.GrabOneFood => desire * 10,
        _ => desire,
    };

    /// <summary>
    /// The original's <c>scoreGoToBed</c>, which replaces the game's: with Rest Until Healed, an organic member resting below the Healed threshold
    /// that needs no first aid scores 5 asleep or off work, 20 at or below the Rest threshold; then with Allow Sleep Outside Work Hours, an awake
    /// member off work scores by the time since it last slept (under 2 h 0, 8 h 0.01, 12 h 0.1, 18 h 0.5, else 1); otherwise 0.
    /// </summary>
    static float GoToBed(MemberRule rule)
    {
        var self = rule.Self;
        if (Recovering(rule))
        {
            if (self.Asleep || rule.OffWork) return 5;
            if (self.Rested <= rule.Rest.RestHealth / 100f) return 20;
        }

        if (!rule.Rest.SleepOffWork || !rule.OffWork || self.Asleep) return 0;

        double minutes = self.HoursSinceSlept * 60;
        return minutes < 120 ? 0 : minutes < 480 ? 0.01f : minutes < 720 ? 0.1f : minutes < 1080 ? 0.5f : 1;
    }

    /// <summary>
    /// The original's <c>scoreGetOutOfBed</c> while the member's goal is a bed goal: 1 under melee attack; 0 while recovering (as in
    /// <see cref="GoToBed"/>); 1 at work with Allow Sleep Outside Work Hours; otherwise the game's.
    /// </summary>
    static float GetOutOfBed(MemberRule rule, float desire)
    {
        if (rule.Self.Goal is not (TaskType.GoHomeAndGoToBed or TaskType.GoHomeAndGoToBedSecure)) return desire;
        if (rule.Self.Threatened) return 1;
        if (Recovering(rule)) return 0;

        return rule.Rest.SleepOffWork && !rule.OffWork ? 1 : desire;
    }

    /// <summary>Rest Until Healed applies: an organic member resting below the Healed threshold with no first aid to wait for.</summary>
    static bool Recovering(MemberRule rule) =>
        rule.Rest.UntilHealed && rule.Self.Organic && rule.Self.Rested < rule.Rest.HealedHealth / 100f && rule.Self.AidNeed < NoAidNeed;

    static bool Has(MemberRule rule, CombatSwitches flag) => (rule.Switches & flag) != 0;
}
