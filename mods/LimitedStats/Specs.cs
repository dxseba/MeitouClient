namespace Meitou.LimitedStats;

/// <summary>
/// A number in an entry: a plain value, <c>randi(a, b)</c>, <c>randf(a, b)</c>, or their <c>_ignore</c> forms with values or ranges to skip
/// (<b>Verified</b>, original source; readme "Configuration Syntax").
/// </summary>
sealed record NumberExpr(NumberKind Kind, float A, float B, IReadOnlyList<(float Low, float High)> Ignore)
{
    const int IgnoreAttempts = 2000;

    public static NumberExpr Plain(float value) => new(NumberKind.Plain, value, 0, []);

    /// <summary>The number <paramref name="text"/> writes, else null (a log line names it).</summary>
    public static NumberExpr? Parse(string text)
    {
        string t = Ini.Trim(text);
        int open = t.IndexOf('(');
        if (open < 0) return Plain(Ini.Number(t));

        int close = t.LastIndexOf(')');
        if (close < open) return null;

        string name = Ini.Trim(t[..open]).ToLowerInvariant();
        var parts = Ini.SplitTop(t[(open + 1)..close], ',');
        return name switch
        {
            "randi" or "randf" when parts.Count >= 2 =>
                new(name == "randi" ? NumberKind.RandInt : NumberKind.RandFloat, Ini.Number(parts[0]), Ini.Number(parts[1]), []),
            "randi_ignore" or "randf_ignore" when parts.Count >= 3 => ParseIgnoring(name, parts),
            _ => null,
        };
    }

    static NumberExpr? ParseIgnoring(string name, List<string> parts)
    {
        string spec = string.Join(",", parts.Skip(2).Select(Ini.Trim));
        if (IgnoreRanges(spec) is not { } ranges) return null;

        var kind = name == "randi_ignore" ? NumberKind.RandIntIgnore : NumberKind.RandFloatIgnore;
        return new(kind, Ini.Number(parts[0]), Ini.Number(parts[1]), ranges);
    }

    /// <summary><c>60</c>, <c>[60, 70]</c>, <c>(60, 70)</c> or <c>[(60, 70), (80, 90)]</c> as ranges (a value is a range of one).</summary>
    static List<(float, float)>? IgnoreRanges(string spec)
    {
        string t = Ini.Trim(spec);
        if (t.Length == 0) return null;

        if (t[0] == '[') return ListRanges(t.Length >= 2 ? t[1..^1] : "");
        if (t[0] != '(')
        {
            float v = Ini.Number(t);
            return [(v, v)];
        }

        int close = t.LastIndexOf(')');
        var pair = close < 0 ? [] : Ini.SplitTop(t[1..close], ',');
        return pair.Count >= 2 ? [(Ini.Number(pair[0]), Ini.Number(pair[1]))] : null;
    }

    /// <summary>A bracket list: all values, or (when any item is a range) all ranges, items that are not ranges then skipped.</summary>
    static List<(float, float)> ListRanges(string inner)
    {
        var items = Ini.SplitTop(inner, ',').Select(Ini.Trim).Where(i => i.Length > 0).ToList();
        bool ranges = items.Any(i => i[0] == '(');
        var result = new List<(float, float)>();
        foreach (string item in items)
        {
            if (!ranges)
            {
                float v = Ini.Number(item);
                result.Add((v, v));
                continue;
            }

            int open = item.IndexOf('('), close = item.LastIndexOf(')');
            if (open < 0 || close < 0) continue;

            var pair = Ini.SplitTop(item[(open + 1)..close], ',');
            if (pair.Count >= 2) result.Add((Ini.Number(pair[0]), Ini.Number(pair[1])));
        }

        return result;
    }

