namespace Meitou.StackSort.Search;

/// <summary>
/// After the restarts (StackSort.md "Search"): path relinking between the elite orders (each new best and each restart's last order, up to
/// <see cref="SearchParams.Elites"/>, the worst replaced when full), then a polish that swaps items of the same footprint while the grouping
/// term grows.
/// </summary>
sealed partial class Annealer
{
    readonly List<(int[] Order, long Score)> _elites = [];

    void AddElite(int[] order, long score)
    {
        if (_elites.Count < _p.Elites)
        {
            _elites.Add(((int[])order.Clone(), score));
            return;
        }

        int worst = 0;
        for (int i = 1; i < _elites.Count; i++)
        {
            if (_elites[i].Score < _elites[worst].Score) worst = i;
        }
        if (_elites[worst].Score < score) _elites[worst] = ((int[])order.Clone(), score);
    }

    /// <summary>
    /// For each ordered pair of elites, walks from the first toward the second: at each position where they differ, swaps in the guide's item
    /// and scores the order, keeping any new best.
    /// </summary>
    void Relink()
    {
        if (_elites.Count < 2) return;

        for (int a = 0; a < _elites.Count; a++)
        {
            for (int b = 0; b < _elites.Count; b++)
            {
                if (a != b) Walk((int[])_elites[a].Order.Clone(), _elites[b].Order);
            }
        }
    }

    void Walk(int[] order, int[] guide)
    {
        for (int k = 0; k < order.Length; k++)
        {
            if (order[k] == guide[k]) continue;

            int m = Array.IndexOf(order, guide[k], k + 1);
            if (m < 0) return;

            (order[k], order[m]) = (order[m], order[k]);
            if (Score(order) > _best.Score) _best = Evaluate(order);
        }
    }

    /// <summary>
    /// Swaps the cells of two placed items of the same footprint and different records while that raises the grouping term (the geometry, and
    /// so the rest of the score, stays), pass after pass until none does.
    /// </summary>
    SearchResult Polish(SearchResult best)
    {
        var (x, y) = ((int[])best.Layout.X.Clone(), (int[])best.Layout.Y.Clone());
        _holes.Load(x, y);
        long grouping = Grouping.Of(_holes, _p);
        bool improved = true;
        for (int pass = 0; improved && pass < 16; pass++)
        {
            improved = false;
            foreach (var (i, j) in SameFootprints(x))
            {
                Exchange(x, y, i, j);
                _holes.Load(x, y);
                long next = Grouping.Of(_holes, _p);
                if (next > grouping) (grouping, improved) = (next, true);
                else Exchange(x, y, i, j);
            }
        }

        _holes.Load(x, y);
        var metrics = LayoutMetrics.Of(_holes, _p);
        long score = metrics.Score(_target, _p);
        return score > best.Score ? best with { Layout = new PackLayout(x, y), Metrics = metrics, Score = score } : best;
    }

    IEnumerable<(int, int)> SameFootprints(int[] x)
    {
        var items = _problem.Items;
        for (int i = 0; i < items.Length; i++)
        {
            for (int j = i + 1; j < items.Length; j++)
            {
                if (x[i] < 0 || x[j] < 0 || items[i].Group == items[j].Group) continue;
                if (items[i].Width == items[j].Width && items[i].Height == items[j].Height) yield return (i, j);
            }
        }
    }

    static void Exchange(int[] x, int[] y, int i, int j)
    {
        (x[i], x[j]) = (x[j], x[i]);
        (y[i], y[j]) = (y[j], y[i]);
    }
}
