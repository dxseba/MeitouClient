using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Player;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// A whitelist line for the window: the item, its name, target and the counter's units now.
/// </summary>
internal sealed record ItemView(string Id, string Name, int Target, int Stock);

/// <summary>
/// One counter for the window: its name, worker (none: unassigned), switch, status line and whitelist.
/// </summary>
internal sealed record CounterView(string Name, CharacterId Worker, string? WorkerName, bool Enabled, string Status, IReadOnlyList<ItemView> Items);

/// <summary>
/// A person of the player's who may be assigned.
/// </summary>
internal sealed record WorkerView(CharacterId Id, string Name);

/// <summary>
/// What the window shows, published by the sweep (immutable; read on the interface's thread).
/// </summary>
internal sealed record RestockView(IReadOnlyDictionary<string, CounterView> Counters, IReadOnlyList<WorkerView> Workers);

internal sealed partial class RestockSystem
{
    volatile RestockView? _view;

    internal RestockView? View => _view;

    void Publish(World world)
    {
        var counters = new Dictionary<string, CounterView>(StringComparer.Ordinal);
        foreach (var site in world.Buildings.All)
        {
            if (Rules.Counter(world, site.Id) is { } counter) counters[site.Id] = ViewOf(world, counter);
        }

        _view = new RestockView(counters, People(world));
    }

    CounterView ViewOf(World world, Place counter)
    {
        var config = FindConfig(counter.Id);
        var items = config?.Items.Select(i => new ItemView(i.Item, Catalog.NameOf(i.Item), i.Target, Cargo.InStorage(counter.Storage, i.Item)))
            .ToList() ?? [];
        var worker = WorkerOf(counter.Id) ?? CharacterId.None;
        string? workerName = worker.IsNone ? null : world.Characters.NameOf(worker);
        string counterName = counter.Site.Name.Length > 0 ? counter.Site.Name : counter.Machine.Data.Name;

        return new CounterView(counterName, worker, workerName, config?.Enabled == true, StatusOf(world, counter.Id, config, worker), items);
    }

    /// <summary>
    /// The live status while restocking is on and a worker holds the counter (the original's scheduler texts), else the last command's.
    /// </summary>
    string StatusOf(World world, string counter, CounterConfig? config, CharacterId worker)
    {
        if (config is not { Enabled: true } || worker.IsNone) return config?.Status ?? "";

        var table = world.Characters;
        if (!table.TryResolveNext(worker, out int slot) || table.Cold(slot) is not { } cold)
            return "Waiting: assigned worker is unloaded or unavailable.";
        if ((cold.Orders & CharacterOrders.Jobs) == 0) return "Waiting: enable worker Jobs.";
        if (cold.Medical is { Incapacitated: true } || cold.InCombat) return "Waiting: worker unavailable or in combat.";
        if (TripOf(worker) is { } trip) return (trip.Fetch ? "Fetching " : "Delivering ") + Catalog.NameOf(trip.Item);

        var step = Planner.Next(world, this, slot, counter);
        return step.Why ?? (step.Fetch ? "Fetching " : "Delivering ") + Catalog.NameOf(step.Item);
    }

    static List<WorkerView> People(World world)
    {
        var table = world.Characters;
        var people = new List<WorkerView>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Medical.Dead: false } cold || SquadRegistry.IsAnimal(cold))
                continue;

            people.Add(new WorkerView(table.IdOf(slot), cold.Name));
        }

        return people;
    }
}
