using Meitou.Content;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;

namespace Meitou.Mentors;

/// <summary>
/// The original's trade table read at load time from the installed original's <c>Mentors.dll</c> (Workshop 3834394158, the port's required item):
/// its Workshop folder beside this one, else the mod named "Mentors" without code. Nothing of the original is shipped with the port or written out.
/// Layout (README "Trades"): a match list of 16-byte entries (ASCII lower-case text pointer, trade index) whose first part is compared whole and
/// the rest as substrings, the split being where the code's second <c>lea</c> points; and a trade array of 0x60-byte records (UTF-16 name and
/// covers text, up to 16 stat numbers ending in 0) whose index 0 is an empty sentinel.
/// </summary>
static class OriginalTrades
{
    const string OriginalItem = "3834394158";
    const string OriginalName = "Mentors";
    const string DllName = "Mentors.dll";

    const int MatchSize = 16;
    const int TradeSize = 0x60;
    const int StatsAt = 0x20;
    const int StatSlots = 16;
    const int MaxTrades = 64;
    const int MaxText = 80;

    static readonly HashSet<StatsEnumerated> Teachable = [.. Professions.All.Select(t => t.Stat)];

    /// <summary>The original's table, or null with one log line saying why the port's own trades are used.</summary>
    public static TradeTable? Load(ModContext context)
    {
        if (Locate(context) is not { } path)
        {
            context.Log($"the original {DllName} (Workshop {OriginalItem}) is not installed: using the port's own trades");
            return null;
        }

        try
        {
            var table = Read(path, out int exact, out int words);
            context.Log($"original trade table read: {exact} names, {words} words, {table.Count} trades");
            return table;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context.Log($"original trade table not read ({e.Message}): using the port's own trades");
            return null;
        }
    }

    /// <summary>Reads the table from the DLL at <paramref name="path"/>; throws <see cref="InvalidDataException"/> on anything unexpected.</summary>
    public static TradeTable Read(string path, out int exactCount, out int wordCount)
    {
        var pe = PeImage.Read(path);
        var data = pe.Section(".rdata") ?? throw new InvalidDataException("no .rdata section");
        var code = pe.Section(".text") ?? throw new InvalidDataException("no .text section");
        var leas = pe.LeaTargets(code);

        var (start, count) = MatchRun(pe, data);
        exactCount = Split(pe, data, start, count, leas);
        wordCount = count - exactCount;

        var matches = ReadMatches(pe, start, count);
        int tradeCount = matches.Max(m => m.Trade) + 1;
        var trades = ReadTrades(pe, TradeArray(pe, data, tradeCount, leas), tradeCount);

        return Build(trades, matches, exactCount);
    }

    static IEnumerable<string> Candidates(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent) yield return Path.Combine(parent, OriginalItem, DllName);

