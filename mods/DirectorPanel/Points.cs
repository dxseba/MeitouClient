using System.Globalization;
using System.Numerics;
using Meitou.Modding;

namespace Meitou.DirectorPanel;

/// <summary>A saved teleport point: its name (the original's list names each "Russian | English") and world position.</summary>
internal sealed record Point(string Name, Vector3 At);

/// <summary>
/// The saved points, kept as the original keeps them: <c>DirectorPanel_points.txt</c> in the mod's folder, one <c>name|x|y|z</c> line each (two
/// decimals, invariant culture). Without the file the installed original's list is read once (its 20 towns) and written to the port's own file on
/// the first change. Interface thread only.
/// </summary>
internal sealed class Points(ModContext context)
{
    readonly List<Point> _points = Load(context);

    public IReadOnlyList<Point> All => _points;

    public bool Add(string name, Vector3 at)
    {
        _points.Add(new Point(Clean(name), at));
        return Save();
    }

    public bool Rename(int index, string name)
    {
        if ((uint)index >= (uint)_points.Count) return false;

        _points[index] = _points[index] with { Name = Clean(name) };
        return Save();
    }

    public bool Erase(int index)
    {
        if ((uint)index >= (uint)_points.Count) return false;

        _points.RemoveAt(index);
        return Save();
    }

    /// <summary>The part of a "Russian | English" name in the panel's language; a name without the bar as it is.</summary>
    public static string Shown(Point point, bool russian)
    {
        int bar = point.Name.IndexOf(" | ", StringComparison.Ordinal);
        if (bar < 0) return point.Name;

        return russian ? point.Name[..bar].Trim() : point.Name[(bar + 3)..].Trim();
    }

    static string Clean(string name) => name.Replace('\n', ' ').Replace('\r', ' ').Trim();

    bool Save()
    {
        try
        {
            File.WriteAllLines(Path.Combine(context.Folder, OriginalFiles.PointsName), _points.Select(Line));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"points not saved: {e.Message}");
            return false;
        }
    }

    static string Line(Point p) =>
        string.Create(CultureInfo.InvariantCulture, $"{p.Name}|{p.At.X:0.00}|{p.At.Y:0.00}|{p.At.Z:0.00}");

    static List<Point> Load(ModContext context)
    {
        string own = Path.Combine(context.Folder, OriginalFiles.PointsName);
        string? path = File.Exists(own) ? own : OriginalFiles.Folder(context) is { } folder ? Path.Combine(folder, OriginalFiles.PointsName) : null;
        if (path is null || !File.Exists(path)) return [];

        try
        {
            return [.. File.ReadAllLines(path).Select(Parse).OfType<Point>()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"points not read: {e.Message}");
            return [];
        }
    }

    /// <summary>One line: the last three bar-separated fields are x, y, z; everything before them is the name (which may hold " | ").</summary>
    static Point? Parse(string line)
    {
        string[] parts = line.Split('|');
        if (parts.Length < 4) return null;

        int n = parts.Length;
        if (!Number(parts[n - 3], out float x) || !Number(parts[n - 2], out float y) || !Number(parts[n - 1], out float z)) return null;

        return new Point(string.Join('|', parts[..(n - 3)]).Trim(), new Vector3(x, y, z));
    }

    static bool Number(string text, out float value) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
