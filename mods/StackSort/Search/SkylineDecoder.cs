namespace Meitou.StackSort.Search;

/// <summary>
/// The search's decoder (StackSort.md "Decoder"): turns an item order into a layout with a skyline (the first free row of each column span,
/// items rest against the top) and a list of wasted rectangles (free cells shut in above an item). Each item first takes the smallest wasted
/// rectangle it fits, at its top left; otherwise the skyline spot that rests highest, then wastes least against its same-record contact, then
/// lies leftmost. A target keeps rows free at the bottom: over the reserved block no item may reach them; with no block an item reaches them
/// only when it fits nowhere above. One decoder per search; it owns its state.
/// </summary>
sealed class SkylineDecoder
{
    readonly PackProblem _problem;
    readonly int _width, _height, _wasteWeight;
    readonly List<(int X, int Y, int W)> _skyline = [];
    readonly List<(int X, int Y, int W)> _scratch = [];
    readonly List<(int X, int Y, int W, int H)> _waste = [];
    readonly int[] _groupCount;
    readonly int[] _neighbours = new int[16];
    int _ceiling, _blockFrom, _blockWidth;

    public SkylineDecoder(PackProblem problem, int wasteWeight)
    {
        (_problem, _width, _height, _wasteWeight) = (problem, problem.Width, problem.Height, wasteWeight);
        Owner = new short[_width * _height];
        _groupCount = new int[problem.Items.Length + 1];
    }

    /// <summary>
    /// The layout of the last <see cref="Decode"/>, in placement order.
    /// </summary>
    public List<Placement> Placements { get; } = [];

    /// <summary>
    /// The placement index on each cell, -1 where free.
    /// </summary>
    public short[] Owner { get; }

    /// <summary>
    /// The target (rows kept free at the bottom) and the reserved block's columns (width 0: none) for the next decodes.
    /// </summary>
    public void Target(int target, int blockFrom, int blockWidth) => (_ceiling, _blockFrom, _blockWidth) = (_height - target, blockFrom, blockWidth);

    /// <summary>
    /// Lays the items out in <paramref name="order"/> on an empty grid (an item that fits nowhere stays out).
    /// </summary>
    public void Decode(int[] order)
    {
        Reset();
        foreach (int item in order)
        {
            if (_waste.Count == 0 || !IntoWaste(item)) OntoSkyline(item);
        }
    }

    void Reset()
    {
        Placements.Clear();
        _waste.Clear();
        _skyline.Clear();
        _skyline.Add((0, 0, _width));
        Array.Fill(Owner, (short)-1);
        Array.Clear(_groupCount);
    }

    /// <summary>
    /// The smallest wasted rectangle the item fits (the first of equal areas): the item takes its top left, the rest of it splits into the part
    /// right of the item (full height) and the part below it (item width).
    /// </summary>
    bool IntoWaste(int item)
    {
        var it = _problem.Items[item];
        int best = -1, bestArea = int.MaxValue;
        for (int i = 0; i < _waste.Count; i++)
        {
            var r = _waste[i];
            if (it.Width <= r.W && it.Height <= r.H && r.W * r.H < bestArea) (best, bestArea) = (i, r.W * r.H);
        }

        if (best < 0) return false;

        var hole = _waste[best];
        _waste.RemoveAt(best);
        Put(item, hole.X, hole.Y);
        if (hole.W > it.Width) _waste.Add((hole.X + it.Width, hole.Y, hole.W - it.Width, hole.H));
        if (hole.H > it.Height) _waste.Add((hole.X, hole.Y + it.Height, it.Width, hole.H - it.Height));
        return true;
    }

    /// <summary>
    /// With no reserved block, a first phase below the kept rows and a second without them; with one, a single phase.
    /// </summary>
    void OntoSkyline(int item)
    {
        int phases = _blockWidth > 0 ? 1 : 2;
        for (int phase = 0; phase < phases; phase++)
        {
            var (x, y) = BestSpot(item, phase);
            if (y < 0) continue;

            Settle(item, x, y);
            return;
        }
    }

    /// <summary>
    /// Every skyline segment's left end is a spot; the highest resting one wins, then the least 3 x waste - contact, then the leftmost.
    /// </summary>
    (int X, int Y) BestSpot(int item, int phase)
    {
        var it = _problem.Items[item];
        int bestX = -1, bestY = int.MaxValue;
        long bestKey = long.MaxValue;
        for (int s = 0; s < _skyline.Count; s++)
        {
            int x = _skyline[s].X;
            if (x + it.Width > _width) continue;

            int maxY = Math.Min(Ceiling(x, it.Width, phase) - it.Height, bestY);
            if (maxY < 0) continue;

            var (y, waste) = Rest(s, x, it.Width, maxY);
            if (y < 0) continue;

            long wasted = (long)_wasteWeight * waste;
            if (y == bestY && wasted - (2L * (it.Height + it.Width) + 16) > bestKey) continue;

            long key = wasted - Contact(it, x, y);
            if (y < bestY || key < bestKey) (bestX, bestY, bestKey) = (x, y, key);
        }
        return bestX < 0 ? (-1, -1) : (bestX, bestY);
    }

