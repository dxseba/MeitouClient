using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Movement;
using static Meitou.ShopCounterRestock.ShopCounterRestockMod;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The restock job as the decision step's contributor and the act-out of its row: a walk (<see cref="CharacterTask.GoTo"/>) to the storage or
/// the counter the planner picks, to within its reach. It runs only while the worker's Jobs switch is on and after its orders and other jobs, so
/// another task pauses it and the next decision resumes it; the loads and deliveries happen in the system's sweep once the worker is there.
/// </summary>
internal sealed class RestockJob(RestockSystem system) : IJobSource, ITaskHandler
{
    public DecisionCheck CanRun(World world, int slot, int k)
    {
        var cold = world.Characters.Cold(slot)!;
        if (cold.Jobs[k].Site is not { } counter || system.FindConfig(counter) is not { } config) return DecisionCheck.No("no counter");
        if (!config.Enabled) return DecisionCheck.No("Restock disabled.");
        if (cold.Medical is { Incapacitated: true } || cold.InCombat || !cold.CarriedBy.IsNone || !cold.Carrying.IsNone)
            return DecisionCheck.No("Waiting: worker unavailable or in combat.");
        if (Walking(world, slot, cold, k)) return DecisionCheck.Runs;

        var step = Planner.Next(world, system, slot, counter);
        return step.Place is null ? DecisionCheck.No(step.Why) : DecisionCheck.Run;
    }

    public void Start(World world, int slot, int k)
    {
        var table = world.Characters;
        var cold = table.Cold(slot)!;
        if (cold.Jobs[k].Site is not { } counter || Planner.Next(world, system, slot, counter) is not { Place: { } place } step) return;

        var id = table.IdOf(slot);
        if (system.TripOf(id) is not { } trip || trip.Target != place.Id || trip.Fetch != step.Fetch || trip.Item != step.Item)
            system.SetTrip(id, new Trip(step.Fetch, place.Id, step.Item, world.Tick));

        var request = new TaskRequest(CharacterTask.GoTo, JobType, TaskSource.Job)
        {
            Job = k,
            Site = place.Id,
            Goal = new Vector2(place.Position.X, place.Position.Z),
        };
        TaskStart.Start(world, slot, request);
    }

    public void Stop(World world, int slot, int k) => TaskStart.End(world, slot, TaskEnd.Stopped);

    /// <summary>
    /// The worker is on this job's trip now: walking it, or standing at its place while the sweep loads or unloads.
    /// </summary>
    bool Walking(World world, int slot, CharacterCold cold, int k)
    {
        ref var n = ref world.Characters.Next[slot];
        var task = (CharacterTask)n.Task;
        if (cold.Running is not { Source: TaskSource.Job, Implicit: false, Row: JobType } run || run.Job != k) return false;
        if (system.TripOf(world.Characters.IdOf(slot)) is not { } trip) return false;
        if (task == CharacterTask.GoTo) return run.Runs(task);

        return task == CharacterTask.Idle && system.PlaceOf(world, trip) is { } place && At(in n, place);
    }

    /// <summary>
    /// At the place within its reach (twice that once stopped, as the game's workers arrive) and on its floor.
    /// </summary>
    internal static bool At(in CharacterHot n, Place place)
    {
        float dx = n.Position.X - place.Position.X, dz = n.Position.Z - place.Position.Z;
        bool stopped = n.Velocity.X * n.Velocity.X + n.Velocity.Z * n.Velocity.Z <= 1;
        float reach = stopped ? 2 * place.Reach : place.Reach;
        return dx * dx + dz * dz <= reach * reach && MathF.Abs(n.Position.Y - place.Position.Y) <= Rules.FloorHeight;
    }

    void ITaskHandler.Start(World world, int slot, in TaskRequest request)
    {
        var table = world.Characters;
        var cold = table.Cold(slot)!;
        var place = request.Site is { } id ? RestockSystem.PlaceAt(world, id) : null;
        MovementSystem.GoTo(table, slot, request.Goal!.Value, request.Mode ?? cold.RunSpeed);
        cold.ArriveWithin = place?.Reach ?? Rules.MinReach;
    }

    void ITaskHandler.End(World world, int slot, CharacterTask task, TaskEnd reason) { }
}
