using System.Numerics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public partial class TorsoCutGeometryTests
{
    [Fact]
    public void Internal_backing_tracks_a_changing_opposite_skin_depth_and_preserves_the_shallow_rim()
    {
        static float Back(float x) => 0.05f + 0.25f * (x + 1);
        var source = Box(new(-1, -1, -1), new(1, 1, 1)).Select(v => v with
        {
            Position = new(v.Position.X, v.Position.Y, v.Position.Z > 0 ? Back(v.Position.X) : -0.3f),
        }).ToArray();
        var cap = Assert.IsType<Vertex[]>(TorsoCutSurface.Build(source, [0], new(new(0, 0, 1, 0), 1, true)));
        var floor = cap.Where(v => v.Uv.X == TorsoCutSurface.BackingBand).ToArray();
        Assert.NotEmpty(floor);
        Assert.Contains(floor, v => v.Position.Z > 0.30f);
        Assert.Contains(floor, v => v.Position.Z < 0.15f);
        Assert.All(floor, v => Assert.InRange(v.Position.Z, 0.008f, Back(v.Position.X) * 0.84f));
        var lip = cap.Where(v => v.Uv.X == 2).ToArray();
        Assert.NotEmpty(lip);
        Assert.All(lip, v => Assert.InRange(v.Position.Z, 0.008f, 0.201f));
        CheckSectionBacking(cap, source, new(0, 0, 1, 0), 1, "variable-thickness section");
    }

    static void CheckSectionBacking(Vertex[] cap, Vertex[] source, Vector4 plane, int side, string sample)
    {
        var normal = Vector3.Normalize(new(plane.X, plane.Y, plane.Z));
        var axis = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, normal));
        var v = Vector3.Cross(normal, u);
        Vector2 Project(Vector3 p) => new(Vector3.Dot(p, u), Vector3.Dot(p, v));
        var outlines = new Dictionary<float, List<(Vector2 A, Vector2 B)>>();
        int backingCount = 0;
        for (int i = 0; i < cap.Length; i += 3)
        {
            var outer = cap[i..(i + 3)].Where(p => p.Uv.X < 0.5f).ToArray();
            if (outer.Length == 2)
            {
                if (!outlines.TryGetValue(outer[0].Uv.Y, out var edges)) outlines[outer[0].Uv.Y] = edges = [];
                edges.Add((Project(outer[0].Position), Project(outer[1].Position)));
            }
            if (cap[i].Uv.X >= TorsoCutSurface.BackingBand && cap[i + 1].Uv.X >= TorsoCutSurface.BackingBand && cap[i + 2].Uv.X >= TorsoCutSurface.BackingBand) backingCount++;
        }
        Assert.True(backingCount > 0, sample + " has no internal backing");
        var checkedPoints = new HashSet<(float Contour, Vector2 Point)>();
        string pointDetail = string.Empty;
        void Contained(Vector3 position, float contour)
        {
            var point = Project(position);
            if (!checkedPoints.Add((contour, point))) return;
            Assert.True(outlines.TryGetValue(contour, out var edges), sample + " lost the backing contour");
            bool inside = false;
            foreach (var (a, b) in edges!)
            {
                var step = b - a;
                float length = step.LengthSquared();
                float t = length < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - a, step) / length, 0, 1);
                if (Vector2.DistanceSquared(point, a + step * t) <= 0.05f * 0.05f) return;
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            Assert.True(inside, sample + $" backing crosses its projected section outline at {point}, contour {contour}, edges {edges!.Count}; {pointDetail}");
        }
        int at = 0, stride = Math.Max(1, backingCount / 16);
        for (int i = 0; i < cap.Length; i += 3)
        {
            var a = cap[i]; var b = cap[i + 1]; var c = cap[i + 2];
            if (a.Uv.X < TorsoCutSurface.BackingBand || b.Uv.X < TorsoCutSurface.BackingBand || c.Uv.X < TorsoCutSurface.BackingBand) continue;
            Assert.Equal(a.Uv.Y, b.Uv.Y); Assert.Equal(a.Uv.Y, c.Uv.Y);
            pointDetail = $"triangle {i / 3}, colour {a.Colour}, a {a.Position}, b {b.Position}, c {c.Position}";
            foreach (var p in new[] { a.Position, b.Position, c.Position, (a.Position + b.Position) * 0.5f,
                (b.Position + c.Position) * 0.5f, (c.Position + a.Position) * 0.5f, (a.Position + b.Position + c.Position) / 3 })
            {
                Contained(p, a.Uv.Y);
                float depth = (Vector3.Dot(normal, p) + plane.W) * side;
                Assert.InRange(depth, -0.001f, TorsoCutSurface.ExposureDepth + 0.351f);
            }
            if (at++ % stride != 0) continue;
            var centre = (a.Position + b.Position + c.Position) / 3;
            float distance = (Vector3.Dot(normal, centre) + plane.W) * side;
            var start = centre - normal * (Vector3.Dot(normal, centre) + plane.W) + normal * (side * 0.001f);
            float exit = SkinExit(start, normal * side, source);
            if (float.IsFinite(exit)) Assert.True(distance <= exit + 0.03f, sample + $" backing penetrates the opposite skin ({distance} > {exit})");
        }
    }

    static float SkinExit(Vector3 origin, Vector3 direction, Vertex[] source)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            var e1 = source[i + 1].Position - source[i].Position;
            var e2 = source[i + 2].Position - source[i].Position;
            var cross = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, cross);
            if (MathF.Abs(det) < 1e-8f) continue;
            var from = origin - source[i].Position;
            float x = Vector3.Dot(from, cross) / det;
            var q = Vector3.Cross(from, e1);
            float y = Vector3.Dot(direction, q) / det;
            if (x is < -1e-5f or > 1.00001f || y < -1e-5f || x + y > 1.00001f) continue;
            float t = Vector3.Dot(e2, q) / det;
            if (t > 0.001f) best = MathF.Min(best, t);
        }
        return best;
    }
}
