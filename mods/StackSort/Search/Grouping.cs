namespace Meitou.StackSort.Search;

/// <summary>
/// The score's grouping term (StackSort.md "Grouping"): placed items of one record joined by a positive <see cref="Placement.Contact"/> form
/// clusters, each worth L x isqrt(L) for its summed contact L; items of different but related records (<see cref="Affinity"/>) form affinity
/// clusters, each worth s x isqrt(isqrt(s)) for s = sum of affinity x contact / 100, of which <see cref="SearchParams.AffinityShare"/> percent
/// counts. Layouts of fewer than 2 or more than 256 placements score 0.
/// </summary>
static class Grouping
{
    /// <summary>
    /// The neighbour list cap of the polish passes' adjacency (24 per placement).
    /// </summary>
    const int MaxNeighbours = 24;

    /// <summary>
    /// The term of a layout, every touching pair found from the cell owners (<paramref name="owner"/>: placement index per cell, -1 free).
    /// </summary>
    public static long Of(PackProblem p, IReadOnlyList<Placement> placements, short[] owner, SearchParams prm)
    {
        if (!Counted(placements.Count)) return 0;

        var sets = new Sets(placements.Count);
        for (int a = 0; a < placements.Count; a++) Sides(p, placements, owner, a, sets);
        return sets.Worth(prm);
    }

    /// <summary>
    /// The term over listed touching pairs (an <see cref="Adjacency"/>).
    /// </summary>
    public static long Of(PackProblem p, IReadOnlyList<Placement> placements, List<(int A, int B, int Contact)> pairs, SearchParams prm)
    {
        if (!Counted(placements.Count)) return 0;

        var sets = new Sets(placements.Count);
        foreach (var (a, b, contact) in pairs) sets.Join(p, placements, a, b, contact);
        return sets.Worth(prm);
    }

    /// <summary>
    /// The touching pairs (a before b, by placement index) with a positive contact, at most 24 per placement (a pair is left out once either
    /// side has 24), as the polish passes see them.
    /// </summary>
    public static List<(int A, int B, int Contact)> Adjacency(IReadOnlyList<Placement> placements)
    {
        var pairs = new List<(int, int, int)>();
        var count = new int[placements.Count];
        for (int a = 0; a < placements.Count; a++)
        {
            for (int b = a + 1; b < placements.Count; b++)
            {
                int contact = Placement.Contact(placements[a], placements[b]);
                if (contact <= 0 || count[a] >= MaxNeighbours || count[b] >= MaxNeighbours) continue;

                pairs.Add((a, b, contact));
                count[a]++;
                count[b]++;
            }
        }
        return pairs;
    }

    static bool Counted(int placed) => placed is >= 2 and <= 256;

    /// <summary>
    /// Joins placement <paramref name="a"/> with each later placement along its top, bottom, left and right side.
    /// </summary>
    static void Sides(PackProblem p, IReadOnlyList<Placement> placements, short[] owner, int a, Sets sets)
    {
        var pa = placements[a];
        int width = p.Width;
        if (pa.Y > 0) Scan(p, placements, owner, a, (pa.Y - 1) * width + pa.X, 1, pa.W, sets);
        if (pa.Bottom < p.Height) Scan(p, placements, owner, a, pa.Bottom * width + pa.X, 1, pa.W, sets);
        if (pa.X > 0) Scan(p, placements, owner, a, pa.Y * width + pa.X - 1, width, pa.H, sets);
        if (pa.Right < width) Scan(p, placements, owner, a, pa.Y * width + pa.Right, width, pa.H, sets);
    }

    static void Scan(PackProblem p, IReadOnlyList<Placement> placements, short[] owner, int a, int cell, int step, int count, Sets sets)
    {
        int last = -1;
        for (int k = 0; k < count; k++, cell += step)
        {
            int b = owner[cell];
            if (b < 0 || b == last) continue;

            last = b;
            if (b <= a) continue;

            int contact = Placement.Contact(placements[a], placements[b]);
            if (contact > 0) sets.Join(p, placements, a, b, contact);
        }
    }

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
    /// The two cluster sets of one layout: same record, and related records.
    /// </summary>
    sealed class Sets(int n)
    {
        readonly UnionFind _same = new(n);
        readonly UnionFind _related = new(n);

        public void Join(PackProblem p, IReadOnlyList<Placement> placements, int a, int b, int contact)
        {
            var ia = p.Items[placements[a].Item];
            var ib = p.Items[placements[b].Item];
            if (ia.Group >= 0 && ia.Group == ib.Group)
            {
                _same.Union(a, b, contact);
                return;
            }

            int affinity = Affinity(ia, ib);
            if (affinity > 0) _related.Union(a, b, (long)affinity * contact);
        }

        public long Worth(SearchParams p)
        {
            long clusters = _same.Sum(w => Cluster(w, p.ClusterExponent));
            if (p.AffinityShare <= 0) return clusters;

            long affinity = _related.Sum(w => AffinityCluster(w / 100));
            return clusters + p.AffinityShare * affinity / 100;
        }
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
