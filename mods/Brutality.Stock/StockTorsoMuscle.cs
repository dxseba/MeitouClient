using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Rendering;

namespace Meitou.Brutality;

/// <summary>Fits the exposed anatomy to the morphed torso and binds it to nearby skin.</summary>
internal sealed class StockTorsoMuscle
{
    const float Clearance = .22f, BandHeight = .25f;
    readonly Dictionary<(int X, int Y, int Z), List<SkinSample>> skin = [];
    readonly Dictionary<int, (Vector2 Min, Vector2 Max)> sections = [];
    readonly Dictionary<Vector3, Fitted?> fitted = [];

    readonly record struct SkinSample(Vector3 At, Vector3 Normal, uint Bones, Vector4 Weights);
    internal readonly record struct Fitted(Vector3 Position, uint Bones, Vector4 Weights);

    internal StockTorsoMuscle(Vertex[] source, int[] groups)
    {
        var seen = new HashSet<Vector3>();
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            var a = source[i]; var b = source[i + 1]; var c = source[i + 2];
            bool torsoA = MeshCut.Group(a, groups) == 0, torsoB = MeshCut.Group(b, groups) == 0;
            bool torsoC = MeshCut.Group(c, groups) == 0;
            if (torsoA) AddSample(a);
            if (torsoB) AddSample(b);
            if (torsoC) AddSample(c);
            if (torsoA && torsoB) AddEdge(a.Position, b.Position);
            if (torsoB && torsoC) AddEdge(b.Position, c.Position);
            if (torsoC && torsoA) AddEdge(c.Position, a.Position);
        }

        void AddSample(Vertex point)
        {
            if (!seen.Add(point.Position)) return;
            float length = point.Normal.LengthSquared();
            if (!float.IsFinite(length) || length < 1e-8f) return;
            var cell = Cell(point.Position);
            if (!skin.TryGetValue(cell, out var samples)) skin.Add(cell, samples = []);
            samples.Add(new(point.Position, point.Normal / MathF.Sqrt(length), point.Bones, point.Weights));
        }

        void AddEdge(Vector3 a, Vector3 b)
        {
            if (a.Y > b.Y) (a, b) = (b, a);
            if (b.Y - a.Y < .0001f) return;
            int first = (int)MathF.Ceiling(a.Y / BandHeight), last = (int)MathF.Floor(b.Y / BandHeight);
            for (int band = first; band <= last; band++)
            {
                var p = Vector3.Lerp(a, b, (band * BandHeight - a.Y) / (b.Y - a.Y));
                var point = new Vector2(p.X, p.Z);
                if (sections.TryGetValue(band, out var bounds))
                    sections[band] = (Vector2.Min(bounds.Min, point), Vector2.Max(bounds.Max, point));
                else sections.Add(band, (point, point));
            }
        }
    }

    static (int X, int Y, int Z) Cell(Vector3 p) => ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));

    internal Fitted? Fit(Vector3 position)
    {
        if (fitted.TryGetValue(position, out var known)) return known;
        Vector3 inside = LimitGirth(position);
        var (x, y, z) = Cell(inside);
        float best = float.PositiveInfinity;
        SkinSample nearest = default;
        for (int r = 1; r <= 2 && !float.IsFinite(best); r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r; dz <= r; dz++)
                        if (skin.TryGetValue((x + dx, y + dy, z + dz), out var cell))
                            foreach (var sample in cell)
                            {
                                float distance = Vector3.DistanceSquared(inside, sample.At);
                                if (distance < best) (best, nearest) = (distance, sample);
                            }
        if (!float.IsFinite(best)) return fitted[position] = null;
        float signed = Vector3.Dot(inside - nearest.At, nearest.Normal);
        inside -= nearest.Normal * MathF.Max(0, signed + Clearance);
        // The same skin weights are restricted to the retained side when the piece is built.
        return fitted[position] = new(inside, nearest.Bones, nearest.Weights);
    }

    Vector3 LimitGirth(Vector3 p)
    {
        float slice = p.Y / BandHeight;
        int band = (int)MathF.Floor(slice);
        bool lower = sections.TryGetValue(band, out var a), upper = sections.TryGetValue(band + 1, out var b);
        if (!lower && !upper) return p;
        if (!lower) a = b;
        if (!upper) b = a;
        float t = slice - band;
        var min = Vector2.Lerp(a.Min, b.Min, t);
        var max = Vector2.Lerp(a.Max, b.Max, t);
        var radius = (max - min) * .5f;
        if (radius.X < Clearance * 1.5f || radius.Y < Clearance * 1.5f) return p;
        var middle = (min + max) * .5f;
        var offset = new Vector2(p.X, p.Z) - middle;
        float length = (offset / (radius - new Vector2(Clearance))).Length();
        if (length <= 1) return p;
        var at = middle + offset / length;
        return new(at.X, p.Y, at.Y);
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
