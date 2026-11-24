namespace Meitou.StackSort.Search;

/// <summary>
/// The original's constructive packer (StackSort.md "Constructive packer"): a list of maximal free rectangles. Each item in order takes the
/// top left of the free rectangle it fits best; the used cells are cut out of every free rectangle they touch (left, right, top, bottom
/// strips) and rectangles inside others are dropped. Two fit rules: <see cref="Fit.ShortSide"/> (least short leftover, then least long
/// leftover) and <see cref="Fit.Product"/> (least leftover width x leftover height, then least short leftover).
/// </summary>
static class MaxRects
{
    public enum Fit
    {
        ShortSide,
        Product,
    }

    /// <summary>
    /// Packs <paramref name="order"/>; a target keeps rows free as the decoder does: a reserved block (<paramref name="blockWidth"/> > 0) is
    /// taken out first; without one an item first tries above the kept rows. Items that fit nowhere stay out.
    /// </summary>
    public static List<Placement> Pack(PackProblem p, int[] order, int target, int blockFrom, int blockWidth, Fit fit)
    {
        var free = new List<(int X, int Y, int W, int H)> { (0, 0, p.Width, p.Height) };
        var placed = new List<Placement>();
        int cap = p.Height - target;
        if (blockWidth > 0) Use(free, (blockFrom, cap, blockWidth, target));

        foreach (int item in order)
        {
            var it = p.Items[item];
            int at = blockWidth > 0 ? -1 : Best(free, it, cap, fit);
            if (at < 0) at = Best(free, it, -1, fit);
            if (at < 0) continue;

            var spot = new Placement(item, free[at].X, free[at].Y, it.Width, it.Height);
            placed.Add(spot);
            Use(free, (spot.X, spot.Y, spot.W, spot.H));
        }
        return placed;
    }

    /// <summary>
    /// The best free rectangle for an item whose bottom stays within row <paramref name="cap"/> (-1: anywhere), -1 when none fits.
    /// </summary>
    static int Best(List<(int X, int Y, int W, int H)> free, PackItem it, int cap, Fit fit)
    {
        int best = -1;
        long bestFirst = long.MaxValue, bestSecond = long.MaxValue;
        for (int i = 0; i < free.Count; i++)
        {
            var r = free[i];
            if (it.Width > r.W || it.Height > r.H || cap >= 0 && it.Height + r.Y > cap) continue;

            var (first, second) = Leftover(r.W - it.Width, r.H - it.Height, fit);
            if (first < bestFirst || first == bestFirst && second < bestSecond) (best, bestFirst, bestSecond) = (i, first, second);
        }
        return best;
    }

    static (long First, long Second) Leftover(int w, int h, Fit fit) =>
        fit == Fit.ShortSide ? (Math.Min(w, h), Math.Max(w, h)) : ((long)w * h, Math.Min(w, h));

    /// <summary>
    /// Cuts a used rectangle out of the free list and drops contained rectangles.
    /// </summary>
    static void Use(List<(int X, int Y, int W, int H)> free, (int X, int Y, int W, int H) used)
    {
        var next = new List<(int X, int Y, int W, int H)>(free.Count + 4);
        foreach (var f in free)
        {
            bool overlaps = f.X < used.X + used.W && used.X < f.X + f.W && used.Y < f.Y + f.H && f.Y < used.Y + used.H;
            if (!overlaps)
            {
                next.Add(f);
                continue;
            }

            if (f.X < used.X) next.Add((f.X, f.Y, used.X - f.X, f.H));
            if (used.X + used.W < f.X + f.W) next.Add((used.X + used.W, f.Y, f.X + f.W - used.X - used.W, f.H));
            if (f.Y < used.Y) next.Add((f.X, f.Y, f.W, used.Y - f.Y));
            if (used.Y + used.H < f.Y + f.H) next.Add((f.X, used.Y + used.H, f.W, f.Y + f.H - used.Y - used.H));
        }

        free.Clear();
        free.AddRange(Prune(next));
    }

    /// <summary>
    /// Drops every rectangle inside another (StackSort.md: a later one inside an earlier one goes, or the earlier one when it lies inside a later
    /// one that starts in the same column or left of it), keeping the order.
    /// </summary>
    static IEnumerable<(int X, int Y, int W, int H)> Prune(List<(int X, int Y, int W, int H)> rects)
    {
        var gone = new bool[rects.Count];
        for (int i = 0; i < rects.Count; i++)
        {
            for (int j = i + 1; j < rects.Count && !gone[i]; j++)
            {
                if (gone[j]) continue;

                if (Inside(rects[j], rects[i])) gone[j] = true;
                else if (rects[i].X >= rects[j].X && Inside(rects[i], rects[j])) gone[i] = true;
            }
        }
        return rects.Where((_, i) => !gone[i]);
    }

    static bool Inside((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b) =>
        b.X <= a.X && b.Y <= a.Y && a.X + a.W <= b.X + b.W && a.Y + a.H <= b.Y + b.H;
}
