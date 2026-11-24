using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Movement;
using static Meitou.WorkForCats.WorkForCatsMod;

namespace Meitou.WorkForCats;

/// <summary>
/// The mod job as the decision step's contributor and the act-out of its row, as the game's obedient slaves and town workers work a building: a
/// walk to within the functionality's <c>use range</c> of the workplace (<see cref="CharacterTask.GoTo"/>), then standing there facing it at its
/// work clip (<see cref="CharacterTask.Idle"/> with <c>ActionClip</c> and <c>ActionWork</c>). The job runs only while the jobs switch is on, and
/// after the order queue, so a player's order or Jobs off pauses it and the next decision resumes it. The machine itself is not operated: the
/// employee makes no products.
/// </summary>
internal sealed class WorkJob(WorkSystem system) : IJobSource, ITaskHandler
{
    /// <summary>
    /// Walks that end short of the workplace before the job waits.
    /// </summary>
    const int MaxFails = 3;

    /// <summary>
    /// Seconds the job waits after <see cref="MaxFails"/> short walks.
    /// </summary>
    const float RetrySeconds = 60;

    #region The job

    public DecisionCheck CanRun(World world, int slot, int k)
    {
        var cold = world.Characters.Cold(slot)!;
        if (cold.Jobs[k].Site is not { } site) return DecisionCheck.No("no workplace");
        if (cold.Medical is { Incapacitated: true } || !cold.CarriedBy.IsNone || !cold.Carrying.IsNone) return DecisionCheck.No("cannot work now");
        if (system.Eligible(world, site) is not { } place) return DecisionCheck.No("the workplace no longer employs");
        if (system.Waiting(world, slot)) return DecisionCheck.No("could not reach the workplace");
        if (Continues(world, slot, cold, k, place)) return DecisionCheck.Runs;

        // The original's access check before each approach (isFreeSlot): a station whose every slot is held takes no new walk.
        return system.StationFull(site) ? DecisionCheck.No("every station here is taken") : DecisionCheck.Run;
    }

    public void Start(World world, int slot, int k)
    {
        var table = world.Characters;
        var cold = table.Cold(slot)!;
        if (cold.Jobs[k].Site is not { } site || system.Eligible(world, site) is not { } place) return;

        var employee = system.EmployeeOf(world, slot, site);
        if (AtWork(in table.Next[slot], place))
        {
            employee.Fails = 0;
            Begin(world, slot, k, place, CharacterTask.Idle);
            return;
        }

        if (WalkedShort(world, slot, cold) && ++employee.Fails >= MaxFails)
        {
            employee.Fails = 0;
            employee.RetryTick = world.Tick + (long)(RetrySeconds / world.TickSeconds);
            world.Post($"{cold.Name} cannot reach {place.Name}.");
            return;
        }

        Begin(world, slot, k, place, CharacterTask.GoTo);
    }

    /// <summary>
    /// The job's task runs and nothing on the list can (Jobs off, the workplace gone): the task ends.
    /// </summary>
    public void Stop(World world, int slot, int k) => TaskStart.End(world, slot, TaskEnd.Stopped);

    /// <summary>
    /// The character's task is this job's: walking while short of the workplace, or standing at it at the work clip.
    /// </summary>
    static bool Continues(World world, int slot, CharacterCold cold, int k, Workplace place)
    {
        ref var n = ref world.Characters.Next[slot];
        var task = (CharacterTask)n.Task;
        if (cold.Running is not { Source: TaskSource.Job, Implicit: false, Row: JobType } run || run.Job != k || !run.Runs(task)) return false;

        return task == CharacterTask.GoTo ? !AtWork(in n, place) : cold.ActionClip == place.Clip;
    }

    /// <summary>
    /// The last walk of the job ended where the character stands, short of the workplace.
    /// </summary>
    static bool WalkedShort(World world, int slot, CharacterCold cold) =>
        world.Characters.Next[slot].Task == (byte)CharacterTask.Idle && cold.Running is { Row: JobType, Task: CharacterTask.GoTo };

    static void Begin(World world, int slot, int k, Workplace place, CharacterTask task)
    {
        var request = new TaskRequest(task, JobType, TaskSource.Job) { Job = k, Site = place.Key };
        if (task == CharacterTask.GoTo) request = request with { Goal = new Vector2(place.Position.X, place.Position.Z) };

        TaskStart.Start(world, slot, request);
    }

    /// <summary>
    /// At the workplace within its reach, or twice that once stopped, as the game's arrival rule for workers (docs/simulation.md "Work").
    /// </summary>
    internal static bool AtWork(in CharacterHot n, Workplace place)
    {
        float dx = n.Position.X - place.Position.X, dz = n.Position.Z - place.Position.Z;
        bool stopped = n.Velocity.X * n.Velocity.X + n.Velocity.Z * n.Velocity.Z <= 1;
        float reach = stopped ? 2 * place.Range : place.Range;
        return dx * dx + dz * dz <= reach * reach;
    }

    #endregion

    #region The act-out

    void ITaskHandler.Start(World world, int slot, in TaskRequest request)
    {
        var table = world.Characters;
        var cold = table.Cold(slot)!;
        var place = system.Eligible(world, request.Site);
        if (request.Task == CharacterTask.GoTo)
        {
            MovementSystem.GoTo(table, slot, request.Goal!.Value, request.Mode ?? cold.RunSpeed);
            cold.ArriveWithin = place?.Range ?? 0;
            return;
        }

        MovementSystem.Stop(table, slot);
        if (place is not null) Stand(table, slot, cold, place);
    }

    /// <summary>
    /// Faces the workplace and plays its work clip, with the functionality's animation events.
    /// </summary>
    static void Stand(CharacterTable table, int slot, CharacterCold cold, Workplace place)
    {
        ref var n = ref table.Next[slot];
        float dx = place.Position.X - n.Position.X, dz = place.Position.Z - n.Position.Z;
        if (dx * dx + dz * dz > 1e-4f) n.Yaw = MathF.Atan2(dx, dz);

        cold.ActionClip = place.Clip;
        cold.ActionWork = place.Functionality is { } functionality ? new WorkEvents(functionality, place.Clip) : null;
    }

    /// <summary>
    /// Another task takes the employee, or the job stops: the work clip goes.
    /// </summary>
    void ITaskHandler.End(World world, int slot, CharacterTask task, TaskEnd reason)
    {
        if (task != CharacterTask.Idle) return;

        var cold = world.Characters.Cold(slot)!;
        cold.ActionClip = null;
        cold.ActionWork = null;
    }

    #endregion
}
