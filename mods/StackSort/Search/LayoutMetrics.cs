namespace Meitou.StackSort.Search;

/// <summary>
/// What the score reads from a decoded layout (StackSort.md "Score"): items placed, the largest empty rectangle (area, width, height, cell),
/// the free space's concentration (sum of each free region's share squared), the stranded cells (inner cells of free regions apart from the
/// rectangle) and the grouping term.
/// </summary>
readonly record struct LayoutMetrics(int Placed, int RectArea, int RectWidth, int RectHeight, int RectX, int RectY, double Concentration,
    int Stranded, long Grouping)
{
    /// <summary>
    /// Higher is better: 1,000,000 per placed item, plus the rectangle's area squared less the stranded cells squared, plus 10,000 when the
    /// rectangle is at least <paramref name="target"/> rows tall, plus grouping x concentration, plus 50 x concentration (the weights of
    /// <paramref name="p"/>). The original also subtracts one per turned item; the game never turns one.
    /// </summary>
    public long Score(int target, SearchParams p)
    {
        long rect = (long)p.RectangleWeight * ((long)RectArea * RectArea - (long)Stranded * Stranded);
        if (RectHeight >= target) rect += p.RectangleWeight * 10_000L;

        long grouping = (long)(Grouping * Concentration * p.GroupWeight);
        return 1_000_000L * Placed + rect + grouping + (long)(p.ConcentrationWeight * Concentration);
    }

    /// <summary>
    /// The metrics of the decoder's current layout.
    /// </summary>
    public static LayoutMetrics Of(LayoutDecoder decoder, SearchParams p)
    {
        var rect = LargestEmpty(decoder);
        var (concentration, stranded) = FreeRegions(decoder, rect);
        long grouping = Search.Grouping.Of(decoder, p);
        return new(decoder.Placed, rect.Area, rect.W, rect.H, rect.X, rect.Y, concentration, stranded, grouping);
    }

    /// <summary>
    /// The largest all-free rectangle, by row histograms and a stack; the first of equal areas in row order.
    /// </summary>
    internal static (int Area, int W, int H, int X, int Y) LargestEmpty(LayoutDecoder d)
    {
        int width = d.Problem.Width, height = d.Problem.Height;
        var heights = new int[width + 1];
        var stack = new (int Start, int Height)[width + 1];
        var best = (Area: 0, W: 0, H: 0, X: 0, Y: 0);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++) heights[x] = d.Free(x, y) ? heights[x] + 1 : 0;

            int top = 0;
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

                if (top == 0 || stack[top - 1].Height < h) stack[top++] = (start, h);
            }
        }
        return best;
    }

    /// <summary>
    /// The free cells' 4-connected regions: the sum of each region's share of the free cells squared, and the inner cells (off the grid's
    /// edge) of regions that do not touch the largest empty rectangle.
    /// </summary>
    static (double Concentration, int Stranded) FreeRegions(LayoutDecoder d, (int Area, int W, int H, int X, int Y) rect)
    {
        int width = d.Problem.Width, height = d.Problem.Height;
        var seen = new bool[width * height];
        var queue = new int[width * height];
        int free = 0, stranded = 0;
        for (int cell = 0; cell < seen.Length; cell++)
        {
            if (d.Free(cell % width, cell / width)) free++;
        }

        double concentration = 0;
        for (int start = 0; start < seen.Length; start++)
        {
            if (seen[start] || !d.Free(start % width, start / width)) continue;

            var (size, inner, touches) = Flood(d, start, seen, queue, rect);
            concentration += (double)size / free * ((double)size / free);
            if (!touches) stranded += inner;
        }
        return (concentration, stranded);
    }

    static (int Size, int Inner, bool Touches) Flood(LayoutDecoder d, int start, bool[] seen, int[] queue,
        (int Area, int W, int H, int X, int Y) rect)
    {
        int width = d.Problem.Width, height = d.Problem.Height;
        int head = 0, tail = 0, inner = 0;
        bool touches = false;
        queue[tail++] = start;
        seen[start] = true;
        while (head < tail)
        {
            int cell = queue[head++], x = cell % width, y = cell / width;
            if (x > 0 && x < width - 1 && y > 0 && y < height - 1) inner++;
            if (rect.Area > 0 && x >= rect.X && x < rect.X + rect.W && y >= rect.Y && y < rect.Y + rect.H) touches = true;

            tail = Visit(d, x - 1, y, seen, queue, tail);
            tail = Visit(d, x + 1, y, seen, queue, tail);
            tail = Visit(d, x, y - 1, seen, queue, tail);
            tail = Visit(d, x, y + 1, seen, queue, tail);
        }
        return (tail, inner, touches);
    }

    static int Visit(LayoutDecoder d, int x, int y, bool[] seen, int[] queue, int tail)
    {
        int width = d.Problem.Width;
        if (x < 0 || y < 0 || x >= width || y >= d.Problem.Height) return tail;

        int cell = y * width + x;
        if (seen[cell] || !d.Free(x, y)) return tail;

        seen[cell] = true;
        queue[tail] = cell;
        return tail + 1;
    }
}
