using System.Globalization;

namespace Meitou.Nitoryu;

/// <summary>
/// One of the original's curves, <c>"Path:value,Path:value"</c>: straight lines between the points, the first value below the first point and
/// the last above the last (the original's <c>[Balance]</c> format, README "Settings").
/// </summary>
sealed class Curve
{
    readonly (float X, float Y)[] _points;

    Curve((float X, float Y)[] points) => _points = points;

    /// <summary>A curve parsed from <paramref name="text"/>; <paramref name="fallback"/>'s when the text has no valid point.</summary>
    public static Curve Parse(string? text, string fallback)
    {
        var points = Points(text);
        if (points.Length == 0) points = Points(fallback);

        return new Curve(points);
    }

    static (float, float)[] Points(string? text)
    {
        var list = new List<(float, float)>();
        foreach (string pair in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = pair.Split(':');
            if (parts.Length != 2) continue;

            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                list.Add((x, y));
            }
        }

        return [.. list.OrderBy(p => p.Item1)];
    }

    /// <summary>The value at <paramref name="x"/>; 0 for an empty curve.</summary>
    public float At(float x)
    {
        if (_points.Length == 0) return 0;
        if (x <= _points[0].X) return _points[0].Y;

        for (int i = 1; i < _points.Length; i++)
        {
            var (x1, y1) = _points[i];
            if (x > x1) continue;

            var (x0, y0) = _points[i - 1];
            float t = x1 > x0 ? (x - x0) / (x1 - x0) : 1;
            return y0 + (y1 - y0) * t;
        }

        return _points[^1].Y;
    }
}
