using System.Globalization;

namespace Meitou.GearCompareAlt;

/// <summary>One property that differs: its label, both values as printed, the change, and whether it got better (1), worse (-1) or neither.</summary>
readonly record struct Difference(string Label, string Worn, string New, string Change, int Side);

/// <summary>
/// The comparison of a new item with a worn one (README "Facts", the original's compare step): every property whose values differ by at least
/// 0.0001, a property one item lacks read as 1 for a multiplier and 0 otherwise; better and worse counted over the rows with a direction; the main
/// axis in percent of the worn item's; the weight added; the verdict.
/// </summary>
sealed record Comparison(IReadOnlyList<Difference> Rows, int Better, int Worse, float MainPercent, float WeightChange, string MainLabel)
{
    const float Epsilon = 0.0001f;

    /// <summary>The verdict (the original's rule): -1 worse, 1 better, 2 mixed, 0 equal.</summary>
    public int Verdict
    {
        get
        {
            int d = Better - Worse;
            if (MainPercent > 2 && d >= 0) return 1;
            if (MainPercent < -2 && d < 1) return -1;
            if (MainPercent > 2 || MainPercent < -2) return 2;
            if (d > 0) return 1;
            if (d < 0) return -1;
            return WeightChange > 0.5f ? -1 : WeightChange >= -0.5f ? 0 : 1;
        }
    }

    /// <summary>The total of <c>ShowTotal</c>: the main axis, plus <paramref name="perProperty"/> per better row, minus as much per worse row and
    /// <paramref name="perKg"/> per added kilogram.</summary>
    public float Total(float perProperty, float perKg) => (Better - Worse) * perProperty + MainPercent - perKg * WeightChange;

    /// <summary>Compares <paramref name="fresh"/> with <paramref name="worn"/> (null: nothing worn).</summary>
    public static Comparison Of(GearStats? worn, GearStats fresh, Func<string, string> tr)
    {
        var rows = new List<Difference>();
        int better = 0, worse = 0;
        foreach (var key in Keys(worn, fresh))
        {
            var a = Find(worn, key);
            var b = Find(fresh, key);
            var p = b ?? a!.Value;
            float neutral = p.Format == Fmt.Mult ? 1 : 0;
            float was = a?.Value ?? neutral, now = b?.Value ?? neutral;
            if (MathF.Abs(now - was) < Epsilon) continue;

            // The weight row takes a colour but is not counted (the original counts no row of the weight kind).
            int side = p.Dir == 0 ? 0 : p.Dir * (now - was) > 0 ? 1 : -1;
            bool counted = p.Format != Fmt.Kg;
            if (counted && side > 0) better++;
            if (counted && side < 0) worse++;
            rows.Add(new(tr(p.Label) + p.Suffix, a is null ? "-" : Text(p.Format, was), b is null ? "-" : Text(p.Format, now),
                Change(p.Format, was, now), side));
        }

        float wornMain = worn?.Main ?? 0;
        float percent = wornMain > Epsilon ? (fresh.Main - wornMain) / wornMain * 100 : fresh.Main > Epsilon ? 100 : 0;
        return new Comparison(rows, better, worse, percent, fresh.Weight - (worn?.Weight ?? 0), fresh.MainLabel);
    }

    /// <summary>The properties of both, by label and suffix, in the new item's order then the worn one's.</summary>
    static IEnumerable<(string, string)> Keys(GearStats? worn, GearStats fresh) =>
        fresh.Props.Concat(worn?.Props ?? []).Select(p => (p.Label, p.Suffix)).Distinct();

    static Prop? Find(GearStats? stats, (string Label, string Suffix) key)
    {
        if (stats is null) return null;

        foreach (var p in stats.Props)
        {
            if (p.Label == key.Label && p.Suffix == key.Suffix) return p;
        }

        return null;
    }

    static string Invariant(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>A value as the original prints it by its kind.</summary>
    public static string Text(Fmt format, float v) => format switch
    {
        Fmt.Percent => Invariant(MathF.Round(v * 100), "0") + "%",
        Fmt.Mult => "x" + Invariant(v, "0.00"),
        Fmt.Signed => Invariant(MathF.Round(v), "+0;-0;0"),
        Fmt.Kg => Invariant(v, "0.0"),
        Fmt.Int => Invariant(MathF.Round(v), "0"),
        _ => Invariant(v, "0.00"),
    };

    /// <summary>The change, new minus worn, signed: in whole points for a percentage, in percent for a multiplier.</summary>
    public static string Change(Fmt format, float was, float now) => format switch
    {
        Fmt.Percent => Invariant(MathF.Round(now * 100) - MathF.Round(was * 100), "+0;-0;0"),
        Fmt.Mult => Invariant(MathF.Round(now * 100) - MathF.Round(was * 100), "+0;-0;0") + "%",
        Fmt.Signed or Fmt.Int => Invariant(MathF.Round(now) - MathF.Round(was), "+0;-0;0"),
        Fmt.Kg => Invariant(now - was, "+0.0;-0.0;0.0"),
        _ => Invariant(now - was, "+0.00;-0.00;0.00"),
    };
}
