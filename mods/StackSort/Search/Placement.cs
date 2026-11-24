namespace Meitou.StackSort.Search;

/// <summary>
/// One placed item: the item's index and the cells it covers. A layout is a list of these in placement order (the order the packer put
/// them down); several passes walk that order, so it is part of the result.
/// </summary>
readonly record struct Placement(int Item, int X, int Y, int W, int H)
{
    public int Right => X + W;

    public int Bottom => Y + H;

    /// <summary>
    /// The original's contact length of two rectangles (StackSort.md "Contact"): along a shared horizontal edge the overlap, plus one when it
    /// spans a whole side of either, but nothing for a single cell between two items both at least 3 wide; the same for a shared vertical edge.
    /// </summary>
    public static int Contact(in Placement a, in Placement b)
    {
        if (a.X > b.Right || b.X > a.Right || a.Y > b.Bottom || b.Y > a.Bottom) return 0;

        int contact = 0;
        int across = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        if (across > 0 && (a.Bottom == b.Y || b.Bottom == a.Y) && (across > 1 || a.W < 3 || b.W < 3))
        {
            contact = across == a.W || across == b.W ? across + 1 : across;
        }

        int down = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        if (down <= 0 || a.Right != b.X && b.Right != a.X) return contact;
        if (down < 2 && a.H > 2 && b.H > 2) return contact;

        contact += down;
        return down == a.H || down == b.H ? contact + 1 : contact;
    }
}

/// <summary>
/// Cell grids of a layout: which placement covers each cell (-1 free).
/// </summary>
static class Cells
{
    /// <summary>
    /// The placement index on each cell of a <paramref name="width"/> x <paramref name="height"/> grid, -1 where free.
    /// </summary>
    public static short[] Owners(IReadOnlyList<Placement> placements, int width, int height)
    {
        var owner = new short[width * height];
        Array.Fill(owner, (short)-1);
        for (int i = 0; i < placements.Count; i++) Mark(owner, width, placements[i], (short)i);
        return owner;
    }

    public static void Mark(short[] owner, int width, in Placement p, short value)
    {
        for (int r = p.Y; r < p.Bottom; r++)
        {
            Array.Fill(owner, value, r * width + p.X, p.W);
        }
    }

    /// <summary>
    /// Whether no placement covers a cell.
    /// </summary>
    public static bool Free(short[] owner, int cell) => owner[cell] < 0;
}
