namespace Meitou.StackSort.Search;

/// <summary>
/// The search's moves on an item order (StackSort.md "Moves"): a swap, a shift (take one item out and insert it elsewhere), a turn (the game
/// never turns an item, so this is a swap too) and a hole move; and the restarts' shuffle of the base order's runs.
/// </summary>
sealed partial class Annealer
{
    /// <summary>
    /// The best layout's grid, for the hole move.
    /// </summary>
    readonly LayoutDecoder _holes;

    /// <summary>
    /// Whether the best layout changed since <see cref="_holeCells"/> was taken.
    /// </summary>
    bool _holesStale = true;

    /// <summary>
    /// The best layout's stranded cells: free inner cells outside its largest empty rectangle.
    /// </summary>
    readonly List<int> _holeCells = [];

    void Move(int[] order)
    {
        int roll = Below(100);
        if (roll < _p.SwapBelow || roll >= _p.ShiftBelow && roll < _p.TurnBelow) Swap(order);
        else if (roll < _p.ShiftBelow) Shift(order);
        else if (!HoleMove(order)) Swap(order);
    }

    /// <summary>
    /// A move's first position: uniform on a roll below <see cref="SearchParams.UniformPercent"/>, else the latest of Bias / 4 draws (the
    /// order's tail holds what fitted worst).
    /// </summary>
    int First(int n)
    {
        if (_p.Bias < 1 || _p.UniformPercent > 99 || Below(100) < _p.UniformPercent) return Below(n);

        int pick = 0;
        for (int d = Math.Max(1, _p.Bias / 4); d > 0; d--) pick = Math.Max(pick, Below(n));
        return pick;
    }

    /// <summary>
    /// A second position, other than <paramref name="first"/>.
    /// </summary>
    int Second(int n, int first)
    {
        int j = Below(n - 1);
        return j >= first ? j + 1 : j;
    }

    void Swap(int[] order)
    {
        int i = First(order.Length), j = Second(order.Length, i);
        (order[i], order[j]) = (order[j], order[i]);
    }

    void Shift(int[] order)
    {
        int i = First(order.Length), j = Second(order.Length, i);
        MoveTo(order, i, j);
    }

    static void MoveTo(int[] order, int from, int to)
    {
        int item = order[from];
        if (from < to) Array.Copy(order, from + 1, order, from, to - from);
        else Array.Copy(order, to, order, to + 1, from - to);
        order[to] = item;
    }

    /// <summary>
    /// Shuffles the runs of one record in <paramref name="order"/> (Fisher-Yates over the runs; a run keeps its items' order).
    /// </summary>
    void ShuffleRuns(int[] order)
    {
        var runs = new List<int[]>();
        for (int start = 0; start < order.Length;)
        {
            int end = start + 1;
            while (end < order.Length && _problem.Items[order[end]].Group == _problem.Items[order[start]].Group) end++;
            runs.Add(order[start..end]);
            start = end;
        }

        if (runs.Count < 2) return;

        for (int i = runs.Count - 1; i > 0; i--)
        {
            int j = Below(i + 1);
            (runs[i], runs[j]) = (runs[j], runs[i]);
        }
        runs.SelectMany(r => r).ToArray().CopyTo(order, 0);
    }
}
