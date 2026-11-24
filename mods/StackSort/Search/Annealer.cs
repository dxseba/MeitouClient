namespace Meitou.StackSort.Search;

/// <summary>
/// A search's answer for one target: the best layout, the order the next target starts from (the original's out order: the base order or the
/// last order that set a new best), its metrics and score.
/// </summary>
sealed record SearchResult(int Target, int[] Order, PackLayout Layout, LayoutMetrics Metrics, long Score)
{
    public bool Complete => Layout.Complete;
}

/// <summary>
/// The original's <c>Packer::Search::PackAnnealed</c> core rebuilt from its description (StackSort.md "Search"): a constructive layout first,
/// then restarted late-acceptance hill climbing over item orders decoded by <see cref="SkylineDecoder"/>, path relinking between elite orders,
/// a comparison with the constructive packer when a block was reserved, and the polish passes (<see cref="Polish"/>). Every random choice
/// comes from one linear congruential generator in the original's draw order, so a problem always gives the same layout.
/// </summary>
sealed partial class Annealer
{
    readonly PackProblem _problem;
    readonly int _target, _n;
    readonly SearchParams _p;
    readonly SkylineDecoder _decoder;
    readonly int[] _base;
    int _blockFrom, _blockWidth;

    (List<Placement> Placements, LayoutMetrics Metrics, long Score) _best;
    LayoutMetrics _last;
    int[] _out;
    int _bestIteration = -1;
    double _initialConcentration;

    Annealer(PackProblem problem, int target, SearchParams p)
    {
        (_problem, _target, _p, _n) = (problem, target, p, problem.Items.Length);
        _decoder = new SkylineDecoder(problem, p.WasteWeight);
        _base = BaseOrder(problem);
        _out = _base;
        _seed = (uint)_n * 0x9E37u ^ (uint)target * 0x9E3779B1u;
        if (_seed == 0) _seed = 1;
    }

    /// <summary>
    /// The best layout of <paramref name="problem"/> for keeping <paramref name="target"/> rows free at the bottom. The first restart starts
    /// from <paramref name="warm"/> (the previous target's order) when given; <paramref name="shortcut"/> is the largest empty rectangle an
    /// earlier target reached at least this tall (0: none): a reserved block smaller than that takes the constructive packer's layout alone.
    /// </summary>
    public static SearchResult Run(PackProblem problem, int target, SearchParams p, int[]? warm = null, int shortcut = 0) =>
        new Annealer(problem, target, p).Search(warm is { Length: > 0 } && warm.Length == problem.Items.Length ? warm : null, shortcut);

    SearchResult Search(int[]? warm, int shortcut)
    {
        if (_n < 3) return Result(Constructive(), warm ?? _base);

        ReserveBlock();
        if (shortcut > 0 && _target > 1 && _blockWidth > 0 && _blockWidth * _target < shortcut) return Result(Constructive(), _base);

        Start();
        Restarts(warm);
        if (_p.UseElites) Relink();
        if (_blockWidth > 0) CompareConstructive();
        return Polished();
    }

    /// <summary>
    /// The items by longest side, then area, both largest first, then record group, then list position (a stable sort).
    /// </summary>
    static int[] BaseOrder(PackProblem problem)
    {
        var items = problem.Items;
        return [.. Enumerable.Range(0, items.Length)
            .OrderByDescending(i => Math.Max(items[i].Width, items[i].Height))
            .ThenByDescending(i => items[i].Width * items[i].Height)
            .ThenBy(i => items[i].Group)];
    }

    /// <summary>
    /// For a target over 1: the widest block of the kept rows, from the right edge, around which the constructive packer still places the base
    /// order (none when even one column does not fit: then the kept rows only take what fits nowhere else).
    /// </summary>
    void ReserveBlock()
    {
        int width = _problem.Width;
        for (int from = 0; _target > 1 && from < width; from++)
        {
            if (MaxRects.Pack(_problem, _base, _target, from, width - from, MaxRects.Fit.ShortSide).Count < _n) continue;

            (_blockFrom, _blockWidth) = (from, width - from);
            break;
        }
        _decoder.Target(_target, _blockFrom, _blockWidth);
    }

