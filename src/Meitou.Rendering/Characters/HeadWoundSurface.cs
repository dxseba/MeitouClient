using System.Globalization;
using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>A blade-shaped forehead wound, projected onto the skin; layers 8/9 are scalp/skull wounds.</summary>
static class HeadWoundSurface
{
    static readonly Vector3 Centre = new(0.42f, 18.9f, 0.83f);
    static readonly Vector3 Normal = Vector3.Normalize(new(0.81257f, 0.30728f, 0.49528f));
    static readonly Vector3 Across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Normal));
    static readonly Vector3 Up = Vector3.Normalize(Vector3.Cross(Normal, Across));
    static readonly Vector3 Along = Vector3.Normalize(Across * 0.83f + Up * 0.55f);
    static readonly Vector3 Side = Vector3.Normalize(Vector3.Cross(Normal, Along));
    const float HalfLength = 0.62f, HalfWidth = 0.105f;
    const int Sections = 48;

    // These parameters also emit the skin's GLSL field; no separately tuned painted opening.
    static float Width(float u) => HalfWidth * MathF.Pow(MathF.Max(0, 1 - MathF.Abs(u) / HalfLength), 0.65f)
        * (1 + 0.10f * MathF.Sin(u * 47) + 0.06f * MathF.Sin(u * 83));
    static float Bend(float u) => 0.008f * MathF.Sin(u * 15) + 0.003f * MathF.Sin(u * 37);
    static float Cleft(float u) => 0.24f + 0.20f * MathF.Exp(-MathF.Pow((u - 0.12f) / 0.20f, 2));
    static string F(float value) => value.ToString("0.0########", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"vec3({F(v.X)}, {F(v.Y)}, {F(v.Z)})";
    public static string ShaderField => $$"""
        float headSwordCleft(vec3 p)
        {
            float u = dot(p - {{V(Centre)}}, {{V(Along)}});
            float t = (u - 0.12) / 0.20;
            return 0.24 + 0.20 * exp(-t * t);
        }
        float headSwordWound(vec3 p)
        {
            vec3 d = p - {{V(Centre)}};
            float u = dot(d, {{V(Along)}}), v = dot(d, {{V(Side)}});
            if (abs(u) > {{F(HalfLength)}} || abs(dot(d, {{V(Normal)}})) > 0.65) return 100.0;
            float width = {{F(HalfWidth)}} * pow(max(0.0, 1.0 - abs(u) / {{F(HalfLength)}}), 0.65)
                * (1.0 + 0.10 * sin(u * 47.0) + 0.06 * sin(u * 83.0));
            float bend = 0.008 * sin(u * 15.0) + 0.003 * sin(u * 37.0);
            return abs(v - bend) / max(width, 0.0001);
        }
        """;

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
                if (MathF.Min(Vector3.Distance(a.Position, Centre), MathF.Min(Vector3.Distance(b.Position, Centre), Vector3.Distance(c.Position, Centre))) > 1.6f) continue;
                result.AddRange([a, b, c]);
            }
        return result.ToArray();
    }

    public static void Append(List<Vertex> destination, Vertex[] source, int headBone, Vector4[]? morphs)
    {
        if (source.Length == 0) return;
        Vector3 Delta(Vertex v)
        {
            uint slot = BitConverter.SingleToUInt32Bits(v.Colour.X);
            return morphs is not null && slot > 0 && slot <= morphs.Length ? new Vector3(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : Vector3.Zero;
        }
        (Vector3 Position, Vector3 Morph)? Surface(Vector3 point)
        {
            Vector3 start = point + Normal * 2, direction = -Normal;
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
                if (MathF.Abs(Vector3.Dot(hit - Centre, Normal)) > 0.65f) continue;
                float distance = Vector3.DistanceSquared(hit, Centre);
                if (distance < best)
                {
                    best = distance;
                    found = (hit, Delta(source[i]) * (1 - u - v) + Delta(source[i + 1]) * u + Delta(source[i + 2]) * v);
                }
            }
            return found;
        }
        var pending = new List<Vertex>();
        bool Patch(bool broken)
        {
            // Cross-sections follow the blade, with pointed ends. Broken skull: two banks and an open cleft, no circular cap.
            float[] bands = broken ? [-1.2f, -0.85f, -0.52f, -0.24f, -0.24f, 0.24f, 0.24f, 0.52f, 0.85f, 1.2f]
                                   : [-1.2f, -0.85f, -0.52f, 0, 0.52f, 0.85f, 1.2f];
            float[] heights = broken ? [-0.004f, -0.012f, -0.045f, -0.045f, -0.13f, -0.13f, -0.045f, -0.045f, -0.012f, -0.004f]
                                     : [-0.004f, -0.012f, -0.045f, -0.045f, -0.045f, -0.012f, -0.004f];
            var points = new Vector3[Sections + 1, bands.Length];
            var colours = new Vector4[Sections + 1, bands.Length];
            Vector2 uv = default;
            for (int row = 0; row <= Sections; row++)
            {
                float u = HalfLength * (2f * row / Sections - 1), width = Width(u), bend = Bend(u);
                for (int band = 0; band < bands.Length; band++)
                {
                    float side = bands[band];
                    if (broken && band is >= 3 and <= 6) side = MathF.Sign(side) * Cleft(u);
                    if (Surface(Centre + Along * u + Side * (bend + side * width)) is not { } hit) return false;
                    float lift = width / HalfWidth * 0.004f * MathF.Sin(u * 19 + side * 2);
                    points[row, band] = hit.Position + hit.Morph + Normal * (heights[band] * Math.Clamp(width / HalfWidth * 4, 0, 1) + lift);
                    // Dark wet cut lips, a narrow ivory strip, and the darker broken bone's inner face.
                    bool flesh = MathF.Abs(side) >= 0.85f;
                    bool inner = broken && band is 4 or 5;
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
                if (Vector3.Dot(n, Normal) < 0) { (b, c) = (c, b); (cb, cc) = (cc, cb); n = -n; }
                Vertex Make(Vector3 p, Vector4 colour) => new() { Position = p, Normal = Vector3.Normalize(Normal * 0.5f + n * 0.5f), Uv = uv, Tangent = new Vector4(p, broken ? 9 : 8), Colour = colour, Bones = (uint)headBone, Weights = new(1, 0, 0, 0) };
                pending.AddRange([Make(a, ca), Make(b, cb), Make(c, cc)]);
            }
            for (int row = 0; row < Sections; row++)
                for (int band = 0; band + 1 < bands.Length; band++)
                {
                    if (broken && band == 4) continue; // The blade's cleft stays open all the way through the bone.
                    Vector3 a = points[row, band], b = points[row + 1, band], c = points[row, band + 1], d = points[row + 1, band + 1];
                    Triangle(a, b, c, colours[row, band], colours[row + 1, band], colours[row, band + 1]);
                    Triangle(b, d, c, colours[row + 1, band], colours[row + 1, band + 1], colours[row, band + 1]);
                }
            return true;
        }
        // All-or-nothing fit, so unsupported heads keep the shader fallback rather than a half-open strip.
        if (Patch(false) && Patch(true)) destination.AddRange(pending);
    }
}