using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>A run of job rows that move together: a job and the companion the engine added after it (152 + 124, 148 + 106, ...).</summary>
public readonly record struct JobUnit(int Start, int Length)
{
    public int End => Start + Length;

    public bool Contains(int row) => row >= Start && row < End;
}

/// <summary>The job facts the window works with: healing roles and their order, companions, the rows invalid cleanup never touches.</summary>
public static class JobRules
{
    /// <summary>The healing roles in the order Prioritize Healing puts them (the original's JobBatchHealingRole), then Engineer.</summary>
    static readonly TaskType[] PriorityOrder =
    [
        TaskType.FindAndRescueIfTheresBeds, TaskType.FindBedAndPutIn, TaskType.JobMedic, TaskType.JobRepairRobot, TaskType.SplintJob,
        TaskType.JobBuilder,
    ];

    /// <summary>The five roles Add Medic/Robotics fills, each with the order whose job it is (<see cref="AddOrderJob"/>).</summary>
    public static readonly (TaskType Role, TaskType Ordered)[] HealingRoles =
    [
        (TaskType.FindAndRescueIfTheresBeds, TaskType.FindAndRescueIfTheresBeds),
        (TaskType.FindBedAndPutIn, TaskType.FindBedAndPutIn),
        (TaskType.JobMedic, TaskType.FirstAidOrder),
        (TaskType.JobRepairRobot, TaskType.FirstAidRobot),
        (TaskType.SplintJob, TaskType.SplintOrder),
    ];

    /// <summary>
    /// The rows Remove Invalid Jobs never removes (the original's IsJobBatchInvalidRemovalProtectedTask, as far as Meitou has the types).
    /// </summary>
    static readonly HashSet<TaskType> Protected =
    [
        TaskType.JobBuilder, TaskType.JobMedic, TaskType.JobRepairRobot, TaskType.FindAndRescueIfTheresBeds, TaskType.FindBedAndPutIn,
        TaskType.SplintJob, TaskType.Build, TaskType.AddMaterialsToBuilding, TaskType.Repair, TaskType.Dismantle, TaskType.LootAnimalsJob,
        TaskType.PickupIntruders, TaskType.TakeIntruderOutside, TaskType.PickupIntrudersTown, TaskType.TakeIntruderOutsideTown,
        TaskType.FirstAidOrder, TaskType.FirstAidRobot, TaskType.SplintOrder, TaskType.FindAndRescueLeader, TaskType.PutSomeoneInBed,
        TaskType.UseBed, TaskType.GoHomeAndGoToBed, TaskType.CarryWoundedSlaves,
    ];

    /// <summary>Prioritize Healing's rank: Rescue 0, Put in Bed 1, Medic 2, Robotics 3, Splint 4, Engineer 5, the rest 6.</summary>
    public static int Rank(TaskType type) => Array.IndexOf(PriorityOrder, type) is var i and >= 0 ? i : PriorityOrder.Length;

    /// <summary>Whether <paramref name="type"/> is one of the five healing roles.</summary>
    public static bool IsHealing(TaskType type) => Rank(type) < 5;

    public static bool IsProtected(TaskType type) => Protected.Contains(type);

    /// <summary>The companion the engine adds after a job of <paramref name="type"/> (PermaJobs' rows with a companion), or null.</summary>
    static TaskType? Companion(TaskType type) => type switch
    {
        TaskType.OperateAutomaticMachinery => TaskType.OperateStorage,
        TaskType.Bodyguard => TaskType.StayCloseToTarget,
        TaskType.FindAndRescueIfTheresBeds => TaskType.FindBedAndPutIn,
        TaskType.DeadGuysGoInThePot => TaskType.FindADeadGuy,
        TaskType.PickupIntruders => TaskType.TakeIntruderOutside,
        TaskType.PickupIntrudersTown => TaskType.TakeIntruderOutsideTown,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="next"/> is the companion the engine added with <paramref name="primary"/>: its type, the same target.
    /// </summary>
    static bool IsCompanion(PermaJob primary, PermaJob next)
    {
        bool type = Companion(primary.Type) == next.Type || primary.Type == TaskType.Bodyguard && next.Type == TaskType.Unnamed289;
        return type && next with { Type = primary.Type } == primary;
    }

    /// <summary>The list cut into units: a job with its companion right after it, every other row alone.</summary>
    public static List<JobUnit> Units(IReadOnlyList<PermaJob> jobs)
    {
        var units = new List<JobUnit>();
        int row = 0;
        while (row < jobs.Count)
        {
            int length = row + 1 < jobs.Count && IsCompanion(jobs[row], jobs[row + 1]) ? 2 : 1;
            units.Add(new(row, length));
            row += length;
        }

        return units;
    }

    /// <summary>The unit holding <paramref name="row"/>.</summary>
    public static int UnitOf(List<JobUnit> units, int row) => units.FindIndex(u => u.Contains(row));

    /// <summary>
    /// The engine's same-target test of PermaJobs.Add: the same character when either follows one, else the same site, else the same handle.
    /// </summary>
    public static bool SameTarget(PermaJob a, PermaJob b) =>
        !a.Character.IsNone || !b.Character.IsNone
            ? a.Character == b.Character
            : a.Site is not null || b.Site is not null ? a.Site == b.Site : a.Saved == b.Saved;

    /// <summary>
    /// Whether a list refuses another <paramref name="job"/>: one of its type is there (on the same target when the type allows several).
    /// </summary>
    public static bool Refuses(IReadOnlyList<PermaJob> jobs, PermaJob job) =>
        jobs.Any(j => j.Type == job.Type && (!PermaJobs.PerTarget(job.Type) || SameTarget(j, job)));

    /// <summary>The jobs an animal takes (PermaJobs.Give): attacking enemies, bodyguard, staying close.</summary>
    public static bool AnimalTakes(TaskType type) => type is TaskType.AttackEnemies or TaskType.Bodyguard or TaskType.StayCloseToTarget;

    /// <summary>A row's text: the engine's job name, else the type's name.</summary>
    public static string Name(TaskType type) => PermaJobs.Text(type) ?? type.ToString();

    /// <summary>Whether <paramref name="snapshot"/> is an animal (published without a body).</summary>
    public static bool IsAnimal(CharacterSnapshot snapshot) => snapshot.Body is null;
}
