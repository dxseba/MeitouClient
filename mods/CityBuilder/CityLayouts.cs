using System.Globalization;
using System.Numerics;

namespace Meitou.CityBuilder;

/// <summary>
/// One piece of a layout: a BUILDING record and its place in the building's frame.
/// </summary>
internal sealed record LayoutPiece(string Record, Vector3 Position, Quaternion Rotation);

/// <summary>
/// The original's layouts.txt (read from the installed original): <c>L|&lt;vanilla BUILDING&gt;|&lt;layout name&gt;|&lt;exterior 0/1&gt;</c>
/// lines, each
/// followed by its <c>F|&lt;furniture BUILDING&gt;|x|y|z|w|x|y|z</c> pieces (position in the building's frame, then the rotation as w, x, y, z).
/// </summary>
internal sealed class CityLayouts
{
    readonly Dictionary<(string Building, string Name, bool Exterior), List<LayoutPiece>> _layouts = [];

    public int Count => _layouts.Count;

    public static CityLayouts Read(string? path)
    {
        var layouts = new CityLayouts();
        if (path is null || !File.Exists(path)) return layouts;

        List<LayoutPiece>? current = null;
        foreach (string line in File.ReadLines(path))
        {
            var parts = line.Split('|');
            if (parts[0] == "L" && parts.Length >= 3)
            {
                current = [];
                layouts._layouts[(parts[1], parts[2], parts.Length > 3 && parts[3] == "1")] = current;
            }
            else if (parts[0] == "F" && parts.Length >= 9 && current is not null && Piece(parts) is { } piece)
            {
                current.Add(piece);
            }
        }

        return layouts;
    }

    static LayoutPiece? Piece(string[] p)
    {
        var f = new float[7];
        for (int i = 0; i < 7; i++)
        {
            if (!float.TryParse(p[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]) || !float.IsFinite(f[i])) return null;
        }

        var rotation = new Quaternion(f[4], f[5], f[6], f[3]);
        return rotation.LengthSquared() < 1e-6f ? null : new(p[1], new(f[0], f[1], f[2]), Quaternion.Normalize(rotation));
    }

    /// <summary>
    /// The pieces of layout <paramref name="name"/> of <paramref name="building"/> (interior, or an exterior's signs: the line's last field 1): an
    /// exact name, else one alike ignoring case, else one whose name holds it; none when the building has no such layout.
    /// </summary>
    public IReadOnlyList<LayoutPiece> Find(string building, string name, bool exterior = false)
    {
        if (name.Length == 0) return [];
        if (_layouts.TryGetValue((building, name, exterior), out var exact)) return exact;

        var own = _layouts.Where(l => l.Key.Building == building && l.Key.Exterior == exterior)
            .OrderBy(l => l.Key.Name, StringComparer.Ordinal).ToList();
        return own.FirstOrDefault(l => string.Equals(l.Key.Name, name, StringComparison.OrdinalIgnoreCase)).Value
            ?? own.FirstOrDefault(l => l.Key.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).Value
            ?? (IReadOnlyList<LayoutPiece>)[];
    }

    /// <summary>
    /// A piece's world place on a building at <paramref name="position"/> with <paramref name="rotation"/>.
    /// </summary>
    public static (Vector3 Position, Quaternion Rotation) Place(Vector3 position, Quaternion rotation, LayoutPiece piece) =>
        (position + Vector3.Transform(piece.Position, rotation), Quaternion.Normalize(rotation * piece.Rotation));
}
