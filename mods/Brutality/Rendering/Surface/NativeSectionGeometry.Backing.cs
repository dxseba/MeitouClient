// Adapted from integrated GPL-3.0-or-later source 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Rendering.Surface;

internal static partial class NativeSectionGeometry
{
    // Internal backing is separate from the shallow flesh rim. Its UV band is metadata, not an asset texture coordinate.
    internal const float BackingBand = 3;

    static float SectionDepth(Vector3 origin, Vector3 kept, SectionSkin skin, float backingDepth)
    {
        float depth = backingDepth;
        bool hit = false;
        var source = skin.Vertices;
        foreach (int triangle in skin.Candidates(origin))
        {
            var edge1 = source[triangle + 1] - source[triangle];
            var edge2 = source[triangle + 2] - source[triangle];
            var cross = Vector3.Cross(kept, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (MathF.Abs(determinant) < 1e-8f) continue;
            var from = origin - source[triangle];
            float u = Vector3.Dot(from, cross) / determinant;
            if (u is < -1e-5f or > 1.00001f) continue;
            var q = Vector3.Cross(from, edge1);
            float v = Vector3.Dot(kept, q) / determinant;
            if (v < -1e-5f || u + v > 1.00001f) continue;
            float at = Vector3.Dot(edge2, q) / determinant;
            if (at <= 0.001f) continue;
            hit = true;
            depth = MathF.Min(depth, at * 0.82f);
        }
        // Open skin shells supply no opposite wall. A shallow fallback is safer than projecting outside an unsupported head/limb.
        return hit ? MathF.Max(0.008f, depth) : 0.20f;
    }

    static void AppendSectionBacking(Vertex[] floor, List<(int A, int B, int C)> triangles, Vector3[] outline, in NativeSectionCut cut,
        SectionSkin skin, float backingDepth, int remaining, Action<Vertex, Vertex, Vertex> emit)
    {
        if (remaining < 3 || triangles.Count == 0) return;
        var n = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        var kept = n * cut.KeepSide;
        float offset = cut.Plane.W;
        Vertex Project(Vertex vertex)
        {
            var p = vertex.Position - n * (Vector3.Dot(n, vertex.Position) + offset);
            return vertex with { Position = p, Uv = new(BackingBand, vertex.Uv.Y) };
        }
        var polygon = floor.Select(Project).ToArray();
        var axis = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, n));
        var v = Vector3.Cross(n, u);
        var footprint = outline.Select(p => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v))).ToArray();
        bool Inside(Vector3 p) => SectionInside(new(Vector3.Dot(p, u), Vector3.Dot(p, v)), footprint);
        var recessed = new Dictionary<Vector3, Vector3>();
        Vertex Recess(Vertex vertex)
        {
            if (!recessed.TryGetValue(vertex.Position, out var p))
            {
                p = vertex.Position + kept * SectionDepth(vertex.Position + kept * 0.001f, kept, skin, backingDepth);
                recessed[vertex.Position] = p;
            }
            return vertex with { Position = p, Normal = -kept, Tangent = new(p, Layer) };
        }
        static Vertex Mid(Vertex a, Vertex b)
        {
            var point = Interpolate(new(a.Position, default, a.Bones, a.Weights), new(b.Position, default, b.Bones, b.Weights), 0.5f);
            return a with { Position = point.Position, Bones = point.Bones, Weights = point.Weights, Colour = (a.Colour + b.Colour) * 0.5f };
        }
        var pending = new Stack<(Vertex A, Vertex B, Vertex C, int Depth)>();
        foreach (var (a, b, c) in triangles) pending.Push((polygon[a], polygon[b], polygon[c], 0));
        const float targetEdge = .65f, maximumEdge = 1.3f;
        int used = 0;
        while (pending.TryPop(out var triangle))
        {
            var (a, b, c, depth) = triangle;
            float ab = Vector3.DistanceSquared(a.Position, b.Position), bc = Vector3.DistanceSquared(b.Position, c.Position), ca = Vector3.DistanceSquared(c.Position, a.Position);
            bool contained = Inside(a.Position) && Inside(b.Position) && Inside(c.Position)
                && Inside((a.Position + b.Position) * 0.5f) && Inside((b.Position + c.Position) * 0.5f)
                && Inside((c.Position + a.Position) * 0.5f) && Inside((a.Position + b.Position + c.Position) / 3);
            float longest = MathF.Max(ab, MathF.Max(bc, ca));
            // Subdivide long ears locally, and clip ears of a near-touching concave contour to the actual original outline.
            if ((longest > targetEdge * targetEdge || !contained && longest > 0.015f * 0.015f) && depth < 12 && used + 3 * (pending.Count + 2) <= remaining)
            {
                if (ab >= bc && ab >= ca)
                {
                    var m = Mid(a, b);
                    pending.Push((a, m, c, depth + 1)); pending.Push((m, b, c, depth + 1));
                }
                else if (bc >= ca)
                {
                    var m = Mid(b, c);
                    pending.Push((b, m, a, depth + 1)); pending.Push((m, c, a, depth + 1));
                }
                else
                {
                    var m = Mid(c, a);
                    pending.Push((c, m, b, depth + 1)); pending.Push((m, a, b, depth + 1));
                }
                continue;
            }
            // An unresolved ear can span several joints and form a sheet as the piece bends.
            if (!contained || longest > maximumEdge * maximumEdge) continue;
            if (used + 3 > remaining) break;
            emit(Recess(a), Recess(b), Recess(c));
            used += 3;
        }
    }
}
