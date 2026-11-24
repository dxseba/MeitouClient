namespace Meitou.StackSort.Search;

/// <summary>
/// The search's random stream and moves on an item order (StackSort.md "Moves" and "Random stream"): a roll of 0-99 picks a swap, a shift
/// (take one item out and insert it elsewhere), a turn (the game never turns an item, so this swaps) or a hole move; and the restarts'
/// shuffle of the base order's runs. Every draw is the generator's next value modulo the range.
/// </summary>
sealed partial class Annealer
{
    uint _seed;

    /// <summary>
    /// The generator: x = x * 1664525 + 1013904223 (mod 2^32).
    /// </summary>
    uint Next() => _seed = _seed * 1664525u + 1013904223u;

    int Draw(int range) => (int)(Next() % (uint)range);

    void Move(int[] order)
    {
        int roll = Draw(100);
        if (!_p.HoleMoves && roll >= _p.TurnBelow) roll = Draw(Math.Max(_p.SwapBelow, 1));

        if (roll < _p.SwapBelow) Swap(order);
        else if (roll < _p.ShiftBelow) Shift(order);
        else if (roll < _p.TurnBelow) Swap(order);
        else if (!HoleMove(order)) Swap(order);
    }

    /// <summary>
    /// A move's first position: uniform on a roll below <see cref="SearchParams.UniformPercent"/>, else the latest of Bias / 4 draws (the
    /// order's tail holds what fitted worst).
    /// </summary>
    int First(int n)
    {
        if (_p.Bias < 1 || _p.UniformPercent > 99 || Draw(100) < _p.UniformPercent) return Draw(n);

        int pick = 0;
        for (int d = Math.Max(1, _p.Bias / 4); d > 0; d--) pick = Math.Max(pick, Draw(n));
        return pick;
    }

    /// <summary>
    /// A second position, other than <paramref name="first"/>.
    /// </summary>
    int Second(int n, int first)
    {
        int j = Draw(n - 1);
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
    /// Shuffles the runs of one record in <paramref name="order"/> (Fisher-Yates over the runs from the last; a run keeps its items' order).
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
            int j = Draw(i + 1);
            (runs[i], runs[j]) = (runs[j], runs[i]);
        }
        runs.SelectMany(r => r).ToArray().CopyTo(order, 0);
    }
}
