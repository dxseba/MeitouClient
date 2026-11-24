using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Production;
using static Meitou.ShopCounterRestock.ShopCounterRestockMod;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The assignment sweep: the job list is the truth (the counter menu's Restock row, the window and the Jobs panel's X all change it), and the
/// sweep keeps one worker per counter, newest first, and the job last in each worker's list (the original's entry sits after the normal jobs).
/// </summary>
internal sealed partial class RestockSystem
{
    readonly record struct OnJob(int Slot, CharacterId Id, CharacterCold Cold, string Counter);

    /// <summary>
    /// Whether the sweep has run in this world: the first one (after a load) only notes the saved jobs' workers.
    /// </summary>
    bool _settled;

    void Sweep(World world)
    {
        var onJob = Workers(world);
        Forget(onJob);
        if (!_settled)
        {
            foreach (var w in onJob) _assigned.TryAdd(w.Id, w.Counter);
            _settled = true;
        }

        foreach (var group in onJob.GroupBy(w => w.Counter))
        {
            var workers = group.ToList();
            var keep = workers.FirstOrDefault(w => _assigned.GetValueOrDefault(w.Id) != w.Counter, workers[0]);
            foreach (var other in workers.Where(w => w.Id != keep.Id)) Unassign(world, other.Id);

            Take(keep);
            KeepLast(world, keep);
        }
    }

    static List<OnJob> Workers(World world)
    {
        var table = world.Characters;
        var onJob = new List<OnJob>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold || JobCounter(cold) is not { } counter) continue;

            onJob.Add(new OnJob(slot, table.IdOf(slot), cold, counter));
        }

        return onJob;
    }

    /// <summary>
    /// Workers no longer on the job (its X on the Jobs panel, death, leaving the player): the counter keeps its whitelist and targets.
    /// </summary>
    void Forget(List<OnJob> onJob)
    {
        foreach (var (worker, counter) in _assigned.OrderBy(a => a.Key.Slot).ToList())
        {
            if (onJob.Any(w => w.Id == worker)) continue;

            _assigned.Remove(worker);
            ClearTrip(worker);
            ConfigOf(counter).Status = "Restock assignment removed from Jobs panel.";
        }
    }

    /// <summary>
    /// Notes the worker on its counter; a new assignment from the counter's menu row turns restocking on (the original's Shift + right-click).
    /// </summary>
    void Take(OnJob worker)
    {
        if (_assigned.TryGetValue(worker.Id, out var had) && had == worker.Counter) return;

        if (had is not null)
        {
            ConfigOf(had).Status = "Worker reassigned to another counter.";
            ClearTrip(worker.Id);
        }

        _assigned[worker.Id] = worker.Counter;
        var config = ConfigOf(worker.Counter);
        config.Enabled = true;
        config.Status = config.Items.Count == 0 ? "Restock assigned. Add items to the whitelist." : "Restock assigned to " + worker.Cold.Name;
    }

    /// <summary>
    /// Moves the job to the end of the worker's list, unless its task runs now (the list's indices name the running job).
    /// </summary>
    static void KeepLast(World world, OnJob worker)
    {
        var jobs = worker.Cold.Jobs;
        int k = jobs.FindIndex(j => j.Type == JobType);
        if (k < 0 || k == jobs.Count - 1 || worker.Cold.Running is { Source: TaskSource.Job }) return;

        var job = jobs[k];
        jobs.RemoveAt(k);
        jobs.Add(job);
        DecisionSystem.Request(worker.Cold);
    }
}
