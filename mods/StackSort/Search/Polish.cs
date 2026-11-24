namespace Meitou.StackSort.Search;

/// <summary>
/// The passes after the search (StackSort.md "Polish"), each keeping the covered cells (so the empty rectangle and free regions) and each
/// accepting a change only when the grouping term over the placements' adjacency (<see cref="Grouping.Adjacency"/>) rises: reorder stacks of
/// up to 5 same-width placements (vertical, then horizontal strips), swap a placement with a block of others filling a same-size window, and
/// swap the items of same-footprint placements.
/// </summary>
static class Polish
{
    public static void Run(PackProblem p, List<Placement> placements, SearchParams prm)
    {
        if (placements.Count is < 2 or > 256) return;

        Strips(p, placements, prm, vertical: true);
        Strips(p, placements, prm, vertical: false);
        BlockSwaps(p, placements, prm);
        FootprintSwaps(p, placements, prm);
    }

    static long Score(PackProblem p, List<Placement> placements, SearchParams prm) =>
        Grouping.Of(p, placements, Grouping.Adjacency(placements), prm);

    /// <summary>
    /// Every chain of 2-5 placements of one column span stacked edge to edge (one row span side by side when not <paramref name="vertical"/>),
    /// from its top (left) end, tries every order of its members; the best grouping is kept. A chain longer than 5 is left alone.
    /// </summary>
    static void Strips(PackProblem p, List<Placement> placements, SearchParams prm, bool vertical)
    {
        var used = new bool[placements.Count];
        for (int s = 0; s < placements.Count; s++)
        {
            if (used[s]) continue;

            var chain = Chain(placements, Head(placements, used, s, vertical), used, vertical);
            if (chain.Count < 2 || chain.Count == 5 && Successor(placements, chain[^1], used, vertical) >= 0) continue;

            Reorder(p, placements, chain, prm, vertical);
        }
    }

    static int Head(List<Placement> placements, bool[] used, int s, bool vertical)
    {
        for (int cur = s; ;)
        {
            int before = -1;
            for (int j = 0; j < placements.Count && before < 0; j++)
            {
                if (j != cur && !used[j] && Touches(placements[j], placements[cur], vertical)) before = j;
            }

            if (before < 0) return cur;

            cur = before;
        }
    }

    static List<int> Chain(List<Placement> placements, int head, bool[] used, bool vertical)
    {
        var chain = new List<int> { head };
        used[head] = true;
        while (chain.Count < 5 && Successor(placements, chain[^1], used, vertical) is var next and >= 0)
        {
            chain.Add(next);
            used[next] = true;
        }
        return chain;
    }

    static int Successor(List<Placement> placements, int cur, bool[] used, bool vertical)
    {
        for (int j = 0; j < placements.Count; j++)
        {
            if (!used[j] && Touches(placements[cur], placements[j], vertical)) return j;
        }
        return -1;
    }

    /// <summary>
    /// Whether <paramref name="b"/> follows <paramref name="a"/> in a strip: the same column span, starting where a ends (row span, sideways).
    /// </summary>
    static bool Touches(Placement a, Placement b, bool vertical) => vertical
        ? b.X == a.X && b.W == a.W && b.Y == a.Bottom
        : b.Y == a.Y && b.H == a.H && b.X == a.Right;

    /// <summary>
    /// Lays the chain out in every permutation (lexicographic, from the identity) and keeps the first that scores highest.
    /// </summary>
    static void Reorder(PackProblem p, List<Placement> placements, List<int> chain, SearchParams prm, bool vertical)
    {
        int start = vertical ? placements[chain[0]].Y : placements[chain[0]].X;
        var perm = Enumerable.Range(0, chain.Count).ToArray();
        var best = (int[])perm.Clone();
        long bestScore = Score(p, placements, prm);
        while (NextPermutation(perm))
        {
            Lay(placements, chain, perm, start, vertical);
            long score = Score(p, placements, prm);
            if (score > bestScore) (bestScore, best) = (score, (int[])perm.Clone());
        }
        Lay(placements, chain, best, start, vertical);
    }

    static void Lay(List<Placement> placements, List<int> chain, int[] perm, int start, bool vertical)
    {
        var size = chain.Select(i => vertical ? placements[i].H : placements[i].W).ToArray();
        foreach (int k in perm)
        {
            var pl = placements[chain[k]];
            placements[chain[k]] = vertical ? pl with { Y = start } : pl with { X = start };
            start += size[k];
        }
    }

    static bool NextPermutation(int[] a)
    {
        int i = a.Length - 2;
        while (i >= 0 && a[i] >= a[i + 1]) i--;
        if (i < 0)
        {
            Array.Reverse(a);
            return false;
        }

        int j = a.Length - 1;
        while (a[j] <= a[i]) j--;
        (a[i], a[j]) = (a[j], a[i]);
        Array.Reverse(a, i + 1, a.Length - i - 1);
        return true;
    }

