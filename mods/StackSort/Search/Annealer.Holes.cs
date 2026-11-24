namespace Meitou.StackSort.Search;

/// <summary>
/// The hole move (StackSort.md "Hole move"): when the best layout strands cells, one stranded inner cell is drawn (reservoir sampling, one draw
/// per candidate), the free cells around it (up to 16, depth first) give a box, and the first item in the order that fits the box (the first
/// item of a record with several is left alone) moves to the front. Nothing fits, or nothing is stranded: a swap instead.
/// </summary>
sealed partial class Annealer
{
    /// <summary>
    /// Whether the best layout changed since <see cref="_holeCells"/> was taken.
    /// </summary>
    bool _holesDirty = true;

    /// <summary>
    /// The best layout's stranded inner cells (free, not connected to its largest empty rectangle), row by row.
    /// </summary>
    readonly List<int> _holeCells = [];

    short[] _holeOwner = [];

    bool HoleMove(int[] order)
    {
        if (_best.Metrics.Stranded <= 0 || _best.Placements.Count == 0) return false;
        if (_holesDirty) FindHoles();
        if (_holeCells.Count == 0) return false;

        var (w, h) = HoleBox(PickHole());
        int k = Filler(order, w, h);
        if (k < 0) return false;

        MoveTo(order, k, 0);
        return true;
    }

    void FindHoles()
    {
        _holesDirty = false;
        _holeCells.Clear();
        int width = _problem.Width, height = _problem.Height;
        _holeOwner = Cells.Owners(_best.Placements, width, height);
        var reached = Reached(_holeOwner, _best.Metrics);
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int cell = y * width + x;
                if (Cells.Free(_holeOwner, cell) && !reached[cell]) _holeCells.Add(cell);
            }
        }
    }

    /// <summary>
    /// The free cells connected to the largest empty rectangle.
    /// </summary>
    bool[] Reached(short[] owner, LayoutMetrics m)
    {
        int width = _problem.Width;
        var reached = new bool[owner.Length];
        var stack = new Stack<int>();
        for (int y = m.RectY; y < m.RectY + m.RectHeight; y++)
        {
            for (int x = m.RectX; x < m.RectX + m.RectWidth; x++) Reach(owner, y * width + x, reached, stack);
        }

        while (stack.Count > 0)
        {
            int cell = stack.Pop();
            foreach (int next in Neighbours(cell)) Reach(owner, next, reached, stack);
        }
        return reached;
    }

    static void Reach(short[] owner, int cell, bool[] reached, Stack<int> stack)
    {
        if (reached[cell] || !Cells.Free(owner, cell)) return;

        reached[cell] = true;
        stack.Push(cell);
    }

    /// <summary>
    /// The cell's grid neighbours: left, right, up, down.
    /// </summary>
    IEnumerable<int> Neighbours(int cell)
    {
        int width = _problem.Width, x = cell % width, y = cell / width;
        if (x > 0) yield return cell - 1;
        if (x < width - 1) yield return cell + 1;
        if (y > 0) yield return cell - width;
        if (y < _problem.Height - 1) yield return cell + width;
    }

    /// <summary>
    /// Reservoir sampling: the k-th candidate replaces the pick when a draw modulo k is 0.
    /// </summary>
    int PickHole()
    {
        int pick = -1;
        for (int k = 0; k < _holeCells.Count; k++)
        {
            if (Next() % (uint)(k + 1) == 0) pick = _holeCells[k];
        }
        return pick;
    }

    /// <summary>
    /// The bounding box of the first 16 free cells met depth first from <paramref name="start"/>.
    /// </summary>
    (int W, int H) HoleBox(int start)
    {
        int width = _problem.Width;
        var seen = new bool[_holeOwner.Length];
        var stack = new Stack<int>();
        stack.Push(start);
        seen[start] = true;
        int x0 = start % width, x1 = x0, y0 = start / width, y1 = y0;
        for (int popped = 0; stack.Count > 0 && popped < 16; popped++)
        {
            int cell = stack.Pop(), x = cell % width, y = cell / width;
            (x0, x1, y0, y1) = (Math.Min(x0, x), Math.Max(x1, x), Math.Min(y0, y), Math.Max(y1, y));
            foreach (int next in Neighbours(cell))
            {
                if (seen[next] || !Cells.Free(_holeOwner, next)) continue;

                seen[next] = true;
                stack.Push(next);
            }
        }
        return (x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>
    /// The first order position whose item fits a <paramref name="w"/> x <paramref name="h"/> box, skipping the first item of each record that
    /// has several; -1 when none fits.
    /// </summary>
    int Filler(int[] order, int w, int h)
    {
        var count = new int[_n];
        var first = Enumerable.Repeat(-1, _n).ToArray();
        for (int k = 0; k < _n; k++)
        {
            int g = _problem.Items[order[k]].Group;
            if (g < 0 || g >= _n) continue;

            count[g]++;
            if (first[g] < 0) first[g] = k;
        }

        for (int k = 0; k < _n; k++)
        {
            var it = _problem.Items[order[k]];
            bool leader = it.Group >= 0 && it.Group < _n && first[it.Group] == k && count[it.Group] >= 2;
            if (!leader && it.Width <= w && it.Height <= h) return k;
        }
        return -1;
    }
}
