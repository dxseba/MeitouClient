using Meitou.StackSort.Search;

namespace Meitou.StackSort;

/// <summary>
/// The layouts a sort offers for some grids and a goal: the original's search (<see cref="TargetSweep"/>, through <see cref="Precompute"/>),
/// one layout per ranked target, or this port's heuristic when the search finds no target complete in every grid or cannot search a grid.
/// </summary>
static class LayoutSearch
{
    /// <summary>
    /// Grids the synchronous budget still searches: at most 400 cells and 80 items (larger ones, such as a big trade stock, take the heuristic
    /// rather than stall the step).
    /// </summary>
    const int SyncCells = 400, SyncItems = 80;

    /// <summary>
    /// Up to <paramref name="top"/> layouts, best first, one entry per grid in each; empty when nothing fits.
    /// </summary>
    public static List<PackLayout[]> Layouts(List<Packer.Grid> grids, ItemFacts facts, Goal goal, int top, SweepBudget budget, bool sync = false)
    {
        var problems = grids.Select(g => Packer.Problem(g, facts)).ToList();
        if (problems.All(p => Searches(p, sync)) && Searched(problems, goal, top, budget) is { Count: > 0 } found) return found;

        StackSortMod.Log(2, () => "search: no complete target, the heuristic's layouts");
        return Heuristic(problems, goal, top);
    }

    static bool Searches(PackProblem p, bool sync) => p.Searchable && (!sync || p.Width * p.Height <= SyncCells && p.Items.Length <= SyncItems);

    static List<PackLayout[]>? Searched(List<PackProblem> problems, Goal goal, int top, SweepBudget budget)
    {
        try
        {
            var sweeps = problems.Select(p => Precompute.Sweep(p, goal == Goal.Width, budget)).ToList();
            var targets = TargetSweep.Rank(sweeps, top);
            foreach (int t in targets) StackSortMod.Log(2, () => $"target {t}: score {sweeps.Sum(s => s[t]!.Score)}, {Describe(sweeps[0][t]!)}");
            return [.. targets.Select(t => sweeps.Select(s => s[t]!.Layout).ToArray())];
        }
        catch (Exception e)
        {
            StackSortMod.Log(0, () => $"search failed ({e.Message}); the heuristic's layouts");
            return null;
        }
    }

    static string Describe(SearchResult r) =>
        $"empty rectangle {r.Metrics.RectWidth}x{r.Metrics.RectHeight}={r.Metrics.RectArea}, concentration {r.Metrics.Concentration:0.00}, "
        + $"stranded {r.Metrics.Stranded}, grouping {r.Metrics.Grouping}";

    static List<PackLayout[]> Heuristic(List<PackProblem> problems, Goal goal, int top)
    {
        var each = problems.Select(p => Search.Heuristic.Layouts(p, goal == Goal.Width, top)).ToList();
        int count = each.Count == 0 ? 0 : each.Min(l => l.Count);
        return [.. Enumerable.Range(0, count).Select(k => each.Select(l => l[k]).ToArray())];
    }
}
