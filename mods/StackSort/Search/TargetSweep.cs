namespace Meitou.StackSort.Search;

/// <summary>
/// The original's background job for one grid and goal, run here as one pure function (StackSort.md "Sweep"): the first pass searches each
/// target 1 .. size - 1 in turn, each warm-started from the previous target's order and told the largest empty rectangle of the
/// complete answers so far that reached its height, a complete layout whose empty rectangle is taller than its target also answering the
/// targets up to that height; then every target whose answer is not already ideal is refined with the larger budget, a complete better answer
/// replacing it, on <see cref="SweepBudget.Workers"/> threads (each target's search is independent, so the result does not depend on them).
/// </summary>
static class TargetSweep
{
    /// <summary>
    /// The answer per target (index = target, entry 0 unused; null: not searched), layouts in <paramref name="problem"/>'s own orientation.
    /// <paramref name="width"/>: the Width goal, searched on the transposed grid.
    /// </summary>
    public static SearchResult?[] Run(PackProblem problem, bool width, SweepBudget budget)
    {
        var p = width ? problem.Transposed() : problem;
        if (!p.Searchable || p.Items.Length == 0 || p.Height < 2) return [];

        var (results, covered) = FirstPass(p, budget.FirstPass);
        if (budget.Refine is { } refine) RefineAll(p, results, covered, budget, refine);
        return width ? [.. results.Select(r => r is null ? null : r with { Layout = r.Layout.Transposed() })] : results;
    }

    static (SearchResult?[] Results, bool[] Covered) FirstPass(PackProblem p, SearchParams first)
    {
        int max = p.Height - 1;
        var results = new SearchResult?[max + 1];
        var covered = new bool[max + 1];
        int[]? warm = null;
        int bestArea = 0, bestHeight = 0;
        for (int t = 1; t <= max; t++)
        {
            var result = Annealer.Run(p, t, first, warm, t <= bestHeight ? bestArea : 0);
            warm = result.Order;
            results[t] = result;
            if (result.Complete)
            {
                bestArea = Math.Max(bestArea, result.Metrics.RectArea);
                bestHeight = Math.Max(bestHeight, result.Metrics.RectHeight);
            }

            int reach = result.Metrics.RectHeight;
            if (!result.Complete || reach <= t || reach > p.Height - 1) continue;

            for (int u = t + 1; u <= Math.Min(reach, max); u++)
            {
                results[u] = result;
                covered[u] = t == 1 && u <= 64;
            }
            t = Math.Min(reach, max);
        }
        return (results, covered);
    }

    /// <summary>
    /// The original refines at most 64 targets of a sweep (the first ones that need it).
    /// </summary>
    const int MaxRefined = 64;

    static void RefineAll(PackProblem p, SearchResult?[] results, bool[] covered, SweepBudget budget, SearchParams refine)
    {
        var targets = Enumerable.Range(1, results.Length - 1)
            .Where(t => results[t] is { } r && !covered[t] && NeedsRefine(p, r, t, budget.RefineItemThreshold))
            .Take(MaxRefined)
            .ToArray();
        var refined = new SearchResult?[results.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, budget.Workers) };
        Parallel.ForEach(targets, options, t => refined[t] = Annealer.Run(p, t, refine, results[t]!.Order));
        foreach (int t in targets)
        {
            if (refined[t]!.Complete && refined[t]!.Score > results[t]!.Score) results[t] = refined[t];
        }
    }

    /// <summary>
    /// Whether a target's first-pass answer gets refined: always, unless it is complete, its free space is one region in effect (concentration
    /// at least 0.95, nothing stranded) and either the target cannot be reached by area or the empty rectangle spans the grid; then only when
    /// the grid holds more than <paramref name="threshold"/> items.
    /// </summary>
    static bool NeedsRefine(PackProblem p, SearchResult r, int target, int threshold)
    {
        if (!r.Complete) return true;

        var m = r.Metrics;
        bool ideal = (p.FreeArea < p.Width * target || p.Width <= m.RectWidth) && m.Concentration >= 0.95 && m.Stranded < 1;
        return !ideal || p.Items.Length > threshold;
    }

    /// <summary>
    /// The click cycle (StackSort.md "Cycle"): the targets answered completely in every grid, by their summed score, best first (ties: smaller
    /// target first), a target scoring the same as the one taken before it skipped, at most <paramref name="top"/> (<c>top_targets</c>).
    /// </summary>
    public static List<int> Rank(IReadOnlyList<SearchResult?[]> grids, int top)
    {
        int max = grids.Count == 0 ? 0 : grids.Min(g => g.Length) - 1;
        var sums = new List<(int Target, long Sum)>();
        for (int t = 1; t <= max; t++)
        {
            if (grids.All(g => g[t] is { Complete: true })) sums.Add((t, grids.Sum(g => g[t]!.Score)));
        }

        var ranked = new List<int>();
        long? last = null;
        foreach (var (t, sum) in sums.OrderByDescending(s => s.Sum).ThenBy(s => s.Target))
        {
            if (ranked.Count >= top) break;
            if (last == sum) continue;

            ranked.Add(t);
            last = sum;
        }
        return ranked;
    }
}
