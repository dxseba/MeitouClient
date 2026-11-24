using Meitou.Simulation;
using Meitou.Simulation.Ai;
using static Meitou.ShopCounterRestock.ShopCounterRestockMod;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The trips in the sweep: a worker at its trip's place loads (at a storage) or delivers (at the counter) up to
/// <see cref="Rules.UnitsPerStep"/> units per sweep; a trip ends when nothing is left to move, when another task takes the worker (the assignment
/// stays and restocking resumes once it is idle), or after <see cref="Rules.TripSeconds"/>: a storage then cools down, an unreachable counter
/// pauses restocking.
/// </summary>
internal sealed partial class RestockSystem
{
    /// <summary>
    /// The player building with storage at <paramref name="id"/> (a counter or a storage), null when gone.
    /// </summary>
    internal static Place? PlaceAt(World world, string id) =>
        world.Buildings.Find(id) is { Complete: true, Destroyed: false } site && world.Machines.Find(id) is { Storage: not null } machine
            ? new Place(site, machine)
            : null;

    internal Place? PlaceOf(World world, Trip trip) => PlaceAt(world, trip.Target);

    void StepTrips(World world)
    {
        var table = world.Characters;
        foreach (var (worker, trip) in _trips.OrderBy(t => t.Key.Slot).ToList())
        {
            if (!table.TryResolveNext(worker, out int slot) || table.Cold(slot) is not { } cold || !_assigned.TryGetValue(worker, out var counter))
            {
                ClearTrip(worker);
                continue;
            }

            Step(world, slot, cold, worker, counter, trip);
        }
    }

    void Step(World world, int slot, CharacterCold cold, CharacterId worker, string counter, Trip trip)
    {
        if (cold.Running is not { Row: JobType })
        {
            ClearTrip(worker);
            return;
        }

        if (PlaceOf(world, trip) is { } place && RestockJob.At(in world.Characters.Next[slot], place))
        {
            Transfer(world, cold, worker, counter, trip, place);
            return;
        }

        if (world.Tick - trip.Tick > Rules.TripSeconds / world.TickSeconds) TimedOut(world, slot, worker, counter, trip);
    }

    void Transfer(World world, CharacterCold cold, CharacterId worker, string counter, Trip trip, Place place)
    {
        int moved = 0;
        if (Rules.Counter(world, counter) is { } shop && FindConfig(counter) is { } config)
        {
            var shortfalls = Planner.Shortfalls(this, shop, config);
            moved = trip.Fetch ? Load(cold, trip, place, shortfalls) : Deliver(cold, shop, shortfalls);
        }

        if (moved > 0) return;

        ClearTrip(worker);
        DecisionSystem.Request(cold);
    }

    /// <summary>
    /// Takes the trip's item from the storage, up to the counter's shortfall less what the worker already carries.
    /// </summary>
    static int Load(CharacterCold cold, Trip trip, Place storage, List<(string Item, int Missing)> shortfalls)
    {
        int missing = shortfalls.Find(s => s.Item == trip.Item).Missing - Cargo.Carried(cold, trip.Item);
        return missing > 0 ? Cargo.Load(storage.Storage, cold, trip.Item, Math.Min(missing, Rules.UnitsPerStep)) : 0;
    }

    /// <summary>
    /// Puts the worker's goods of the short items into the counter, in whitelist order, up to each shortfall.
    /// </summary>
    static int Deliver(CharacterCold cold, Place counter, List<(string Item, int Missing)> shortfalls)
    {
        int moved = 0;
        foreach (var (item, missing) in shortfalls)
        {
            int left = Rules.UnitsPerStep - moved;
            if (left <= 0) break;

            moved += Cargo.Unload(cold, counter.Storage, item, Math.Min(missing, left));
        }

        return moved;
    }

    /// <summary>
    /// A trip past its time: a storage cools down; a counter the worker cannot reach pauses restocking until the player turns it back on.
    /// </summary>
    void TimedOut(World world, int slot, CharacterId worker, string counter, Trip trip)
    {
        ClearTrip(worker);
        StopOwnTask(world, slot);
        if (trip.Fetch)
        {
            CoolDown(world, trip.Target);
            return;
        }

        var config = ConfigOf(counter);
        config.Enabled = false;
        config.Status = "Paused: counter unreachable. Check doors and resume.";
    }
}
