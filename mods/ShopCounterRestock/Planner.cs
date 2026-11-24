using System.Numerics;
using Meitou.Simulation;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The next step of a worker's restock: deliver at the counter, or fetch <see cref="Item"/> at the storage <see cref="Place"/>; or, with no
/// step, why (the original's status text).
/// </summary>
internal readonly record struct Step(Place? Place, bool Fetch, string Item, string? Why)
{
    public static Step Wait(string why) => new(null, false, "", why);
}

/// <summary>
/// Chooses the worker's next step, as the original's scheduler: the whitelist's shortfalls (target minus the counter's units); goods the worker
/// already carries are delivered first; otherwise the first short item it can carry more of is fetched from the nearest storage holding it that
/// is not cooling down after a failed trip. A read of the world: it changes nothing.
/// </summary>
internal static class Planner
{
    public static Step Next(World world, RestockSystem system, int slot, string counterId)
    {
        var cold = world.Characters.Cold(slot)!;
        if (Rules.Counter(world, counterId) is not { } counter) return Step.Wait("Waiting: counter unavailable or no longer eligible.");
        if (system.FindConfig(counterId) is not { Items.Count: > 0 } config) return Step.Wait("Waiting: add items to this counter's whitelist.");

        var shortfalls = Shortfalls(system, counter, config);
        if (shortfalls.Count == 0) return Step.Wait("Targets met: all whitelisted items are stocked.");

        return Deliver(system, cold, counter, shortfalls) ?? Fetch(world, system, slot, cold, counter, shortfalls);
    }

    /// <summary>
    /// The known whitelisted items the counter holds fewer units of than their target, with the units missing, in whitelist order.
    /// </summary>
    public static List<(string Item, int Missing)> Shortfalls(RestockSystem system, Place counter, CounterConfig config)
    {
        var shortfalls = new List<(string, int)>();
        foreach (var (item, target) in config.Items)
        {
            if (!system.Catalog.Knows(item)) continue;

            int missing = target - Cargo.InStorage(counter.Storage, item);
            if (missing > 0) shortfalls.Add((item, missing));
        }

        return shortfalls;
    }

    static Step? Deliver(RestockSystem system, CharacterCold cold, Place counter, List<(string Item, int Missing)> shortfalls)
    {
        string? full = null;
        foreach (var (item, _) in shortfalls)
        {
            if (Cargo.Carried(cold, item) == 0) continue;
            if (Cargo.CounterHasRoom(counter.Storage, cold, item)) return new Step(counter, false, item, null);

            full ??= item;
        }

        return full is null ? null : Step.Wait($"Waiting: counter has no room for {system.Catalog.NameOf(full)}.");
    }

    static Step Fetch(World world, RestockSystem system, int slot, CharacterCold cold, Place counter, List<(string Item, int Missing)> shortfalls)
    {
        var storages = Rules.Storages(world, counter).ToList();
        if (storages.Count == 0) return Step.Wait("Waiting: no usable player storage found in this outpost.");

        string? why = null;
        var at = world.Characters.Next[slot].Position;
        foreach (var (item, missing) in shortfalls)
        {
            if (missing - Cargo.Carried(cold, item) <= 0)
            {
                why ??= $"Waiting: worker already carries the needed {system.Catalog.NameOf(item)}.";
                continue;
            }

            var (step, reason) = Source(world, system, cold, storages, item, at);
            if (step is { } found) return found;

            why ??= reason;
        }

        return Step.Wait(why ?? "Waiting: no usable player storage found in this outpost.");
    }

    static (Step? Step, string Why) Source(World world, RestockSystem system, CharacterCold cold, List<Place> storages, string item, Vector3 at)
    {
        string name = system.Catalog.NameOf(item);
        var holding = storages.Where(s => Cargo.InStorage(s.Storage, item) > 0).ToList();
        if (holding.Count == 0) return (null, $"Waiting: no {name} in storage.");

        var usable = holding.Where(s => !system.Cooling(world, s.Id)).ToList();
        if (usable.Count == 0) return (null, $"Waiting: storage with {name} is blocked or unreachable.");
        if (!Cargo.WorkerHasRoom(cold, usable[0].Storage, item)) return (null, $"Waiting: worker has no cargo space for {name}.");

        var nearest = usable.MinBy(s => Vector3.DistanceSquared(s.Position, at))!;
        return (new Step(nearest, true, item, null), "");
    }
}
