using System.Collections.Concurrent;
using Meitou.Simulation;
using Meitou.StackSort.Search;

namespace Meitou.StackSort;

/// <summary>
/// The search's results per grid, goal and budget (a pure function of them, so a cached or background-computed result is the one a press would
/// compute), and the background precompute for the open windows (StackSort.md "Precompute"): once an open window's grid has stayed unchanged
/// for <c>mutation_debounce_ms</c>, at most <c>resnapshots_per_frame</c> grids per step are copied (merged first on a copy with Merge stacks)
/// and swept on a thread pool of <c>num_workers</c>. A press waits for a sweep still running for its grid, so when it ran never changes a layout.
/// </summary>
static class Precompute
{
    const int CacheSize = 32;

    /// <summary>
    /// The open windows' grids, by character and bag, with the host's clock when the strip was last built (host thread writes; the simulation
    /// only reads it to choose what to precompute, never for a layout).
    /// </summary>
    public static readonly ConcurrentDictionary<(CharacterId Character, bool Bag), long> Open = new();

    static readonly Lock Gate = new();
    static readonly Dictionary<string, Lazy<SearchResult?[]>> Cache = [];
    static readonly Queue<string> Age = new();
    static readonly HashSet<string> Started = [];
    static SemaphoreSlim s_workers = new(4);
    static int s_workerCount = 4;

    /// <summary>
    /// The watched grids (simulation thread): their signature, the steps it has held, and whether its sweeps were started.
    /// </summary>
    static readonly Dictionary<(CharacterId, bool), (string Signature, int Steps, bool Done)> Watched = [];

    /// <summary>
    /// The sweep of <paramref name="problem"/>: from the cache, waiting for a running one, or computed now.
    /// </summary>
    public static SearchResult?[] Sweep(PackProblem problem, bool width, SweepBudget budget) => Entry(problem, width, budget).Value;

    /// <summary>
    /// Starts the sweep of <paramref name="problem"/> on the worker pool unless it is cached or running; true when it started one.
    /// </summary>
    public static bool Start(PackProblem problem, bool width, SweepBudget budget)
    {
        string key = Key(problem, width, budget);
        var entry = Entry(problem, width, budget);
        lock (Gate)
        {
            if (entry.IsValueCreated || !Started.Add(key)) return false;
        }

        var workers = Workers(budget.Workers);
        Task.Run(() => Run(entry, workers));
        return true;
    }

    static void Run(Lazy<SearchResult?[]> entry, SemaphoreSlim workers)
    {
        workers.Wait();
        try
        {
            _ = entry.Value;
        }
        catch (Exception)
        {
            // The entry keeps the exception; the press that asks for it logs it and falls back to the heuristic.
        }
        finally
        {
            workers.Release();
        }
    }

    static SemaphoreSlim Workers(int count)
    {
        lock (Gate)
        {
            if (count != s_workerCount) (s_workers, s_workerCount) = (new SemaphoreSlim(count), count);
            return s_workers;
        }
    }

    static string Key(PackProblem problem, bool width, SweepBudget budget) => $"{(width ? 'W' : 'H')}|{budget.Key()}|{problem.Key()}";

    static Lazy<SearchResult?[]> Entry(PackProblem problem, bool width, SweepBudget budget)
    {
        string key = Key(problem, width, budget);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var entry)) return entry;

            entry = new(() => TargetSweep.Run(problem, width, budget), LazyThreadSafetyMode.ExecutionAndPublication);
            Cache[key] = entry;
            Age.Enqueue(key);
            while (Age.Count > CacheSize)
            {
                string old = Age.Dequeue();
                Cache.Remove(old);
                Started.Remove(old);
            }
            return entry;
        }
    }

    /// <summary>
    /// One simulation step: forgets windows not drawn for two seconds, and starts the sweeps of open windows' grids that have stayed unchanged
    /// long enough (a read of the inventories; they are copied before anything is merged).
    /// </summary>
    public static void Step(World world, ItemFacts facts)
    {
        var original = StackSortMod.Original;
        int debounce = original.DebounceMs <= 0 ? 0 : (int)Math.Ceiling(original.DebounceMs / 1000.0 / Math.Max(world.TickSeconds, 1e-3f));
        int left = original.ResnapshotsPerStep;
        long now = Environment.TickCount64;
        foreach (var (key, seen) in Open.OrderBy(o => o.Key.Character.Slot).ThenBy(o => o.Key.Bag))
        {
            if (now - seen > 2000)
            {
                Open.TryRemove(key, out _);
                Watched.Remove(key);
                continue;
            }

            if (Watch(world, facts, key, debounce, left > 0)) left--;
        }
    }

    /// <summary>
    /// Watches one window's grid; true when it started a sweep. Only a grid whose signature held for the debounce is copied and searched.
    /// </summary>
    static bool Watch(World world, ItemFacts facts, (CharacterId Character, bool Bag) key, int debounce, bool may)
    {
        if (LayoutCycle.Signature(world, key.Character, key.Bag) is not { } signature) return false;

        var (last, steps, done) = Watched.GetValueOrDefault(key);
        if (last != signature)
        {
            Watched[key] = (signature, 0, false);
            return false;
        }

        Watched[key] = (signature, steps + 1, done);
        if (done || !may || steps + 1 < debounce || LayoutCycle.Problems(world, key.Character, key.Bag, facts) is not { } problems) return false;

        Watched[key] = (signature, steps + 1, true);
        var budget = StackSortMod.Original.Budget;
        bool width = LayoutCycle.Current == Goal.Width;
        return problems.Aggregate(false, (started, p) => Start(p, width, budget) | started);
    }
}
