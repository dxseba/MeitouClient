namespace Meitou.StackSort.Search;

/// <summary>
/// What the score reads from a layout (StackSort.md "Score"): items placed, the largest empty rectangle (area, width, height, cell), the free
/// space's concentration (sum of each free region's share squared), the stranded cells (inner cells of free regions apart from the rectangle)
/// and the grouping term.
/// </summary>
readonly record struct LayoutMetrics(int Placed, int RectArea, int RectWidth, int RectHeight, int RectX, int RectY, double Concentration,
    int Stranded, long Grouping)
{
    /// <summary>
    /// Higher is better: 1,000,000 per placed item, plus the rectangle's area squared less the stranded cells squared, plus 10,000 when the
    /// rectangle is at least <paramref name="target"/> rows tall, plus grouping x concentration, plus 50 x concentration (the weights of
    /// <paramref name="p"/>; each product truncated on its own). The original also subtracts one per turned item; the game never turns one.
    /// </summary>
    public long Score(int target, SearchParams p)
    {
        long rect = (long)p.RectangleWeight * ((long)RectArea * RectArea - (long)Stranded * Stranded);
        if (RectHeight >= target) rect += p.RectangleWeight * 10_000L;

        long grouping = (long)((double)Grouping * Concentration * p.GroupWeight);
        return 1_000_000L * Placed + rect + grouping + (long)(p.ConcentrationWeight * Concentration);
    }

    /// <summary>
    /// The metrics of a layout, given its cell owners (-1 free) and its grouping term.
    /// </summary>
    public static LayoutMetrics Of(PackProblem problem, int placed, short[] owner, long grouping)
    {
        var rect = LargestEmpty(owner, problem.Width, problem.Height);
        var (concentration, stranded) = FreeRegions(owner, problem.Width, problem.Height, rect);
        return new(placed, rect.Area, rect.W, rect.H, rect.X, rect.Y, concentration, stranded, grouping);
    }

    /// <summary>
    /// The largest all-free rectangle, by row histograms and a stack; the first strictly larger one wins, rows top to bottom.
    /// </summary>
    static (int Area, int W, int H, int X, int Y) LargestEmpty(short[] owner, int width, int height)
    {
        var heights = new int[width];
        var stack = new (int Start, int Height)[width + 1];
        var best = (Area: 0, W: 0, H: 0, X: 0, Y: 0);
        for (int y = 0; y < height; y++)
        {
            int rowMax = 0;
            for (int x = 0; x < width; x++)
            {
                heights[x] = owner[y * width + x] < 0 ? heights[x] + 1 : 0;
                rowMax = Math.Max(rowMax, heights[x]);
            }

            if (best.Area < rowMax * width) best = RowBest(heights, stack, y, best);
        }
        return best;
    }

    static (int Area, int W, int H, int X, int Y) RowBest(int[] heights, (int Start, int Height)[] stack, int y,
        (int Area, int W, int H, int X, int Y) best)
    {
        int width = heights.Length, top = 0;
        for (int x = 0; x <= width; x++)
        {
            int h = x < width ? heights[x] : 0, start = x;
            while (top > 0 && stack[top - 1].Height > h)
            {
                var (s, sh) = stack[--top];
                int area = sh * (x - s);
                if (area > best.Area) best = (area, x - s, sh, s, y - sh + 1);
                start = s;
            }

            stack[top++] = (start, h);
        }
        return best;
    }

    /// <summary>
    /// The free cells' 4-connected regions in row-major order of discovery: the sum of each region's share of the free cells squared, and the
    /// inner cells (off the grid's edge) of regions that do not touch the largest empty rectangle.
    /// </summary>
    static (double Concentration, int Stranded) FreeRegions(short[] owner, int width, int height, (int Area, int W, int H, int X, int Y) rect)
    {
        var seen = new bool[owner.Length];
        var stack = new int[owner.Length];
        var regions = new List<int>();
        int free = 0, stranded = 0;
        for (int start = 0; start < owner.Length; start++)
        {
            if (seen[start] || owner[start] >= 0) continue;

            var (size, inner, touches) = Flood(owner, width, height, start, seen, stack, rect);
            regions.Add(size);
            free += size;
            if (!touches) stranded += inner;
        }

        double concentration = 0;
        if (free == 0) return (0, stranded);

        foreach (int size in regions) concentration += (double)size / free * ((double)size / free);
        return (concentration, stranded);
    }

    static (int Size, int Inner, bool Touches) Flood(short[] owner, int width, int height, int start, bool[] seen, int[] stack,
        (int Area, int W, int H, int X, int Y) rect)
    {
        int top = 0, size = 0, inner = 0;
        bool touches = false;
        stack[top++] = start;
        seen[start] = true;
        while (top > 0)
        {
            int cell = stack[--top], x = cell % width, y = cell / width;
            size++;
            if (x > 0 && x < width - 1 && y > 0 && y < height - 1) inner++;
            if (x >= rect.X && x < rect.X + rect.W && y >= rect.Y && y < rect.Y + rect.H) touches = true;

            if (x > 0) top = Visit(owner, cell - 1, seen, stack, top);
            if (x < width - 1) top = Visit(owner, cell + 1, seen, stack, top);
            if (y > 0) top = Visit(owner, cell - width, seen, stack, top);
            if (y < height - 1) top = Visit(owner, cell + width, seen, stack, top);
        }
        return (size, inner, touches);
    }

    static int Visit(short[] owner, int cell, bool[] seen, int[] stack, int top)
    {
        if (seen[cell] || owner[cell] >= 0) return top;

        seen[cell] = true;
        stack[top] = cell;
        return top + 1;
    }
}
