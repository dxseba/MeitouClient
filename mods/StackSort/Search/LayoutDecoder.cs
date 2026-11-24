namespace Meitou.StackSort.Search;

/// <summary>
/// Turns an item order into a layout, the search's decoder (StackSort.md "Decoder"): each item in turn goes to the topmost row where it
/// fits, and within that row to the cell that wastes least (free cells left above it, x <see cref="SearchParams.WasteWeight"/>) against its
/// contact with items of its own record, then the leftmost. A target of <c>t</c> keeps the bottom <c>t</c> rows free: over the reserved
/// columns (<c>bandFrom</c>, <c>bandWidth</c>) no item may reach them; with no reserved columns an item reaches them only when it fits nowhere
/// above. One decoder per search; it owns its grid.
/// </summary>
sealed class LayoutDecoder
{
    readonly PackProblem _problem;
    readonly int _wasteWeight;

    /// <summary>
    /// Each row's taken cells, bit x for column x.
    /// </summary>
    readonly ulong[] _rows;

    /// <summary>
    /// Each cell's item, or -1.
    /// </summary>
    readonly short[] _owner;

    int _limit, _bandFrom, _bandWidth;

    public LayoutDecoder(PackProblem problem, int wasteWeight)
    {
        _problem = problem;
        _wasteWeight = wasteWeight;
        _rows = new ulong[problem.Height];
        _owner = new short[problem.Width * problem.Height];
        X = new int[problem.Items.Length];
        Y = new int[problem.Items.Length];
    }

    /// <summary>
    /// Each item's column after the last <see cref="Decode"/>, or -1.
    /// </summary>
    public int[] X { get; }

    /// <summary>
    /// Each item's row, or -1.
    /// </summary>
    public int[] Y { get; }

    /// <summary>
    /// How many items found a cell.
    /// </summary>
    public int Placed { get; private set; }

    public PackProblem Problem => _problem;

    /// <summary>
    /// The item on a cell, or -1.
    /// </summary>
    public int Owner(int x, int y) => _owner[y * _problem.Width + x];

    /// <summary>
    /// Whether a cell is free.
    /// </summary>
    public bool Free(int x, int y) => (_rows[y] >> x & 1) == 0;

    /// <summary>
    /// The target (rows kept free at the bottom) and the reserved columns for the next decodes.
    /// </summary>
    public void Target(int target, int bandFrom, int bandWidth) =>
        (_limit, _bandFrom, _bandWidth) = (_problem.Height - target, bandFrom, bandWidth);

    /// <summary>
    /// Lays the items out in <paramref name="order"/> on an empty grid.
    /// </summary>
    public void Decode(int[] order)
    {
        Array.Clear(_rows);
        Array.Fill(_owner, (short)-1);
        Array.Fill(X, -1);
        Array.Fill(Y, -1);
        Placed = 0;
        foreach (int item in order) Place(item);
    }

    /// <summary>
    /// Takes a finished layout as the grid (items without a cell stay off it).
    /// </summary>
    public void Load(int[] x, int[] y)
    {
        Array.Clear(_rows);
        Array.Fill(_owner, (short)-1);
        Placed = 0;
        for (int i = 0; i < x.Length; i++)
        {
            (X[i], Y[i]) = (-1, -1);
            if (x[i] >= 0) Put(i, _problem.Items[i], x[i], y[i]);
        }
    }

    void Place(int item)
    {
        var it = _problem.Items[item];
        bool found = _bandWidth > 0
            ? Find(it, banded: true, capAll: false, out int x, out int y)
            : Find(it, banded: false, capAll: true, out x, out y) || Find(it, banded: false, capAll: false, out x, out y);
        if (found) Put(item, it, x, y);
    }

    /// <summary>
    /// The topmost row with a free spot, and its best spot; <paramref name="capAll"/>: no spot may reach the kept rows, <paramref name="banded"/>:
    /// no spot over the reserved columns may.
    /// </summary>
    bool Find(PackItem it, bool banded, bool capAll, out int bestX, out int bestY)
    {
        (bestX, bestY) = (-1, -1);
        int width = _problem.Width, last = _problem.Height - it.Height;
        if (capAll) last = Math.Min(last, _limit - it.Height);

        for (int y = 0; y <= last; y++)
        {
            long bestKey = long.MaxValue;
            for (int x = 0; x + it.Width <= width; x++)
            {
                if (banded && y + it.Height > _limit && x + it.Width > _bandFrom && x < _bandFrom + _bandWidth) continue;
                if (!Fits(x, y, it.Width, it.Height)) continue;

                long key = (long)_wasteWeight * Waste(x, y, it.Width) - Contact(x, y, it);
                if (key < bestKey) (bestKey, bestX, bestY) = (key, x, y);
            }

            if (bestX >= 0) return true;
        }
        return false;
    }

    static ulong Mask(int x, int w) => (w >= 64 ? ulong.MaxValue : (1UL << w) - 1) << x;

    bool Fits(int x, int y, int w, int h)
    {
        ulong mask = Mask(x, w);
        for (int r = y; r < y + h; r++)
        {
            if ((_rows[r] & mask) != 0) return false;
        }
        return true;
    }

    /// <summary>
    /// The free cells directly above a spot, column by column up to a taken cell or the top.
    /// </summary>
    int Waste(int x, int y, int w)
    {
        int waste = 0;
        for (int c = x; c < x + w; c++)
        {
            for (int r = y - 1; r >= 0 && Free(c, r); r--) waste++;
        }
        return waste;
    }

    /// <summary>
    /// The edge cells a spot shares with items of the same record.
    /// </summary>
    int Contact(int x, int y, PackItem it)
    {
        int contact = 0;
        for (int c = x; c < x + it.Width; c++)
        {
            contact += Same(c, y - 1, it.Group) + Same(c, y + it.Height, it.Group);
        }
        for (int r = y; r < y + it.Height; r++)
        {
            contact += Same(x - 1, r, it.Group) + Same(x + it.Width, r, it.Group);
        }
        return contact;
    }

    int Same(int x, int y, int group)
    {
        if (x < 0 || y < 0 || x >= _problem.Width || y >= _problem.Height || group < 0) return 0;

        int owner = Owner(x, y);
        return owner >= 0 && _problem.Items[owner].Group == group ? 1 : 0;
    }

    void Put(int item, PackItem it, int x, int y)
    {
        ulong mask = Mask(x, it.Width);
        for (int r = y; r < y + it.Height; r++)
        {
            _rows[r] |= mask;
            for (int c = x; c < x + it.Width; c++) _owner[r * _problem.Width + c] = (short)item;
        }

        (X[item], Y[item]) = (x, y);
        Placed++;
    }
}
