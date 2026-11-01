using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>Small relief patches projected onto the body's actual forehead triangles; layers 8/9 are scalp/skull wounds.</summary>
static class HeadWoundSurface
{
    static readonly Vector3 Centre = new(0.3f, 18.85f, 0.65f);
    const int Segments = 80;

    // Keep this contour in step with CharacterShaders' head opening. Projection uses the undeformed skin, like that shader.
    static float Field(Vector3 p) => Vector3.Distance(p, Centre) + 0.012f * MathF.Sin(p.X * 17 + p.Y * 9) + 0.007f * MathF.Sin(p.Y * 21);

    public static Vertex[] Source(Model model)
    {
        var result = new List<Vertex>();
        foreach (var part in model.Parts)
            for (int i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                var a = part.Vertices[part.Indices[i]];
                var b = part.Vertices[part.Indices[i + 1]];
                var c = part.Vertices[part.Indices[i + 2]];
                if (a.Uv.Y >= 0 || b.Uv.Y >= 0 || c.Uv.Y >= 0) continue;
                if (MathF.Min(Vector3.Distance(a.Position, Centre), MathF.Min(Vector3.Distance(b.Position, Centre), Vector3.Distance(c.Position, Centre))) > 1.5f) continue;
                result.AddRange([a, b, c]);
            }
        return result.ToArray();
    }

    public static void Append(List<Vertex> destination, Vertex[] source, int headBone, Vector4[]? morphs)
    {
        if (source.Length == 0) return;
        var closest = source.MinBy(v => Vector3.DistanceSquared(v.Position, Centre));
        var normal = Vector3.Normalize(closest.Normal);
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
        if (!float.IsFinite(across.X)) return;
        var up = Vector3.Normalize(Vector3.Cross(normal, across));
        var origin = closest.Position;
        Vector3 Delta(Vertex v)
        {
            uint slot = BitConverter.SingleToUInt32Bits(v.Colour.X);
            return morphs is not null && slot > 0 && slot <= morphs.Length ? new Vector3(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : Vector3.Zero;
        }
        (Vector3 Position, Vector3 Morph)? Surface(Vector3 point)
        {
            Vector3 start = point + normal * 2, direction = -normal;
            float best = float.MaxValue;
            (Vector3 Position, Vector3 Morph)? found = null;
            for (int i = 0; i + 2 < source.Length; i += 3)
            {
                Vector3 a = source[i].Position, edge1 = source[i + 1].Position - a, edge2 = source[i + 2].Position - a;
                Vector3 cross = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, cross);
                if (MathF.Abs(determinant) < 1e-7f) continue;
                Vector3 delta = start - a;
                float u = Vector3.Dot(delta, cross) / determinant;
                if (u < -1e-5f || u > 1.00001f) continue;
                Vector3 q = Vector3.Cross(delta, edge1);
                float v = Vector3.Dot(direction, q) / determinant;
                if (v < -1e-5f || u + v > 1.00001f) continue;
                float t = Vector3.Dot(edge2, q) / determinant;
                if (t < 0 || t > 4) continue;
                var hit = start + direction * t;
                float distance = Vector3.DistanceSquared(hit, Centre);
                if (distance < best) { best = distance; found = (hit, Delta(source[i]) * (1 - u - v) + Delta(source[i + 1]) * u + Delta(source[i + 2]) * v); }
            }
            return found;
        }
        // Centre the rings on the projection of the wound centre, rather than on the nearest (coarse) vertex.
        if (Surface(Centre) is not { } projected || Field(projected.Position) > 0.255f) return;
        origin = projected.Position;
        var pending = new List<Vertex>();
        bool Patch(bool broken)
        {
            float[] radii = broken ? [0.405f, 0.36f, 0.32f, 0.285f, 0.275f, 0.275f] : [0.405f, 0.36f, 0.32f, 0.28f, 0.25f];
            float[] heights = broken ? [-0.004f, -0.015f, -0.05f, -0.05f, -0.075f, -0.14f] : [-0.004f, -0.015f, -0.05f, -0.05f, -0.05f];
            var rings = new Vector3[radii.Length, Segments];
            for (int ring = 0; ring < radii.Length; ring++)
                for (int segment = 0; segment < Segments; segment++)
                {
                    float angle = MathF.Tau * segment / Segments;
                    var radial = across * MathF.Cos(angle) + up * MathF.Sin(angle);
                    float low = 0, high = 0;
                    // High-frequency edge noise can cross a level more than once: keep the first connected contour.
                    for (int scan = 1; scan <= 70; scan++)
                    {
                        high = scan * 0.01f;
                        if (Surface(origin + radial * high) is not { } sample) return false;
                        if (Field(sample.Position) >= radii[ring]) break;
                        low = high;
                    }
                    var hit = projected;
                    for (int step = 0; step < 16; step++)
                    {
                        float radius = (low + high) / 2;
                        if (Surface(origin + radial * radius) is not { } point) return false;
                        hit = point;
                        if (Field(point.Position) < radii[ring]) low = radius; else high = radius;
                    }
                    // The edge lifts in little pieces; the inner bone face drops into the existing skin opening.
                    float chip = broken && ring is 3 or 4 ? 0.004f * MathF.Sin(segment * 1.73f) : 0;
                    rings[ring, segment] = hit.Position + hit.Morph + normal * (heights[ring] + chip);
                }
            Vector4 Colour(int ring) => ring < 2 ? new(0.30f, 0.025f, 0.03f, 0) : broken && ring == radii.Length - 1 ? new(0.34f, 0.22f, 0.15f, 0) : new(0.68f, 0.60f, 0.47f, 0);
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc)
            {
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-12f) return;
                var n = Vector3.Normalize(cross);
                if (Vector3.Dot(n, normal) < 0) { (b, c) = (c, b); (cb, cc) = (cc, cb); n = -n; }
                Vertex Make(Vector3 p, Vector4 colour) => new() { Position = p, Normal = Vector3.Normalize(normal * 0.5f + n * 0.5f), Uv = closest.Uv, Tangent = new Vector4(p, broken ? 9 : 8), Colour = colour, Bones = (uint)headBone, Weights = new(1, 0, 0, 0) };
                pending.AddRange([Make(a, ca), Make(b, cb), Make(c, cc)]);
            }
            for (int ring = 0; ring + 1 < radii.Length; ring++)
                for (int segment = 0; segment < Segments; segment++)
                {
                    int next = (segment + 1) % Segments;
                    var a = rings[ring, segment]; var b = rings[ring, next];
                    var c = rings[ring + 1, segment]; var d = rings[ring + 1, next];
                    Triangle(a, b, c, Colour(ring), Colour(ring), Colour(ring + 1));
                    Triangle(b, d, c, Colour(ring), Colour(ring + 1), Colour(ring + 1));
                }
            if (!broken)
                for (int segment = 0; segment < Segments; segment++)
                    Triangle(rings[radii.Length - 1, segment], rings[radii.Length - 1, (segment + 1) % Segments], origin + projected.Morph + normal * -0.05f, Colour(2), Colour(2), Colour(2));
            return true;
        }
        // Both variants must fit. Otherwise retain the shader's painted fallback, with no incomplete strip.
        if (Patch(false) && Patch(true)) destination.AddRange(pending);
    }
}