    /// <summary>
    /// The first best: the constructive packer on the base order around the block, the product fit unless it places fewer than the short-side
    /// fit.
    /// </summary>
    void Start()
    {
        var shortSide = MaxRects.Pack(_problem, _base, _target, _blockFrom, _blockWidth, MaxRects.Fit.ShortSide);
        var product = MaxRects.Pack(_problem, _base, _target, _blockFrom, _blockWidth, MaxRects.Fit.Product);
        _best = Evaluate(product.Count < shortSide.Count ? shortSide : product);
        _initialConcentration = _best.Metrics.Concentration;
    }

    /// <summary>
    /// The constructive packer without a block (the original's second packer): the product fit unless it places fewer.
    /// </summary>
    (List<Placement> Placements, LayoutMetrics Metrics, long Score) Constructive()
    {
        var shortSide = MaxRects.Pack(_problem, _base, _target, 0, 0, MaxRects.Fit.ShortSide);
        var product = MaxRects.Pack(_problem, _base, _target, 0, 0, MaxRects.Fit.Product);
        return Evaluate(product.Count < shortSide.Count ? shortSide : product);
    }

    /// <summary>
    /// After a block was reserved, the constructive packer's complete layout replaces the best when it scores higher.
    /// </summary>
    void CompareConstructive()
    {
        var other = Constructive();
        if (other.Metrics.Placed == _n && other.Score > _best.Score) _best = other;
    }

    (List<Placement> Placements, LayoutMetrics Metrics, long Score) Evaluate(List<Placement> placements)
    {
        var owner = Cells.Owners(placements, _problem.Width, _problem.Height);
        long grouping = Grouping.Of(_problem, placements, owner, _p);
        var metrics = LayoutMetrics.Of(_problem, placements.Count, owner, grouping);
        return (placements, metrics, metrics.Score(_target, _p));
    }

    /// <summary>
    /// Decodes and scores an order (its metrics kept for a new best).
    /// </summary>
    long ScoreOf(int[] order)
    {
        _decoder.Decode(order);
        long grouping = Grouping.Of(_problem, _decoder.Placements, _decoder.Owner, _p);
        _last = LayoutMetrics.Of(_problem, _decoder.Placements.Count, _decoder.Owner, grouping);
        return _last.Score(_target, _p);
    }

    /// <summary>
    /// The last decoded layout becomes the best (its order the out order).
    /// </summary>
    void SetBest(int[] order, long score)
    {
        _best = ([.. _decoder.Placements], _last, score);
        _holesDirty = true;
        _out = (int[])order.Clone();
    }

    /// <summary>
    /// The polish passes on the best layout, then its grouping and score again (the geometry, so the rest of the metrics, stays).
    /// </summary>
    SearchResult Polished()
    {
        var placements = new List<Placement>(_best.Placements);
        Polish.Run(_problem, placements, _p);
        var owner = Cells.Owners(placements, _problem.Width, _problem.Height);
        var metrics = _best.Metrics with { Grouping = Grouping.Of(_problem, placements, owner, _p) };
        return Result((placements, metrics, metrics.Score(_target, _p)), _out);
    }

    SearchResult Result((List<Placement> Placements, LayoutMetrics Metrics, long Score) layout, int[] order)
    {
        var x = Enumerable.Repeat(-1, _n).ToArray();
        var y = Enumerable.Repeat(-1, _n).ToArray();
        foreach (var p in layout.Placements) (x[p.Item], y[p.Item]) = (p.X, p.Y);
        return new SearchResult(_target, (int[])order.Clone(), new PackLayout(x, y), layout.Metrics, layout.Score);
    }
}
