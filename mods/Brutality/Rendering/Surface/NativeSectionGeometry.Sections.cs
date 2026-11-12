// Adapted from integrated GPL-3.0-or-later source 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Rendering.Surface;

internal static partial class NativeSectionGeometry
{
    // Stay behind the installed anatomy's exposure window, but inside the opposite skin of a thin arm/chest slab.
    static float[] SectionDepths(List<Point> rim, Vector3[] inset, Vector3 kept, SectionSkin skin, float backingDepth)
    {
        var result = new float[rim.Count];
        for (int i = 0; i < rim.Count; i++)
        {
            var origin = rim[i].Position + rim[i].Morph + inset[i] + kept * 0.001f;
            result[i] = SectionDepth(origin, kept, skin, backingDepth);
        }
        return result;
    }

    static float SectionCross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // Ear clipping retains an actual concave cut outline; a centre fan would bridge the opening of a U-shaped strip.
    static List<(int A, int B, int C)> SectionTriangles(Vertex[] polygon, Vector3 u, Vector3 v)
    {
        var points = polygon.Select(p => new Vector2(Vector3.Dot(p.Position, u), Vector3.Dot(p.Position, v))).ToArray();
        var pending = Enumerable.Range(0, points.Length).ToList();
        float area = 0;
        for (int i = 0; i < points.Length; i++) area += SectionCross(points[i], points[(i + 1) % points.Length]);
        float turn = area < 0 ? -1 : 1;
        var result = new List<(int A, int B, int C)>();
        while (pending.Count > 3)
        {
            bool found = false;
            for (int i = 0; i < pending.Count; i++)
            {
                int a = pending[(i + pending.Count - 1) % pending.Count], b = pending[i], c = pending[(i + 1) % pending.Count];
                float corner = SectionCross(points[b] - points[a], points[c] - points[b]) * turn;
                if (MathF.Abs(corner) <= 1e-8f)
                {
                    pending.RemoveAt(i);
                    found = true;
                    break;
                }
                if (corner < 0) continue;
                bool contains = false;
                foreach (int other in pending)
                {
                    if (other == a || other == b || other == c) continue;
                    var p = points[other];
                    if (SectionCross(points[b] - points[a], p - points[a]) * turn >= -1e-8f
                        && SectionCross(points[c] - points[b], p - points[b]) * turn >= -1e-8f
                        && SectionCross(points[a] - points[c], p - points[c]) * turn >= -1e-8f)
                    {
                        contains = true;
                        break;
                    }
                }
                if (contains) continue;
                result.Add((a, b, c));
                pending.RemoveAt(i);
                found = true;
                break;
            }
            if (!found) break;
        }
        if (pending.Count == 3) result.Add((pending[0], pending[1], pending[2]));
        return result;
    }

    static void AppendLimbBoneSection(Vertex[] floor, List<(int A, int B, int C)> triangles, Vector3[] outline, uint[] groups,
        in NativeSectionCut cut, Vector3 u, Vector3 v, int remaining, Action<Vertex, Vertex, Vertex> emit, uint bloodColour)
    {
        const int segments = 16, needed = segments * 9;
        if (remaining < needed || triangles.Count == 0) return;
        var votes = new int[7];
        foreach (var point in floor) votes[Math.Clamp(Group(point, groups), 0, votes.Length - 1)]++;
        if (Enumerable.Range(1, 4).Max(group => votes[group]) < floor.Length * 0.8f) return;
        var polygon = outline.Select(p => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v))).ToArray();
        float Clearance(Vector2 point)
        {
            float nearest = float.PositiveInfinity;
            for (int edge = 0; edge < polygon.Length; edge++)
            {
                var a = polygon[edge]; var step = polygon[(edge + 1) % polygon.Length] - a;
                float length = step.LengthSquared();
                float t = length < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - a, step) / length, 0, 1);
                nearest = MathF.Min(nearest, Vector2.Distance(point, a + step * t));
            }
            return nearest;
        }
        var best = triangles[0];
        float radius = 0;
        foreach (var triangle in triangles)
        {
            var centre = (polygon[triangle.A] + polygon[triangle.B] + polygon[triangle.C]) / 3;
            if (!SectionInside(centre, polygon)) continue;
            float room = Clearance(centre);
            if (room > radius) { radius = room; best = triangle; }
        }
        radius = MathF.Min(radius * 0.40f, 0.45f);
        if (radius < 0.035f) return;
        static Point PointOf(Vertex p) => new(p.Position, default, p.Bones, p.Weights);
        var middle = Interpolate(Interpolate(PointOf(floor[best.A]), PointOf(floor[best.B]), 0.5f), PointOf(floor[best.C]), 1f / 3);
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        var kept = normal * cut.KeepSide;
        var origin = middle.Position - normal * (Vector3.Dot(normal, middle.Position) + cut.Plane.W) + kept * 0.035f;
        Vertex At(float angle, float scale, bool marrow)
        {
            float rough = 1 + 0.05f * MathF.Sin(angle * 5 + origin.Y * 4);
            var p = origin + (u * MathF.Cos(angle) + v * MathF.Sin(angle) * 0.78f) * (radius * scale * rough);
            var colour = marrow ? NativeFluidPalette.Recolour(new Vector4(0.30f, 0.055f, 0.045f, 0), bloodColour) : new Vector4(0.70f, 0.61f, 0.43f, 0);
            return new() { Position = p, Normal = -kept, Uv = floor[0].Uv, Tangent = new(p, Layer), Colour = colour, Bones = middle.Bones, Weights = middle.Weights };
        }
        var centreVertex = At(0, 0, true);
        for (int i = 0; i < segments; i++)
        {
            float a = MathF.Tau * i / segments, b = MathF.Tau * (i + 1) / segments;
            var outerA = At(a, 1, false); var outerB = At(b, 1, false);
            var innerA = At(a, 0.48f, false); var innerB = At(b, 0.48f, false);
            emit(outerA, outerB, innerA); emit(outerB, innerB, innerA);
            emit(centreVertex, innerA with { Colour = centreVertex.Colour }, innerB with { Colour = centreVertex.Colour });
        }
    }
}
