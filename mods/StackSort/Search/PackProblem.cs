namespace Meitou.StackSort.Search;

/// <summary>
/// One item of a layout problem, as the original's packer sees it: its footprint, its group (one per item record, numbered by first
/// appearance), its record type (-1 for plain items), its item function (-1 for none) and its flags (1 food crop, 2 trade item).
/// </summary>
readonly record struct PackItem(int Width, int Height, int Group, int Type, int Function, int Flags);

/// <summary>
/// One grid to lay out: a pure copy of a live grid (its size, its items in list order, and each item's record and quantity for the
/// heuristic's ordering). The search never sees the inventory.
/// </summary>
sealed class PackProblem(int width, int height, PackItem[] items, string[] records, int[] quantities)
{
    /// <summary>
    /// Cells across.
    /// </summary>
    public int Width { get; } = width;

    /// <summary>
    /// Cells down.
    /// </summary>
    public int Height { get; } = height;

    /// <summary>
    /// The items in the grid's list order.
    /// </summary>
    public PackItem[] Items { get; } = items;

    /// <summary>
    /// Each item's record id (the heuristic groups and orders by it).
    /// </summary>
    public string[] Records { get; } = records;

    /// <summary>
    /// Each item's units (the heuristic's last ordering key).
    /// </summary>
    public int[] Quantities { get; } = quantities;

    /// <summary>
    /// The cells no item covers.
    /// </summary>
    public int FreeArea => Width * Height - Items.Sum(i => i.Width * i.Height);

    /// <summary>
    /// Whether the search can run: as the original's check, 1-126 cells a side and fewer than 256 items (a cell stores a placement in a byte).
    /// </summary>
    public bool Searchable =>
        Width is > 0 and < 127 && Height is > 0 and < 127 && Items.Length < 256 && Items.All(i => i.Width > 0 && i.Height > 0);

    /// <summary>
    /// The same problem turned on its diagonal: the Width goal is the Height goal of this (the original's <c>PackAnnealed</c> swaps every
    /// width and height for W, searches, and swaps the answer back).
    /// </summary>
    public PackProblem Transposed() =>
        new(Height, Width, [.. Items.Select(i => i with { Width = i.Height, Height = i.Width })], Records, Quantities);

    /// <summary>
    /// Everything the search reads, as text: the cache key of a precomputed result.
    /// </summary>
    public string Key()
    {
        var parts = Items.Select((i, n) => $"{i.Width}x{i.Height}g{i.Group}t{i.Type}f{i.Function}l{i.Flags}r{Records[n]}q{Quantities[n]}");
        return $"{Width}x{Height}|" + string.Join(";", parts);
    }
}

/// <summary>
/// A grid's layout: each item's cell (-1 when it found none).
/// </summary>
sealed class PackLayout(int[] x, int[] y)
{
    /// <summary>
    /// Each item's column, or -1.
    /// </summary>
    public int[] X { get; } = x;

    /// <summary>
    /// Each item's row, or -1.
    /// </summary>
    public int[] Y { get; } = y;

    /// <summary>
    /// Whether every item has a cell.
    /// </summary>
    public bool Complete => X.All(v => v >= 0);

    /// <summary>
    /// The layout turned back on its diagonal (see <see cref="PackProblem.Transposed"/>).
    /// </summary>
    public PackLayout Transposed() => new(Y, X);

    /// <summary>
    /// The layout's cells as text, to tell layouts apart.
    /// </summary>
    public string Key() => string.Join(";", X.Select((v, i) => $"{v},{Y[i]}"));
}
