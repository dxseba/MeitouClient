using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Movement;
using static Meitou.ShopCounterRestock.ShopCounterRestockMod;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// A worker's current trip: to fetch <see cref="Item"/> at a storage or to deliver at the counter, its target and when it began.
/// </summary>
internal sealed record Trip(bool Fetch, string Target, string Item, long Tick);

/// <summary>
/// The restock in the tick: the window's commands in <see cref="Inputs"/>; every <see cref="Cadence"/> ticks (0.5 s, the original's update
/// interval) the assignment sweep (one worker per counter, one counter per worker), the trips' arrivals, loads and deliveries, timeouts, and the
/// window's view. The worker's walks are the mod job's (<see cref="RestockJob"/>). Counters' settings are saved and hashed; trips and cooldowns
/// are hashed and reset on load, as in the original.
/// </summary>
internal sealed partial class RestockSystem(ItemCatalog catalog) : ITickSystem, IChooserOwner, ITaskOwner, IStateHashed, IModSaveData
{
    internal const int Cadence = 15;

    readonly SortedDictionary<string, CounterConfig> _counters = new(StringComparer.Ordinal);
    readonly Dictionary<CharacterId, string> _assigned = [];
    readonly Dictionary<CharacterId, Trip> _trips = [];
    readonly SortedDictionary<string, long> _sourceCooldown = new(StringComparer.Ordinal);
    RestockJob? _job;

    RestockJob Job => _job ??= new RestockJob(this);

    internal ItemCatalog Catalog => catalog;

    public void Register(ChooserRegistry chooser) => chooser.Jobs(JobType, Job);

    public void RegisterTasks(TaskHandlers tasks) => tasks.Register(JobType, Job);

    public void SlowWorld(World world)
    {
        if (world.Tick % Cadence != 0) return;

        Sweep(world);
        StepTrips(world);
        Publish(world);
    }

    /// <summary>
    /// The counter's settings, made when missing.
    /// </summary>
    internal CounterConfig ConfigOf(string counter) =>
        _counters.TryGetValue(counter, out var config) ? config : _counters[counter] = new CounterConfig();

    internal CounterConfig? FindConfig(string? counter) => counter is not null && _counters.TryGetValue(counter, out var c) ? c : null;

    internal Trip? TripOf(CharacterId worker) => _trips.GetValueOrDefault(worker);

    internal void SetTrip(CharacterId worker, Trip trip) => _trips[worker] = trip;

    internal void ClearTrip(CharacterId worker) => _trips.Remove(worker);

    internal bool Cooling(World world, string storage) => _sourceCooldown.TryGetValue(storage, out long until) && world.Tick < until;

    internal void CoolDown(World world, string storage) =>
        _sourceCooldown[storage] = world.Tick + (long)(Rules.SourceCooldownSeconds / world.TickSeconds);

    /// <summary>
    /// The counter of the character's restock job, null when it has none.
    /// </summary>
    internal static string? JobCounter(CharacterCold cold)
    {
        foreach (var job in cold.Jobs)
        {
            if (job.Type == JobType) return job.Site;
        }

        return null;
    }

    /// <summary>
    /// Ends the character's task when it is this job's (a job taken off, a switch turned off), so the next choice runs something else.
    /// </summary>
    internal static void StopOwnTask(World world, int slot)
    {
        var cold = world.Characters.Cold(slot)!;
        var task = (CharacterTask)world.Characters.Next[slot].Task;
        if (cold.Running is { Row: JobType, Implicit: false } run && run.Runs(task)) TaskStart.End(world, slot, TaskEnd.Stopped);
    }
}