    /// <summary>
    /// For each placement in turn, every same-size window elsewhere (rows top to bottom, columns left to right) fully covered by at least two
    /// other placements lying inside it: the placement and that block trade places when the grouping rises.
    /// </summary>
    static void BlockSwaps(PackProblem p, List<Placement> placements, SearchParams prm)
    {
        var owner = Cells.Owners(placements, p.Width, p.Height);
        long best = Score(p, placements, prm);
        for (int u = 0; u < placements.Count; u++)
        {
            int w = placements[u].W, h = placements[u].H;
            for (int y = 0; y + h <= p.Height; y++)
            {
                for (int x = 0; x + w <= p.Width; x++)
                {
                    if (x == placements[u].X && y == placements[u].Y) continue;

                    best = TrySwap(p, placements, prm, owner, u, x, y, best);
                }
            }
        }
    }

    static long TrySwap(PackProblem p, List<Placement> placements, SearchParams prm, short[] owner, int u, int x, int y, long best)
    {
        var block = Block(p, placements, owner, u, x, y);
        if (block is null) return best;

        var saved = block.Select(i => placements[i]).Append(placements[u]).ToArray();
        var origin = placements[u];
        foreach (int i in block) placements[i] = placements[i] with { X = origin.X + placements[i].X - x, Y = origin.Y + placements[i].Y - y };
        placements[u] = origin with { X = x, Y = y };

        long score = Score(p, placements, prm);
        if (score > best)
        {
            Array.Copy(Cells.Owners(placements, p.Width, p.Height), owner, owner.Length);
            return score;
        }

        for (int k = 0; k < block.Count; k++) placements[block[k]] = saved[k];
        placements[u] = origin;
        return best;
    }

    /// <summary>
    /// The placements covering the window of placement <paramref name="u"/>'s size at (x, y), when there are 2-64 of them, the window has no
    /// free cell and no cell of u, and each lies inside it; else null.
    /// </summary>
    static List<int>? Block(PackProblem p, List<Placement> placements, short[] owner, int u, int x, int y)
    {
        var pu = placements[u];
        var found = new List<int>();
        for (int r = y; r < y + pu.H; r++)
        {
            for (int c = x; c < x + pu.W; c++)
            {
                int o = owner[r * p.Width + c];
                if (o < 0 || o == u) return null;
                if (found.Contains(o)) continue;
                if (found.Count >= 64) return null;

                found.Add(o);
            }
        }

        bool inside = found.All(i => placements[i].X >= x && placements[i].Y >= y && placements[i].Right <= x + pu.W
            && placements[i].Bottom <= y + pu.H);
        return found.Count > 1 && inside ? found : null;
    }

    /// <summary>
    /// Pairs of same-footprint placements (by footprint, then placement index) holding items of different records trade items while that
    /// raises the grouping, round after round until a round changes nothing (at most 4096 pairs).
    /// </summary>
    static void FootprintSwaps(PackProblem p, List<Placement> placements, SearchParams prm)
    {
        var pairs = SameFootprints(placements);
        if (pairs.Count == 0) return;

        var adjacency = Grouping.Adjacency(placements);
        for (bool improved = true; improved;)
        {
            improved = false;
            long best = Grouping.Of(p, placements, adjacency, prm);
            foreach (var (a, b) in pairs)
            {
                if (p.Items[placements[a].Item].Group == p.Items[placements[b].Item].Group) continue;

                Exchange(placements, a, b);
                long score = Grouping.Of(p, placements, adjacency, prm);
                if (score > best) (best, improved) = (score, true);
                else Exchange(placements, a, b);
            }
        }
    }

    static List<(int A, int B)> SameFootprints(List<Placement> placements)
    {
        var sorted = Enumerable.Range(0, placements.Count).OrderBy(i => placements[i].W << 16 | placements[i].H).ToArray();
        var pairs = new List<(int, int)>();
        for (int s = 0; s < sorted.Length;)
        {
            int e = s + 1;
            while (e < sorted.Length && Footprint(placements[sorted[e]]) == Footprint(placements[sorted[s]])) e++;
            for (int a = s; a < e; a++)
            {
                for (int b = a + 1; b < e && pairs.Count < 4096; b++) pairs.Add((sorted[a], sorted[b]));
            }
            s = e;
        }
        return pairs;
    }

    static int Footprint(Placement pl) => pl.W << 16 | pl.H;

    static void Exchange(List<Placement> placements, int a, int b)
    {
        var (pa, pb) = (placements[a], placements[b]);
        placements[a] = pa with { Item = pb.Item };
        placements[b] = pb with { Item = pa.Item };
    }
}
