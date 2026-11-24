namespace Meitou.StackSort.Search;

/// <summary>
/// The score's grouping term (StackSort.md "Score"): touching items of one record form clusters, each worth L x isqrt(L) for its total
/// shared edge length L; touching items of different but related records (<see cref="Affinity"/>) form affinity clusters, each worth
/// s x isqrt(isqrt(s)) for s = sum of affinity x shared edge / 100, of which <see cref="SearchParams.AffinityShare"/> percent counts.
/// </summary>
static class Grouping
{
    public static long Of(LayoutDecoder d, SearchParams p)
    {
        int n = d.Problem.Items.Length;
        var same = new UnionFind(n);
        var related = new UnionFind(n);
        var seen = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (d.X[i] >= 0) JoinNeighbours(d, i, seen, same, related);
        }

        long clusters = same.Sum(w => Cluster(w, p.ClusterExponent));
        long affinity = related.Sum(w => AffinityCluster(w / 100));
        return clusters + p.AffinityShare * affinity / 100;
    }

    /// <summary>
    /// Joins item <paramref name="i"/> with each touching item of a higher index, once each (<paramref name="seen"/> holds i + 1 for those
    /// already joined).
    /// </summary>
    static void JoinNeighbours(LayoutDecoder d, int i, int[] seen, UnionFind same, UnionFind related)
    {
        var it = d.Problem.Items[i];
        int x = d.X[i], y = d.Y[i];
        for (int c = x; c < x + it.Width; c++)
        {
            Touch(d, c, y - 1, i, seen, same, related);
            Touch(d, c, y + it.Height, i, seen, same, related);
        }
        for (int r = y; r < y + it.Height; r++)
        {
            Touch(d, x - 1, r, i, seen, same, related);
            Touch(d, x + it.Width, r, i, seen, same, related);
        }
    }

    static void Touch(LayoutDecoder d, int x, int y, int i, int[] seen, UnionFind same, UnionFind related)
    {
        if (x < 0 || y < 0 || x >= d.Problem.Width || y >= d.Problem.Height) return;

        int j = d.Owner(x, y);
        if (j <= i || seen[j] == i + 1) return;

        seen[j] = i + 1;
        Join(d, i, j, same, related);
    }

    static void Join(LayoutDecoder d, int i, int j, UnionFind same, UnionFind related)
    {
        var a = d.Problem.Items[i];
        var b = d.Problem.Items[j];
        int contact = SharedEdge(d, i, j);
        if (a.Group >= 0 && a.Group == b.Group)
        {
            same.Union(i, j, contact);
            return;
        }

        int affinity = Affinity(a, b);
        if (affinity > 0) related.Union(i, j, affinity * contact);
    }

    /// <summary>
    /// The length of the edge two touching items share.
    /// </summary>
    static int SharedEdge(LayoutDecoder d, int i, int j)
    {
        var (a, b) = (d.Problem.Items[i], d.Problem.Items[j]);
        int ax = d.X[i], ay = d.Y[i], bx = d.X[j], by = d.Y[j];
        if (ax + a.Width == bx || bx + b.Width == ax) return Overlap(ay, a.Height, by, b.Height);
        if (ay + a.Height == by || by + b.Height == ay) return Overlap(ax, a.Width, bx, b.Width);
        return 0;
    }

    static int Overlap(int a, int la, int b, int lb) => Math.Max(0, Math.Min(a + la, b + lb) - Math.Max(a, b));

    /// <summary>
    /// How related two items of different records are, percent: the highest of the same record type 50 (plain items excepted), the same
    /// item function 40 or a related function 20 (food and raw meat, first aid and robot repair), and a shared flag 10.
    /// </summary>
    public static int Affinity(PackItem a, PackItem b)
    {
        int best = 0;
        if (a.Type >= 0 && a.Type == b.Type) best = 50;

        int function = FunctionRelation(a.Function, b.Function);
        if (function > 0) best = Math.Max(best, (40 * function + 50) / 100);
        if ((a.Flags & b.Flags) != 0) best = Math.Max(best, 10);
        return best;
    }

    /// <summary>
    /// 100 for the same function, 50 for food with raw meat (3, 15) or first aid with robot repair (1, 12), else 0.
    /// </summary>
    static int FunctionRelation(int a, int b)
    {
        if (a < 0 || b < 0) return 0;
        if (a == b) return 100;

        var (low, high) = a < b ? (a, b) : (b, a);
        return (low, high) is (3, 15) or (1, 12) ? 50 : 0;
    }

    /// <summary>
    /// A same-record cluster's worth: L x isqrt(L) for exponent 6 (the default), L for 4, L x isqrt(isqrt(L)) for 5, L squared for 8.
    /// </summary>
    static long Cluster(long length, int exponent) => length <= 0 ? 0 : exponent switch
    {
        4 => length,
        5 => length * ISqrt(ISqrt(length)),
        8 => length * length,
        _ => length * ISqrt(length),
    };

    static long AffinityCluster(long s) => s <= 0 ? 0 : s * ISqrt(ISqrt(s));

    static long ISqrt(long v)
    {
        long r = (long)Math.Sqrt(v);
        while (r * r > v) r--;
        while ((r + 1) * (r + 1) <= v) r++;
        return r;
    }

    /// <summary>
    /// Union-find with a weight per set.
    /// </summary>
    sealed class UnionFind
    {
        readonly int[] _parent;
        readonly long[] _weight;

        public UnionFind(int n)
        {
            _parent = [.. Enumerable.Range(0, n)];
            _weight = new long[n];
        }

        int Find(int i)
        {
            while (_parent[i] != i) i = _parent[i] = _parent[_parent[i]];
            return i;
        }

        public void Union(int a, int b, long weight)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
            {
                _parent[ra] = rb;
                _weight[rb] += _weight[ra];
                _weight[ra] = 0;
            }
            _weight[rb] += weight;
        }

        /// <summary>
        /// The sum of <paramref name="worth"/> over the sets' weights.
        /// </summary>
        public long Sum(Func<long, long> worth)
        {
            long sum = 0;
            for (int i = 0; i < _parent.Length; i++)
            {
                if (_parent[i] == i && _weight[i] > 0) sum += worth(_weight[i]);
            }
            return sum;
        }
    }
}
