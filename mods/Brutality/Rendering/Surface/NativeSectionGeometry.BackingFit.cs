// Adapted from integrated GPL-3.0-or-later source 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Rendering.Surface;

internal static partial class NativeSectionGeometry
{
    static void FitSectionBacking(List<Vertex> vertices, in NativeSectionCut cut, SectionSkin skin, float backingDepth)
    {
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        var kept = normal * cut.KeepSide;
        float planeOffset = cut.Plane.W;
        var depths = new Dictionary<(float Contour, Vector3 Point), float>();
        var allowed = new Dictionary<Vector3, float>();
        Vector3 Base(Vertex vertex) => vertex.Position - normal * (Vector3.Dot(normal, vertex.Position) + planeOffset);
        float Maximum(Vector3 point)
        {
            if (!allowed.TryGetValue(point, out float depth)) allowed[point] = depth = SectionDepth(point + kept * 0.001f, kept, skin, backingDepth);
            return depth;
        }
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (vertex.Uv.X < BackingBand) continue;
            var point = Base(vertex);
            var key = (vertex.Uv.Y, point);
            float depth = Math.Clamp(Vector3.Dot(kept, vertex.Position - point), 0.008f, Maximum(point));
            depths[key] = depths.TryGetValue(key, out float old) ? MathF.Min(old, depth) : depth;
        }
        // Checking only boundary vertices misses a thin neck/hip behind the interior of a large ear.
        // Shared vertices only move toward the cut plane; a later correction cannot invalidate an earlier face.
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            var a = vertices[i]; var b = vertices[i + 1]; var c = vertices[i + 2];
            if (a.Uv.X < BackingBand || b.Uv.X < BackingBand || c.Uv.X < BackingBand) continue;
            var pa = Base(a); var pb = Base(b); var pc = Base(c);
            var ka = (a.Uv.Y, pa); var kb = (b.Uv.Y, pb); var kc = (c.Uv.Y, pc);
            foreach (var weight in new[] { new Vector3(1f / 3), new(0.5f, 0.5f, 0), new(0, 0.5f, 0.5f), new(0.5f, 0, 0.5f) })
            {
                var point = pa * weight.X + pb * weight.Y + pc * weight.Z;
                float limit = Maximum(point);
                float actual = depths[ka] * weight.X + depths[kb] * weight.Y + depths[kc] * weight.Z;
                if (actual <= limit + 1e-5f) continue;
                float ratio = Math.Clamp((limit - 0.008f) / MathF.Max(actual - 0.008f, 1e-8f), 0, 1);
                depths[ka] = 0.008f + (depths[ka] - 0.008f) * ratio;
                depths[kb] = 0.008f + (depths[kb] - 0.008f) * ratio;
                depths[kc] = 0.008f + (depths[kc] - 0.008f) * ratio;
            }
        }
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (vertex.Uv.X < BackingBand) continue;
            var point = Base(vertex);
            var p = point + kept * depths[(vertex.Uv.Y, point)];
            vertices[i] = vertex with { Position = p, Tangent = new(p, Layer) };
        }
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            if (vertices[i].Uv.X < BackingBand || vertices[i + 1].Uv.X < BackingBand || vertices[i + 2].Uv.X < BackingBand) continue;
            var cross = Vector3.Cross(vertices[i + 1].Position - vertices[i].Position, vertices[i + 2].Position - vertices[i].Position);
            var n = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : -kept;
            if (Vector3.Dot(n, kept) > 0) n = -n;
            for (int k = 0; k < 3; k++) vertices[i + k] = vertices[i + k] with { Normal = n };
        }
    }
}
