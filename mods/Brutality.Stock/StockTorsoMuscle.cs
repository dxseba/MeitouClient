using System.Numerics;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Rendering;
using Meitou.Rendering;

namespace Meitou.Brutality;

/// <summary>Fits a compact copy of the torso muscles below the morphed skin, only near the cut.</summary>
internal sealed class StockTorsoMuscle
{
    const float Clearance = .12f;
    readonly Dictionary<(int X, int Y, int Z), List<(Vector3 At, Vector3 Normal)>> skin = [];
    readonly Dictionary<Vector3, Vector3> fitted = [];

    internal StockTorsoMuscle(Vertex[] source, int[] groups)
    {
        var seen = new HashSet<Vector3>();
        foreach (var point in source)
        {
            if (MeshCut.Group(point, groups) != 0 || !seen.Add(point.Position)) continue;
            float length = point.Normal.LengthSquared();
            if (!float.IsFinite(length) || length < 1e-8f) continue;
            var cell = Cell(point.Position);
            if (!skin.TryGetValue(cell, out var sample)) skin.Add(cell, sample = []);
            sample.Add((point.Position, point.Normal / MathF.Sqrt(length)));
        }
    }

    static (int X, int Y, int Z) Cell(Vector3 p) => ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));

    internal Vector3 Fit(Vector3 position)
    {
        if (fitted.TryGetValue(position, out var known)) return known;
        var (x, y, z) = Cell(position);
        float best = float.PositiveInfinity;
        (Vector3 At, Vector3 Normal) nearest = default;
        for (int r = 1; r <= 2 && !float.IsFinite(best); r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r; dz <= r; dz++)
                        if (skin.TryGetValue((x + dx, y + dy, z + dz), out var cell))
                            foreach (var sample in cell)
                            {
                                float distance = Vector3.DistanceSquared(position, sample.At);
                                if (distance < best) (best, nearest) = (distance, sample);
                            }
        var inside = position;
        if (float.IsFinite(best))
        {
            float distance = Vector3.Dot(position - nearest.At, nearest.Normal);
            inside -= nearest.Normal * MathF.Max(0, distance + Clearance);
        }
        fitted.Add(position, inside);
        return inside;
    }

    internal static AnatomyBatch Compact(AnatomyBatch source)
    {
        // Collapse dense tessellation onto a shared grid. Keeping complete faces avoids the open
        // holes from dropping every nth source triangle. Cache this once, rather than on each cut.
        const float grid = .24f;
        var vertices = new List<AnatomyVertex>();
        var faces = new HashSet<(Vector3, Vector3, Vector3)>();
        for (int i = 0; i + 2 < source.Vertices.Length; i += 3)
        {
            var a = Snap(source.Vertices[i]); var b = Snap(source.Vertices[i + 1]); var c = Snap(source.Vertices[i + 2]);
            if (Vector3.Cross(b.Position - a.Position, c.Position - a.Position).LengthSquared() < 1e-8f) continue;
            // Cyclically rotate the face key so duplicates at a chart seam share the same key.
            var key = Before(b.Position, a.Position) && Before(b.Position, c.Position) ? (b.Position, c.Position, a.Position)
                : Before(c.Position, a.Position) ? (c.Position, a.Position, b.Position) : (a.Position, b.Position, c.Position);
            if (!faces.Add(key)) continue;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
        }
        return source with { Vertices = vertices.ToArray() };

        static bool Before(Vector3 a, Vector3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;
        static AnatomyVertex Snap(AnatomyVertex v) => v with
        {
            Position = new Vector3(MathF.Round(v.Position.X / grid), MathF.Round(v.Position.Y / grid), MathF.Round(v.Position.Z / grid)) * grid,
        };
    }
}
