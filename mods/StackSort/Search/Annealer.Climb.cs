namespace Meitou.StackSort.Search;

/// <summary>
/// The restarts (StackSort.md "Climb"): late-acceptance hill climbing from the warm start or the base order, later restarts from the base
/// order with its record runs shuffled.
/// </summary>
sealed partial class Annealer
{
    void Restarts(int[]? warm)
    {
        int restarts = _p.Restarts;
        for (int r = 0; r < restarts; r++)
        {
            var order = StartOrder(r, warm);
            long current = Climb(r, order);
            if (_p.UseElites) AddElite(order, current);
            if (r == 0 && EasyProblem(warm)) restarts = 2;
        }
    }

    /// <summary>
    /// The first restart's order is the warm start (or the base order); later ones shuffle the base order's runs of one record.
    /// </summary>
    int[] StartOrder(int restart, int[]? warm)
    {
        if (restart == 0 && warm is not null) return (int[])warm.Clone();

        var order = (int[])_base.Clone();
        if (restart > 0) ShuffleRuns(order);
        return order;
    }

    /// <summary>
    /// After the first restart: a cold start whose best came within its first 200 iterations, complete, from a constructive layout with
    /// concentrated free space (over 0.9), gets two restarts in all.
    /// </summary>
    bool EasyProblem(int[]? warm) =>
        _p.EasyStop && warm is null && _bestIteration is >= 0 and < 200 && _initialConcentration > 0.9 && _best.Metrics.Placed == _n;

    /// <summary>
    /// One restart from <paramref name="order"/> (left holding its last accepted order); returns its last accepted score. A move is undone when
    /// it scores below both the current order and the score <see cref="SearchParams.History"/> iterations ago; the restart ends after its
    /// iterations or a plateau without a new best.
    /// </summary>
    long Climb(int restart, int[] order)
    {
        long current = ScoreOf(order);
        if (current > _best.Score) NewBest(order, current, restart, 0);

        var history = new long[_p.History];
        Array.Fill(history, current);
        var saved = new int[_n];
        int stale = 0;
        for (int k = 0; k < _p.Iterations; k++)
        {
            Array.Copy(order, saved, _n);
            Move(order);
            long score = ScoreOf(order);
            int slot = k % history.Length;
            (current, stale) = Accept(score, order, saved, current, history[slot], stale, restart, k);
            history[slot] = current;
            if (stale >= _p.Plateau) break;
        }
        return current;
    }

    (long Current, int Stale) Accept(long score, int[] order, int[] saved, long current, long late, int stale, int restart, int k)
    {
        if (score < current && score < late)
        {
            Array.Copy(saved, order, _n);
            return (current, stale + 1);
        }

        if (score <= _best.Score) return (score, stale + 1);

        NewBest(order, score, restart, k);
        return (score, 0);
    }

    void NewBest(int[] order, long score, int restart, int iteration)
    {
        SetBest(order, score);
        if (restart == 0) _bestIteration = iteration;
        if (_p.UseElites) AddElite(order, score);
    }
}