    /// <summary>
    /// The lowest row an item may reach at <paramref name="x"/> (exclusive): the kept rows' top in the first phase without a block, or over the
    /// block; else the grid's bottom.
    /// </summary>
    int Ceiling(int x, int w, int phase)
    {
        if (_blockWidth < 1) return phase == 0 ? _ceiling : _height;

        return x + w > _blockFrom && x < _blockFrom + _blockWidth ? _ceiling : _height;
    }

    /// <summary>
    /// Where an item starting at segment <paramref name="s"/> rests (the deepest skyline row under it) and the free cells it shuts in above it;
    /// Y -1 when it would rest below <paramref name="maxY"/>.
    /// </summary>
    (int Y, int Waste) Rest(int s, int x, int w, int maxY)
    {
        int y = 0, covered = 0;
        for (int k = s; k < _skyline.Count; k++)
        {
            var seg = _skyline[k];
            if (y < seg.Y)
            {
                if (maxY < seg.Y) return (-1, 0);

                y = seg.Y;
            }

            int end = Math.Min(seg.X + seg.W, x + w);
            covered += (end - seg.X) * seg.Y;
            if (end >= x + w) return (y, y * w - covered);
        }
        return (-1, 0);
    }

    /// <summary>
    /// The contact of a spot with the placed items of the same record (up to 16 of them, met along the left, right, top, then bottom side).
    /// </summary>
    int Contact(PackItem it, int x, int y)
    {
        if (it.Group < 0 || _groupCount[it.Group] == 0) return 0;

        int found = 0;
        if (x > 0) found = Column(x - 1, y, it.Height, it.Group, found);
        if (x + it.Width < _width) found = Column(x + it.Width, y, it.Height, it.Group, found);
        if (y > 0) found = Row(y - 1, x, it.Width, it.Group, found);
        if (y + it.Height < _height) found = Row(y + it.Height, x, it.Width, it.Group, found);

        var spot = new Placement(-1, x, y, it.Width, it.Height);
        int contact = 0;
        for (int i = 0; i < found; i++) contact += Placement.Contact(spot, Placements[_neighbours[i]]);
        return contact;
    }

    int Column(int x, int y, int h, int group, int found)
    {
        for (int r = y; r < y + h; r++) found = Note(Owner[r * _width + x], group, found);
        return found;
    }

    int Row(int y, int x, int w, int group, int found)
    {
        for (int c = x; c < x + w; c++) found = Note(Owner[y * _width + c], group, found);
        return found;
    }

    int Note(short owner, int group, int found)
    {
        if (owner < 0 || _problem.Items[Placements[owner].Item].Group != group) return found;
        if (Array.IndexOf(_neighbours, owner, 0, found) >= 0 || found >= _neighbours.Length) return found;

        _neighbours[found] = owner;
        return found + 1;
    }

    /// <summary>
    /// Places an item on the skyline: the gaps over each segment it covers become wasted rectangles, then the skyline takes its bottom.
    /// </summary>
    void Settle(int item, int x, int y)
    {
        var it = _problem.Items[item];
        Put(item, x, y);
        foreach (var seg in _skyline)
        {
            int from = Math.Max(seg.X, x), to = Math.Min(seg.X + seg.W, x + it.Width);
            if (from < to && y > seg.Y) _waste.Add((from, seg.Y, to - from, y - seg.Y));
        }
        Raise(x, it.Width, y + it.Height);
    }

    /// <summary>
    /// The skyline over columns [x, x + w) becomes row <paramref name="top"/>, merged with a neighbour segment of the same row.
    /// </summary>
    void Raise(int x, int w, int top)
    {
        int at = Cut(x, x + w);
        _skyline.Insert(at, (x, top, w));
        if (at + 1 < _skyline.Count && _skyline[at + 1].Y == top) Join(at);
        if (at > 0 && _skyline[at - 1].Y == top) Join(at - 1);
    }

    /// <summary>
    /// Removes columns [from, to) from the skyline (trimming or splitting the segments there); returns where a segment for them goes.
    /// </summary>
    int Cut(int from, int to)
    {
        _scratch.Clear();
        foreach (var seg in _skyline)
        {
            int end = seg.X + seg.W;
            if (end <= from || seg.X >= to)
            {
                _scratch.Add(seg);
                continue;
            }

            if (seg.X < from) _scratch.Add((seg.X, seg.Y, from - seg.X));
            if (end > to) _scratch.Add((to, seg.Y, end - to));
        }

        _skyline.Clear();
        _skyline.AddRange(_scratch);
        int at = 0;
        while (at < _skyline.Count && _skyline[at].X < from) at++;
        return at;
    }

    void Join(int k)
    {
        var (a, b) = (_skyline[k], _skyline[k + 1]);
        _skyline[k] = (a.X, a.Y, a.W + b.W);
        _skyline.RemoveAt(k + 1);
    }

    void Put(int item, int x, int y)
    {
        var it = _problem.Items[item];
        var placement = new Placement(item, x, y, it.Width, it.Height);
        Cells.Mark(Owner, _width, placement, (short)Placements.Count);
        Placements.Add(placement);
        if (it.Group >= 0) _groupCount[it.Group]++;
    }
}
