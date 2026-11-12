// Adapted from integrated GPL-3.0-or-later source 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Rendering.Surface;

internal static partial class NativeSectionGeometry
{
    internal const float BrainSectionBand = 4;
    const int BrainPointBudget = 2048, BrainVertexBudget = 12000;
    sealed record BrainSection(Vertex[] Rim, List<(int A, int B, int C)> Triangles, Vector2[] Footprint);

    /// <summary>The fitted brain's actual section, with a thin cortex and lighter internal tissue; never the skull's whole footprint.</summary>
    static Vertex[]? BuildBrainSection(List<(List<Point> Points, float Area)> rims, in NativeSectionCut cut,
        Vector3 normal, Vector3 u, Vector3 v, uint bloodColour)
    {
        var kept = normal * cut.KeepSide;
        var outward = -kept;
        var sections = new List<BrainSection>();
        int remaining = BrainPointBudget;
        for (int contour = 0; contour < rims.Count; contour++)
        {
            var rim = BrainPolygon(rims[contour].Points, cut, normal, outward, contour);
            // Omit an over-budget island rather than bridge a sulcus by sampling across its true contour.
            if (rim.Length < 3 || rim.Length > remaining) continue;
            var triangles = SectionTriangles(rim, u, v);
            if (triangles.Count == 0) continue;
            remaining -= rim.Length;
            var footprint = rim.Select(p => new Vector2(Vector3.Dot(p.Position, u), Vector3.Dot(p.Position, v))).ToArray();
            sections.Add(new(rim, triangles, footprint));
        }
        var result = new List<Vertex>();
        int reserved = sections.Sum(section => section.Triangles.Count * 3);
        foreach (var section in sections)
        {
            reserved -= section.Triangles.Count * 3;
            AppendBrainTriangles(section, u, v, outward, bloodColour, BrainVertexBudget - reserved, result);
        }
        return result.Count == 0 ? null : result.ToArray();
    }

    static Vertex[] BrainPolygon(List<Point> rim, in NativeSectionCut cut, Vector3 normal, Vector3 outward, int contour)
    {
        var polygon = new List<Vertex>();
        for (int i = 0; i < rim.Count; i++)
        {
            var point = rim[i];
            var before = point.Position - rim[(i + rim.Count - 1) % rim.Count].Position;
            var after = rim[(i + 1) % rim.Count].Position - point.Position;
            if (Vector3.Cross(before, after).LengthSquared() < 1e-14f && Vector3.Dot(before, after) >= 0) continue;
            var p = point.Position + point.Morph;
            p -= normal * (Vector3.Dot(normal, p) + cut.Plane.W);
            p += normal * (cut.KeepSide * 0.05f);
            if (!float.IsFinite(p.LengthSquared())) return [];
            polygon.Add(new Vertex { Position = p, Normal = outward, Uv = new(BrainSectionBand, contour),
                Tangent = new(p, Layer), Bones = point.Bones, Weights = point.Weights });
        }
        return polygon.ToArray();
    }

    static void AppendBrainTriangles(BrainSection section, Vector3 u, Vector3 v, Vector3 outward,
        uint bloodColour, int limit, List<Vertex> result)
    {
        var pending = new Stack<(Vertex A, Vertex B, Vertex C, int Depth)>();
        foreach (var (a, b, c) in section.Triangles) pending.Push((section.Rim[a], section.Rim[b], section.Rim[c], 0));
        var colours = new Dictionary<Vector3, Vector4>();
        void Add(Vertex point)
        {
            if (!colours.TryGetValue(point.Position, out var colour))
                colours[point.Position] = colour = BrainSectionColour(point.Position, section.Footprint, u, v, bloodColour);
            result.Add(point with { Colour = colour });
        }
        while (pending.TryPop(out var triangle))
        {
            var (a, b, c, depth) = triangle;
            float edge = MathF.Max(Vector3.DistanceSquared(a.Position, b.Position),
                MathF.Max(Vector3.DistanceSquared(b.Position, c.Position), Vector3.DistanceSquared(c.Position, a.Position)));
            if (edge > 0.09f && depth < 5 && result.Count + (pending.Count + 4) * 3 <= limit)
            {
                var ab = BrainMid(a, b);
                var bc = BrainMid(b, c);
                var ca = BrainMid(c, a);
                pending.Push((a, ab, ca, depth + 1));
                pending.Push((ab, b, bc, depth + 1));
                pending.Push((ca, bc, c, depth + 1));
                pending.Push((ab, bc, ca, depth + 1));
                continue;
            }
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (!float.IsFinite(cross.LengthSquared()) || cross.LengthSquared() < 1e-12f) continue;
            if (Vector3.Dot(cross, outward) < 0) (b, c) = (c, b);
            Add(a);
            Add(b);
            Add(c);
        }
    }

    static Vertex BrainMid(Vertex a, Vertex b)
    {
        var point = Interpolate(new(a.Position, default, a.Bones, a.Weights), new(b.Position, default, b.Bones, b.Weights), 0.5f);
        return a with { Position = point.Position, Tangent = new(point.Position, Layer), Bones = point.Bones, Weights = point.Weights };
    }

    static Vector4 BrainSectionColour(Vector3 p, Vector2[] footprint, Vector3 u, Vector3 v, uint bloodColour)
    {
        var projected = new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v));
        float distance = float.PositiveInfinity;
        for (int edge = 0; edge < footprint.Length; edge++)
        {
            var a = footprint[edge];
            var step = footprint[(edge + 1) % footprint.Length] - a;
            float length = step.LengthSquared();
            float t = length > 1e-12f ? Math.Clamp(Vector2.Dot(projected - a, step) / length, 0, 1) : 0;
            distance = MathF.Min(distance, Vector2.Distance(projected, a + step * t));
        }
        float white = Math.Clamp((distance - 0.025f) / 0.13f, 0, 1);
        white = white * white * (3 - 2 * white);
        float grain = 0.96f + 0.04f * MathF.Sin(p.X * 33 + p.Y * 27 + p.Z * 41);
        var colour = Vector4.Lerp(new(0.47f, 0.29f, 0.30f, 0), new(0.70f, 0.56f, 0.52f, 0), white) * grain;
        return NativeFluidPalette.Recolour(colour, bloodColour);
    }
}
