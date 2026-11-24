namespace Meitou.StackSort.Search;

/// <summary>
/// A search's answer for one target: the best layout, the order that decodes to it (the next target's warm start), its metrics and score.
/// </summary>
sealed record SearchResult(int Target, int[] Order, PackLayout Layout, LayoutMetrics Metrics, long Score)
{
    public bool Complete => Layout.Complete;
}

/// <summary>
/// The original's <c>Packer::Search::PackAnnealed</c> rebuilt from its description (StackSort.md "Search"): restarted late-acceptance hill
/// climbing over item orders, each order decoded by <see cref="LayoutDecoder"/> and scored by <see cref="LayoutMetrics.Score"/>, then path
/// relinking between the elite orders and a swap polish of equal footprints. Every random choice comes from one linear congruential
/// generator seeded from the item count and the target, so a problem always gives the same layout.
/// </summary>
sealed partial class Annealer
{
    readonly PackProblem _problem;
    readonly int _target;
    readonly SearchParams _p;
    readonly LayoutDecoder _decoder;
    readonly int[] _base;
    uint _seed;

    SearchResult _best = null!;
    int _bestRestart, _bestIteration = -1;

    Annealer(PackProblem problem, int target, SearchParams p)
    {
        (_problem, _target, _p) = (problem, target, p);
        _decoder = new LayoutDecoder(problem, p.WasteWeight);
        _base = BaseOrder(problem);
        _holes = new LayoutDecoder(problem, p.WasteWeight);
        int n = problem.Items.Length;
        _seed = (uint)n * 0x9E37u ^ (uint)target * 0x9E3779B1u;
        if (_seed == 0) _seed = 1;
    }

    /// <summary>
    /// The best layout of <paramref name="problem"/> for keeping <paramref name="target"/> rows free at the bottom, starting the first restart
    /// from <paramref name="warm"/> (the previous target's best order) when given.
    /// </summary>
    public static SearchResult Run(PackProblem problem, int target, SearchParams p, int[]? warm = null) =>
        new Annealer(problem, target, p).Search(warm);

    SearchResult Search(int[]? warm)
    {
        ReserveBand();
        _best = Evaluate(_base);
        if (_problem.Items.Length < 3) return _best;

        var initial = _best;
        int restarts = _p.Restarts;
        for (int r = 0; r < restarts; r++)
        {
            Climb(r, StartOrder(r, warm));
            if (r == 0 && EasyProblem(initial, warm)) restarts = Math.Min(restarts, 2);
        }

        Relink();
        return Polish(_best);
    }

    /// <summary>
    /// The items by longest side, then area, both largest first, then record group, then list position.
    /// </summary>
    static int[] BaseOrder(PackProblem problem)
    {
        var items = problem.Items;
        return [.. Enumerable.Range(0, items.Length)
            .OrderByDescending(i => Math.Max(items[i].Width, items[i].Height))
            .ThenByDescending(i => items[i].Width * items[i].Height)
            .ThenBy(i => items[i].Group)
            .ThenBy(i => i)];
    }

    /// <summary>
    /// The reserved columns: the widest block of the kept rows, from the right edge, that the base order still fits around (none for a target
    /// of 1, or when even one column does not fit: then the kept rows only take what fits nowhere else).
    /// </summary>
    void ReserveBand()
    {
        int width = _problem.Width;
        if (_target > 1)
        {
            for (int from = 0; from < width; from++)
            {
                _decoder.Target(_target, from, width - from);
                _decoder.Decode(_base);
                if (_decoder.Placed == _problem.Items.Length) return;
            }
        }
        _decoder.Target(_target, 0, 0);
    }

    /// <summary>
    /// The first restart's order is the warm start (or the base order); later ones shuffle the base order's runs of one record.
    /// </summary>
    int[] StartOrder(int restart, int[]? warm)
    {
        if (restart == 0) return (int[])(warm is { Length: > 0 } && warm.Length == _base.Length ? warm : _base).Clone();

        var order = (int[])_base.Clone();
        ShuffleRuns(order);
        return order;
    }

    /// <summary>
    /// After the first restart: a problem whose base layout placed everything with concentrated free space (over 0.9), and whose best came
    /// within the first 200 iterations without a warm start, gets two restarts in all.
    /// </summary>
    bool EasyProblem(SearchResult initial, int[]? warm) =>
        warm is null && _bestRestart == 0 && _bestIteration is >= 0 and < 200 && initial.Complete && initial.Metrics.Concentration > 0.9;

    /// <summary>
    /// One restart: late-acceptance hill climbing from <paramref name="order"/>. A move is kept when it scores no worse than the current order
    /// or than the score <see cref="SearchParams.History"/> iterations ago; the restart ends after its iterations or a plateau.
    /// </summary>
    void Climb(int restart, int[] order)
    {
        long current = Score(order);
        if (current > _best.Score) NewBest(order, restart, 0);

        var history = new long[_p.History];
        Array.Fill(history, current);
        var saved = new int[order.Length];
        int stale = 0;
        for (int k = 0; k < _p.Iterations && stale < _p.Plateau; k++)
        {
            Array.Copy(order, saved, order.Length);
            Move(order);
            long score = Score(order);
            int slot = k % history.Length;
            (current, stale) = Accept(score, order, saved, current, history[slot], stale, restart, k);
            history[slot] = current;
        }

        AddElite(order, current);
    }

    (long Current, int Stale) Accept(long score, int[] order, int[] saved, long current, long late, int stale, int restart, int k)
    {
        if (score < current && score < late)
        {
            Array.Copy(saved, order, order.Length);
            return (current, stale + 1);
        }

        if (score <= _best.Score) return (score, stale + 1);

        NewBest(order, restart, k);
        return (score, 0);
    }

    void NewBest(int[] order, int restart, int iteration)
    {
        _best = Evaluate(order);
        if (restart == 0) _bestIteration = iteration;
        _bestRestart = restart;
        _holesStale = true;
        AddElite(order, _best.Score);
    }

    /// <summary>
    /// Decodes and scores an order.
    /// </summary>
    long Score(int[] order)
    {
        _decoder.Decode(order);
        return LayoutMetrics.Of(_decoder, _p).Score(_target, _p);
    }

    /// <summary>
    /// Decodes and scores an order, keeping the layout.
    /// </summary>
    SearchResult Evaluate(int[] order)
    {
        _decoder.Decode(order);
        var metrics = LayoutMetrics.Of(_decoder, _p);
        var layout = new PackLayout((int[])_decoder.X.Clone(), (int[])_decoder.Y.Clone());
        return new SearchResult(_target, (int[])order.Clone(), layout, metrics, metrics.Score(_target, _p));
    }

    /// <summary>
    /// The generator: x = x * 1664525 + 1013904223 (mod 2^32).
    /// </summary>
    uint Next() => _seed = _seed * 1664525u + 1013904223u;

    int Below(int n) => n <= 1 ? 0 : (int)(Next() % (uint)n);
}
