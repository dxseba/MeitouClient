namespace Meitou.StackSort.Search;

/// <summary>
/// The hole move (StackSort.md "Moves"): pick one stranded cell of the best layout at random, take the bounding box of its free region
/// (outside the largest empty rectangle), and move an item that fits that box to the order position of the first item the best layout put at
/// or below the hole's top, so the decoder reaches the hole with it first.
/// </summary>
sealed partial class Annealer
{
    (int X, int Y, int W, int H) _holeRect;

    bool HoleMove(int[] order)
    {
        RefreshHoles();
        if (_holeCells.Count == 0) return false;

        var (_, y0, w, h) = HoleBox(_holeCells[Below(_holeCells.Count)]);
        int at = InsertionPoint(order, y0);
        if (at < 0) return false;

        var fitting = new List<int>();
        for (int k = at + 1; k < order.Length; k++)
        {
            var item = _problem.Items[order[k]];
            if (item.Width <= w && item.Height <= h) fitting.Add(k);
        }

        if (fitting.Count == 0) return false;

        MoveTo(order, fitting[Below(fitting.Count)], at);
        return true;
    }

    /// <summary>
    /// The first order position whose item the best layout put at or below row <paramref name="top"/> (or nowhere).
    /// </summary>
    int InsertionPoint(int[] order, int top)
    {
        var y = _best.Layout.Y;
        for (int k = 0; k < order.Length; k++)
        {
            if (y[order[k]] < 0 || y[order[k]] >= top) return k;
        }
        return -1;
    }

    void RefreshHoles()
    {
        if (!_holesStale) return;

        _holesStale = false;
        _holeCells.Clear();
        _holes.Load(_best.Layout.X, _best.Layout.Y);
        var rect = LayoutMetrics.LargestEmpty(_holes);
        _holeRect = (rect.X, rect.Y, rect.W, rect.H);
        int width = _problem.Width, height = _problem.Height;
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (_holes.Free(x, y) && !InRect(x, y)) _holeCells.Add(y * width + x);
            }
        }
    }

    bool InRect(int x, int y) =>
        _holeRect.W > 0 && x >= _holeRect.X && x < _holeRect.X + _holeRect.W && y >= _holeRect.Y && y < _holeRect.Y + _holeRect.H;

    /// <summary>
    /// The bounding box of the free cells outside the rectangle connected to <paramref name="cell"/> (up to 64 of them).
    /// </summary>
    (int X, int Y, int W, int H) HoleBox(int cell)
    {
        int width = _problem.Width;
        var queue = new List<int> { cell };
        var seen = new HashSet<int> { cell };
        int x0 = cell % width, x1 = x0, y0 = cell / width, y1 = y0;
        for (int head = 0; head < queue.Count && queue.Count < 64; head++)
        {
            int x = queue[head] % width, y = queue[head] / width;
            (x0, x1, y0, y1) = (Math.Min(x0, x), Math.Max(x1, x), Math.Min(y0, y), Math.Max(y1, y));
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= width || ny >= _problem.Height || !_holes.Free(nx, ny) || InRect(nx, ny)) continue;
                if (seen.Add(ny * width + nx)) queue.Add(ny * width + nx);
            }
        }
        return (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }
}