    /// <summary>The value, rolling from <paramref name="roll"/> for the random kinds.</summary>
    public float Evaluate(TalentRoll roll)
    {
        if (Kind == NumberKind.Plain) return A;
        if (Kind is NumberKind.RandInt or NumberKind.RandFloat) return Draw(roll);

        for (int attempt = 0; attempt < IgnoreAttempts; attempt++)
        {
            if (IntKind && (int)B <= (int)A) return (int)A;

            float v = Draw(roll);
            if (!Ignore.Any(r => v >= MathF.Min(r.Low, r.High) && v <= MathF.Max(r.Low, r.High))) return v;
        }

        return A;
    }

    bool IntKind => Kind is NumberKind.RandInt or NumberKind.RandIntIgnore;

    float Draw(TalentRoll roll)
    {
        if (!IntKind) return A + roll.Unit() * (B - A);

        int low = (int)A, high = (int)B;
        return high <= low ? low : low + roll.Below(high - low + 1);
    }
}

enum NumberKind { Plain, RandInt, RandFloat, RandIntIgnore, RandFloatIgnore }

/// <summary>How <c>cap(race, x)</c>, <c>cap(race_max, x)</c> and <c>cap(race_min, x)</c> combine x with the race cap.</summary>
enum CapMode { None, RaceTimes, RaceMax, RaceMin }

/// <summary>One stat's functions: <c>buff(x)</c>, <c>cap(x)</c> or a race-relative cap, joined by <c>&amp;</c>.</summary>
sealed class StatFunctions
{
    public NumberExpr? Buff;
    public NumberExpr? Cap;
    public CapMode Mode;
    public NumberExpr? ModeValue;

    public static StatFunctions Parse(string text, Action<string> log)
    {
        var f = new StatFunctions();
        foreach (string raw in Ini.SplitTop(text, '&'))
        {
            string t = Ini.Trim(raw);
            if (t.Length > 0) f.ParseOne(t, log);
        }

        return f;
    }

    void ParseOne(string t, Action<string> log)
    {
        int open = t.IndexOf('('), close = t.LastIndexOf(')');
        if (open < 0 || close < open)
        {
            log($"bad func '{t}'");
            return;
        }

        string name = Ini.Trim(t[..open]).ToLowerInvariant(), content = t[(open + 1)..close];
        if (name == "buff") Buff = NumberExpr.Parse(content) ?? Bad(log, "buff", t);
        else if (name == "cap") ParseCap(content, t, log);
        else log($"unknown func '{name}'");
    }

    void ParseCap(string content, string t, Action<string> log)
    {
        string low = Ini.Trim(content).ToLowerInvariant();
        (CapMode mode, int skip) = low.StartsWith("race_max,", StringComparison.Ordinal) ? (CapMode.RaceMax, 9)
            : low.StartsWith("race_min,", StringComparison.Ordinal) ? (CapMode.RaceMin, 9)
            : low.StartsWith("race,", StringComparison.Ordinal) ? (CapMode.RaceTimes, 5)
            : (CapMode.None, 0);

        // The prefix is matched on the trimmed text but cut from the raw one, as the original does.
        string value = skip <= content.Length ? content[skip..] : "";
        if (mode == CapMode.None)
        {
            Cap = NumberExpr.Parse(content) ?? Bad(log, "cap", t);
            return;
        }

        if (NumberExpr.Parse(value) is { } parsed) (Mode, ModeValue) = (mode, parsed);
        else log($"bad cap '{t}'");
    }

    static NumberExpr? Bad(Action<string> log, string what, string t)
    {
        log($"bad {what} '{t}'");
        return null;
    }
}

/// <summary>
/// One <c>[Stats]</c> or <c>[SpecialCharacter]</c> entry: its stat groups (<c>all</c> or a stat name, with functions) and its
/// <c>[rand_other_skills...]</c> key modifier.
/// </summary>
sealed class TalentSpec
{
    public List<(string Stat, StatFunctions Functions)> Groups { get; } = [];
    public bool RandomOthers;
    public bool RandomOthersExcept;
    public List<TalentType> RandomTypes { get; } = [];
}

/// <summary>The talent types and their ini names (<b>Verified</b>, original source; <c>super</c> also names Superhuman).</summary>
enum TalentType { Normal, Lazy, Loser, Diligent, Genius, Superhuman }
