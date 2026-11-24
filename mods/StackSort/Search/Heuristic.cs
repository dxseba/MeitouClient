namespace Meitou.StackSort.Search;

/// <summary>
/// This port's earlier layout heuristic, kept as the fallback when the search finds no complete layout (and for grids it cannot search):
/// six candidates, items grouped by record and ordered by one of three keys (area, height, width), filled row by row (top first) or column
/// by column (left first), ranked by the rows used (Height) or the columns used (Width). Pure: it lays out a copy of the grid.
/// </summary>
static class Heuristic
{
    static readonly Func<PackItem, long>[] Keys = [i => i.Width * (long)i.Height, i => i.Height, i => i.Width];

    /// <summary>
    /// Up to <paramref name="offered"/> distinct layouts, best first for the goal.
    /// </summary>
    public static List<PackLayout> Layouts(PackProblem p, bool widthGoal, int offered)
    {
        var found = new List<(long Score, PackLayout Layout)>();
        foreach (var key in Keys)
        {
            foreach (bool columns in new[] { false, true })
            {
                if (TryLayout(p, key, columns) is { } layout) found.Add((Score(p, layout, widthGoal), layout));
            }
        }
        return [.. found.DistinctBy(f => f.Layout.Key()).OrderBy(f => f.Score).Take(offered).Select(f => f.Layout)];
    }

    /// <summary>
    /// Groups by record; the groups by the largest key of their items, then record id; items in a group by the key, then units, then list order.
    /// </summary>
    static PackLayout? TryLayout(PackProblem p, Func<PackItem, long> key, bool columns)
    {
        var items = p.Items;
        var order = Enumerable.Range(0, items.Length)
            .GroupBy(i => p.Records[i], StringComparer.Ordinal)
            .OrderByDescending(g => g.Max(i => key(items[i]))).ThenBy(g => g.Key, StringComparer.Ordinal)
            .SelectMany(g => g.OrderByDescending(i => key(items[i])).ThenByDescending(i => p.Quantities[i]))
            .ToArray();
        var layout = Fill(p, order, byColumns: false);
        if (layout is null || !columns) return layout;

        var placed = order.OrderBy(i => layout.Y[i]).ThenBy(i => layout.X[i]).ToArray();
        return Fill(p, placed, byColumns: true);
    }

    /// <summary>
    /// Each item on the first free spot by rows (or by columns), or null when one finds none.
    /// </summary>
    static PackLayout? Fill(PackProblem p, int[] order, bool byColumns)
    {
        var taken = new bool[p.Width, p.Height];
        var (xs, ys) = (new int[p.Items.Length], new int[p.Items.Length]);
        foreach (int i in order)
        {
            var it = p.Items[i];
            if (FirstFree(p, taken, it, byColumns) is not var (x, y)) return null;

            (xs[i], ys[i]) = (x, y);
            for (int c = x; c < x + it.Width; c++)
            {
                for (int r = y; r < y + it.Height; r++) taken[c, r] = true;
            }
        }
        return new PackLayout(xs, ys);
    }

    static (int X, int Y)? FirstFree(PackProblem p, bool[,] taken, PackItem it, bool byColumns)
    {
        int outer = byColumns ? p.Width - it.Width : p.Height - it.Height;
        int inner = byColumns ? p.Height - it.Height : p.Width - it.Width;
        for (int a = 0; a <= outer; a++)
        {
            for (int b = 0; b <= inner; b++)
            {
                var (x, y) = byColumns ? (a, b) : (b, a);
                if (IsFree(taken, x, y, it)) return (x, y);
            }
        }
        return null;
    }

    static bool IsFree(bool[,] taken, int x, int y, PackItem it)
    {
        for (int c = x; c < x + it.Width; c++)
        {
            for (int r = y; r < y + it.Height; r++)
            {
                if (taken[c, r]) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Lower is better: the used rows (Height) or used columns (Width), the other as a tie-break.
    /// </summary>
    static long Score(PackProblem p, PackLayout layout, bool widthGoal)
    {
        long rows = Enumerable.Range(0, p.Items.Length).Select(i => layout.Y[i] + p.Items[i].Height).DefaultIfEmpty(0).Max();
        long cols = Enumerable.Range(0, p.Items.Length).Select(i => layout.X[i] + p.Items[i].Width).DefaultIfEmpty(0).Max();
        return widthGoal ? cols * 1000 + rows : rows * 1000 + cols;
    }
}