        if (Installed(context) is { } folder) yield return Path.Combine(folder, DllName);
    }

    static string? Locate(ModContext context) => Candidates(context).FirstOrDefault(File.Exists);

    static string? Installed(ModContext context)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(context.Folder);
            bool IsOriginal(ModInfo m) =>
                m.Name == OriginalName && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase);

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }

    /// <summary>The longest run of match entries in <paramref name="data"/> (file offset, entry count); it must be unique.</summary>
    static (int Start, int Count) MatchRun(PeImage pe, PeSection data)
    {
        var best = (Start: -1, Count: 0);
        bool tie = false;
        for (int o = Align(data.RawOffset); o + MatchSize <= data.RawEnd; o += 8)
        {
            if (!IsMatch(pe, o) || (o - MatchSize >= data.RawOffset && IsMatch(pe, o - MatchSize))) continue;

            int n = RunLength(pe, data, o, MatchSize, IsMatch);
            if (n == best.Count) tie = true;

            if (n > best.Count)
            {
                best = (o, n);
                tie = false;
            }
        }

        if (best.Count < 2 || tie) throw new InvalidDataException("no unique match list");
        return best;
    }

    static bool IsMatch(PeImage pe, int o)
    {
        uint trade = pe.U32(o + 8);
        if (trade is 0 or >= MaxTrades || pe.U32(o + 12) != 0) return false;

        string? text = pe.Ascii(pe.U64(o), MaxText);
        return !string.IsNullOrEmpty(text) && !text.Any(char.IsUpper);
    }

    /// <summary>How many exact names lead the match list: the one code-referenced entry inside the run starts the word list.</summary>
    static int Split(PeImage pe, PeSection data, int start, int count, HashSet<ulong> leas)
    {
        ulong first = pe.Va(data, start);
        if (!leas.Contains(first)) throw new InvalidDataException("match list not referenced by code");

        var inside = Enumerable.Range(1, count - 1).Where(i => leas.Contains(first + (ulong)(i * MatchSize))).ToList();
        if (inside.Count != 1) throw new InvalidDataException($"{inside.Count} code references inside the match list, expected 1");

        return inside[0];
    }

    static List<(string Text, int Trade)> ReadMatches(PeImage pe, int start, int count) =>
        [.. Enumerable.Range(0, count).Select(i => start + i * MatchSize).Select(o => (pe.Ascii(pe.U64(o), MaxText)!, (int)pe.U32(o + 8)))];

    /// <summary>The trade array's file offset: an empty sentinel then exactly <paramref name="count"/> - 1 trades, referenced by code; unique.</summary>
    static int TradeArray(PeImage pe, PeSection data, int count, HashSet<ulong> leas)
    {
        var found = new List<int>();
        for (int o = Align(data.RawOffset); o + TradeSize * count <= data.RawEnd; o += 8)
        {
            if (!leas.Contains(pe.Va(data, o)) || !IsSentinel(pe, o)) continue;
            if (RunLength(pe, data, o + TradeSize, TradeSize, IsTrade) == count - 1) found.Add(o);
        }

        if (found.Count != 1) throw new InvalidDataException($"{found.Count} trade arrays of {count} found, expected 1");
        return found[0];
    }

    static bool IsSentinel(PeImage pe, int o) =>
        pe.Utf16(pe.U64(o + 8), MaxText) == "" && pe.Utf16(pe.U64(o + 0x18), MaxText) == "" && Stats(pe, o) is [];

    static bool IsTrade(PeImage pe, int o) =>
        !string.IsNullOrEmpty(pe.Utf16(pe.U64(o + 8), MaxText)) && !string.IsNullOrEmpty(pe.Utf16(pe.U64(o + 0x18), MaxText))
        && Stats(pe, o) is [_, ..];

    /// <summary>The record's stats: teachable stat numbers ending at the first 0, zeros after it; null when any slot breaks that.</summary>
    static List<StatsEnumerated>? Stats(PeImage pe, int o)
    {
        var stats = new List<StatsEnumerated>();
        bool ended = false;
        for (int i = 0; i < StatSlots; i++)
        {
            uint value = pe.U32(o + StatsAt + 4 * i);
            if (value == 0)
            {
                ended = true;
                continue;
            }

            if (ended || value > byte.MaxValue || !Teachable.Contains((StatsEnumerated)value)) return null;
            stats.Add((StatsEnumerated)value);
        }

        return stats;
    }

    static List<Profession> ReadTrades(PeImage pe, int array, int count)
    {
        var trades = new List<Profession>();
        for (int i = 1; i < count; i++)
        {
            int o = array + i * TradeSize;
            trades.Add(new(pe.Utf16(pe.U64(o + 8), MaxText)!, pe.Utf16(pe.U64(o + 0x18), MaxText)!, Stats(pe, o)!));
        }

        return trades;
    }

    static TradeTable Build(List<Profession> trades, List<(string Text, int Trade)> matches, int exactCount)
    {
        var table = new TradeTable();
        foreach (var trade in trades) table.Add(trade);

        for (int i = 0; i < matches.Count; i++)
        {
            var trade = trades[matches[i].Trade - 1];
            if (i < exactCount) table.AddExact(matches[i].Text, trade);
            else table.AddWord(matches[i].Text, trade);
        }

        return table;
    }

    static int RunLength(PeImage pe, PeSection data, int o, int size, Func<PeImage, int, bool> valid)
    {
        int n = 0;
        while (o + size <= data.RawEnd && valid(pe, o))
        {
            n++;
            o += size;
        }

        return n;
    }

    static int Align(int offset) => (offset + 7) & ~7;
}
