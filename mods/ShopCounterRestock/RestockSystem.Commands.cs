using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Production;
using static Meitou.ShopCounterRestock.ShopCounterRestockMod;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The settings window's commands, applied in the serial <see cref="Inputs"/> step; each sets the counter's status line to the original's text.
/// </summary>
internal sealed partial class RestockSystem
{
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case AssignWorker assign:
                    Assign(world, assign);
                    break;
                case SetTarget set:
                    Target(set);
                    break;
                case RemoveItem remove:
                    ConfigOf(remove.Counter).Remove(remove.Item);
                    ConfigOf(remove.Counter).Status = "Item removed. Carried goods remain with the worker.";
                    break;
                case SetEnabled enable:
                    Enable(world, enable);
                    break;
            }
        }
    }

    void Target(SetTarget set)
    {
        var config = ConfigOf(set.Counter);
        if (set.Target is < 1 or > CounterConfig.MaxTarget)
        {
            config.Status = "Enter a target quantity from 1 to 10000.";
            return;
        }

        config.Set(set.Item, set.Target);
        config.Status = "Whitelist updated. Targets count individual units.";
    }

    void Enable(World world, SetEnabled enable)
    {
        var config = ConfigOf(enable.Counter);
        if (!enable.On)
        {
            config.Enabled = false;
            config.Status = "Restock disabled.";
            if (WorkerOf(enable.Counter) is { } worker && world.Characters.TryResolveNext(worker, out int slot)) StopOwnTask(world, slot);
            return;
        }

        if (WorkerOf(enable.Counter) is null || config.Items.Count == 0)
        {
            config.Status = "Assign a worker and whitelist at least one item first.";
            return;
        }

        config.Enabled = true;
        config.Status = "Restock enabled. Worker Jobs must be on.";
    }

    /// <summary>
    /// The window's worker choice: the counter's current worker loses the job, the new one (a living person of the player's) gets it last in its
    /// list, leaving the counter it had; its Jobs switch is left as the player set it.
    /// </summary>
    void Assign(World world, AssignWorker assign)
    {
        var config = ConfigOf(assign.Counter);
        if (WorkerOf(assign.Counter) is { } old && old != assign.Worker) Unassign(world, old);
        if (assign.Worker.IsNone)
        {
            config.Status = "Select a worker to assign shop counter restocking.";
            return;
        }

        var table = world.Characters;
        if (Rules.Counter(world, assign.Counter) is null || !table.TryResolveNext(assign.Worker, out int slot)
            || table.Cold(slot) is not { IsPlayer: true, Medical.Dead: false } cold || SquadRegistry.IsAnimal(cold))
            return;

        Give(world, slot, cold, assign.Counter);
        config.Status = config.Items.Count == 0
            ? "Restock assigned. Add items to the whitelist."
            : "Worker assignment updated. Enable restock when ready.";
        world.Post($"Restock assigned to {cold.Name}.");
    }

    void Give(World world, int slot, CharacterCold cold, string counter)
    {
        if (JobCounter(cold) is { } had && had != counter) ConfigOf(had).Status = "Worker reassigned to another counter.";

        StopOwnTask(world, slot);
        cold.Jobs.RemoveAll(j => j.Type == JobType);
        cold.Jobs.Add(new PermaJob(JobType, counter));
        _assigned[world.Characters.IdOf(slot)] = counter;
        DecisionSystem.Request(cold);
    }

    void Unassign(World world, CharacterId worker)
    {
        _assigned.Remove(worker);
        ClearTrip(worker);
        if (!world.Characters.TryResolveNext(worker, out int slot) || world.Characters.Cold(slot) is not { } cold) return;

        StopOwnTask(world, slot);
        cold.Jobs.RemoveAll(j => j.Type == JobType);
        DecisionSystem.Request(cold);
    }

    /// <summary>
    /// The worker the sweep last saw on the counter's job.
    /// </summary>
    internal CharacterId? WorkerOf(string counter)
    {
        foreach (var (worker, site) in _assigned.OrderBy(a => a.Key.Slot))
        {
            if (site == counter) return worker;
        }

        return null;
    }
}
