using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>A surface-fixed frame, before face morphs. The fourth matrix vector marks dynamic placement.</summary>
readonly record struct HeadWoundFrame(Vector3 Centre, Vector3 Normal, Vector3 Along, float HalfLength, float HalfWidth)
{
    public Vector3 Side => Vector3.Normalize(Vector3.Cross(Normal, Along));
    public Matrix4x4 Packed => new(
        Centre.X, Centre.Y, Centre.Z, HalfLength,
        Along.X, Along.Y, Along.Z, HalfWidth,
        Normal.X, Normal.Y, Normal.Z, 0.65f,
        1, 0, 0, 0);
}

static partial class HeadWoundSurface
{
    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    static bool Unit(Vector3 p, out Vector3 unit)
    {
        float length = p.LengthSquared();
        bool valid = float.IsFinite(length) && length > 1e-10f && Finite(p);
        unit = valid ? p / MathF.Sqrt(length) : default;
        return valid && Finite(unit);
    }

    /// <summary>A small triangle subset; an AABB intersection keeps a large triangle spanning the cut.</summary>
    static Vertex[] Nearby(Vertex[] source, Vector3 point, float reach)
    {
        var result = new List<Vertex>();
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            var min = Vector3.Min(source[i].Position, Vector3.Min(source[i + 1].Position, source[i + 2].Position));
            var max = Vector3.Max(source[i].Position, Vector3.Max(source[i + 1].Position, source[i + 2].Position));
            if (Vector3.DistanceSquared(Vector3.Clamp(point, min, max), point) > reach * reach) continue;
            result.AddRange([source[i], source[i + 1], source[i + 2]]);
        }
        return result.ToArray();
    }

    readonly record struct SurfaceHit(Vector3 Position, Vector3 Normal, Vector3 Morph);
    static SurfaceHit? Project(Vertex[] source, Vector3 point, Vector3 directionNormal, Vector3 anchor, float depth, Vector4[]? morphs)
    {
        Vector3 start = point + directionNormal * 2, direction = -directionNormal;
        float best = float.MaxValue;
        SurfaceHit? found = null;
        Vector3 Delta(Vertex v)
        {
            uint slot = BitConverter.SingleToUInt32Bits(v.Colour.X);
            return morphs is not null && slot > 0 && slot <= morphs.Length ? new(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : default;
        }
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            Vertex va = source[i], vb = source[i + 1], vc = source[i + 2];
            Vector3 edge1 = vb.Position - va.Position, edge2 = vc.Position - va.Position;
            Vector3 cross = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (MathF.Abs(determinant) < 1e-7f) continue;
            Vector3 delta = start - va.Position;
            float u = Vector3.Dot(delta, cross) / determinant;
            if (u < -1e-5f || u > 1.00001f) continue;
            Vector3 q = Vector3.Cross(delta, edge1);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < -1e-5f || u + v > 1.00001f) continue;
            float t = Vector3.Dot(edge2, q) / determinant;
            if (t < 0 || t > 4) continue;
            var hit = start + direction * t;
            if (MathF.Abs(Vector3.Dot(hit - anchor, directionNormal)) > depth) continue;
            if (!Unit(va.Normal * (1 - u - v) + vb.Normal * u + vc.Normal * v, out var normal)) continue;
            // Do not take the far wall merely because the estimate lies inside the head.
            if (Vector3.Dot(normal, directionNormal) < 0.05f) continue;
            float distance = Vector3.DistanceSquared(hit, anchor);
            if (distance < best)
            {
                best = distance;
                found = new(hit, normal, Delta(va) * (1 - u - v) + Delta(vb) * u + Delta(vc) * v);
            }
        }
        return found;
    }

    /// <summary>Projects an inferred head-region anchor onto original skin; this is not a weapon collision measurement.</summary>
    public static bool TryProjectPlacement(Vertex[] source, in CharacterHeadWoundPlacement placement, int profile, out HeadWoundFrame frame)
    {
        frame = default;
        if (!Finite(placement.Point) || !Unit(placement.Normal, out var normal) || !Finite(placement.Tangent) || source.Length == 0) return false;
        if (Project(source, placement.Point, normal, placement.Point, 2, null) is not { } hit) return false;
        if (!Unit(placement.Tangent - hit.Normal * Vector3.Dot(placement.Tangent, hit.Normal), out var along)) return false;
        var preset = Frame(Math.Clamp(profile, 0, Profiles - 1));
        frame = new(hit.Position, hit.Normal, along, preset.HalfLength, preset.HalfWidth);
        return true;
    }

    /// <summary>Fits a long cut to a small curved head without changing its anchor or blade axis.</summary>
    public static Vertex[]? FitPlacementVertices(Vertex[] source, in HeadWoundFrame frame, int headBone, Vector4[]? morphs, int profile, bool broken, out HeadWoundFrame fitted)
    {
        fitted = frame;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var candidate = frame with { HalfLength = frame.HalfLength * (1 - attempt * 0.05f) };
            if (PlacementVertices(source, candidate, headBone, morphs, profile, broken) is not { Length: > 0 } vertices) continue;
            fitted = candidate;
            return vertices;
        }
        return null;
    }

    /// <summary>One active cut, with its original-skin frame and barycentrically interpolated face morph.</summary>
    public static Vertex[]? PlacementVertices(Vertex[] source, in HeadWoundFrame frame, int headBone, Vector4[]? morphs, int profile, bool broken)
    {
        var f = frame;
        source = Nearby(source, f.Centre, f.HalfLength + 0.6f);
        var result = new List<Vertex>();
        float[] bands = broken ? [-1.2f, -0.85f, -0.52f, -0.24f, -0.24f, 0.24f, 0.24f, 0.52f, 0.85f, 1.2f]
                               : [-1.2f, -0.85f, -0.52f, 0, 0.52f, 0.85f, 1.2f];
        float[] heights = broken ? [-0.004f, -0.012f, -0.045f, -0.045f, -0.13f, -0.13f, -0.045f, -0.045f, -0.012f, -0.004f]
                                 : [-0.004f, -0.012f, -0.045f, -0.045f, -0.045f, -0.012f, -0.004f];
        var points = new Vector3[Sections + 1, bands.Length];
        var colours = new Vector4[Sections + 1, bands.Length];
        for (int row = 0; row <= Sections; row++)
        {
            float u = f.HalfLength * (2f * row / Sections - 1), width = Width(u, f.HalfLength, f.HalfWidth), bend = Bend(u);
            for (int band = 0; band < bands.Length; band++)
            {
                float side = bands[band];
                if (broken && band is >= 3 and <= 6) side = MathF.Sign(side) * Cleft(u);
                var p = f.Centre + f.Along * u + f.Side * (bend + side * width);
                if (Project(source, p, f.Normal, f.Centre, 0.65f, morphs) is not { } hit) return null;
                float lift = width / f.HalfWidth * 0.004f * MathF.Sin(u * 19 + side * 2);
                points[row, band] = hit.Position + hit.Morph + f.Normal * (heights[band] * Math.Clamp(width / f.HalfWidth * 4, 0, 1) + lift);
                bool flesh = MathF.Abs(side) >= 0.85f, inner = broken && band is 4 or 5;
                float grain = 0.94f + 0.06f * MathF.Sin(u * 31 + side * 5);
                float exposure = Math.Clamp(1.3f - MathF.Abs(u + 0.03f) / 0.36f, 0, 1) * (side > 0 ? 0.60f : 1);
                var tissue = new Vector4(0.28f, 0.025f, 0.03f, 0);
                colours[row, band] = (flesh ? tissue : inner ? new Vector4(0.32f, 0.19f, 0.15f, 0)
                    : Vector4.Lerp(tissue, new Vector4(0.61f, 0.55f, 0.44f, 0), exposure)) * grain;
            }
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc)
        {
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() < 1e-12f) return;
            var n = Vector3.Normalize(cross);
            if (Vector3.Dot(n, f.Normal) < 0) { (b, c) = (c, b); (cb, cc) = (cc, cb); n = -n; }
            Vertex Make(Vector3 p, Vector4 colour) => new() { Position = p, Normal = Vector3.Normalize(f.Normal * 0.5f + n * 0.5f), Tangent = new(p, 8 + profile * 2 + (broken ? 1 : 0)), Colour = colour, Bones = (uint)headBone, Weights = new(1, 0, 0, 0) };
            result.AddRange([Make(a, ca), Make(b, cb), Make(c, cc)]);
        }
        for (int row = 0; row < Sections; row++)
            for (int band = 0; band + 1 < bands.Length; band++)
            {
                if (broken && band == 4) continue;
                var a = points[row, band]; var b = points[row + 1, band]; var c = points[row, band + 1]; var d = points[row + 1, band + 1];
                Triangle(a, b, c, colours[row, band], colours[row + 1, band], colours[row, band + 1]);
                Triangle(b, d, c, colours[row + 1, band], colours[row + 1, band + 1], colours[row, band + 1]);
            }
        return result.Count == 0 ? null : result.ToArray();
    }
}
