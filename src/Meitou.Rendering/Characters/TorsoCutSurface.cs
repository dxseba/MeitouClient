using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>A torso cross-section with a wet flesh ring and open centre for the installed anatomy.</summary>
static class TorsoCutSurface
{
    public const int Layer = 32;
    public const float ExposureDepth = 2.8f;

    public static bool TryNormalize(in CharacterTorsoCut cut, out CharacterTorsoCut normalized)
    {
        normalized = default;
        var n = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float length = n.Length();
        if (cut.KeepSide is not (-1 or 1) || !float.IsFinite(length) || length < 1e-5f || !float.IsFinite(cut.Plane.W)) return false;
        var plane = cut.Plane / length;
        if (plane.Y < 0.5f) return false;
        normalized = new(plane, cut.KeepSide);
        return true;
    }

    public static Matrix4x4 Packed(in CharacterTorsoCut cut) => new(
        cut.Plane.X, cut.Plane.Y, cut.Plane.Z, cut.Plane.W,
        cut.KeepSide, ExposureDepth, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0);

    public static int Group(in Vertex vertex, uint[] groups)
    {
        int at = 0;
        for (int k = 1; k < 4; k++) if (vertex.Weights[k] > vertex.Weights[at]) at = k;
        int bone = (int)(vertex.Bones >> (at * 8) & 255);
        return bone / 8 < groups.Length ? (int)(groups[bone / 8] >> (bone % 8 * 4) & 15) : 0;
    }

    public static float Band(Vector3 position, int group, in CharacterTorsoCut cut) => group switch
    {
        1 or 2 or 6 => cut.KeepSide * 1000,
        3 or 4 => -cut.KeepSide * 1000,
        _ => (Vector3.Dot(new(cut.Plane.X, cut.Plane.Y, cut.Plane.Z), position) + cut.Plane.W) * cut.KeepSide,
    };

    /// <summary>A bind-space pick box clipped to the same kept half; ray parameter remains the caller's world distance.</summary>
    public static float? RayBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, int group, in CharacterTorsoCut cut)
    {
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) < 1e-9f)
            {
                if (origin[axis] < min[axis] || origin[axis] > max[axis]) return null;
                continue;
            }
            float a = (min[axis] - origin[axis]) / direction[axis], b = (max[axis] - origin[axis]) / direction[axis];
            if (a > b) (a, b) = (b, a);
            near = MathF.Max(near, a); far = MathF.Min(far, b);
            if (near > far) return null;
        }
        if (group != 0) return Band(default, group, cut) >= 0 ? near : null;
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float at = Band(origin, group, cut), slope = Vector3.Dot(normal, direction) * cut.KeepSide;
        if (MathF.Abs(slope) < 1e-9f) return at >= 0 ? near : null;
        float plane = -at / slope;
        if (slope > 0) near = MathF.Max(near, plane); else far = MathF.Min(far, plane);
        return near <= far ? near : null;
    }

    static float Ragged(Vector3 p) => 0.018f * MathF.Sin(p.X * 13 + p.Y * 7 + p.Z * 11) + 0.012f * MathF.Sin(p.X * 29 + p.Z * 17);
    static Vector3 Round(Vector3 p) => new(MathF.Round(p.X, 4), MathF.Round(p.Y, 4), MathF.Round(p.Z, 4));
    readonly record struct Point(Vector3 Position, Vector3 Morph, uint Bones, Vector4 Weights);

    /// <summary>Intersects only torso triangles, preserving edge skin weights and one interpolated body morph.</summary>
    public static Vertex[]? Build(Vertex[] source, uint[] groups, in CharacterTorsoCut cut, Vector4[]? morphs = null)
    {
        if (!TryNormalize(cut, out var normalized)) return null;
        var n = new Vector3(normalized.Plane.X, normalized.Plane.Y, normalized.Plane.Z);
        var points = new List<Point>();
        var byPosition = new Dictionary<Vector3, int>();
        var adjacent = new List<HashSet<int>>();
        int Add(Point point)
        {
            var key = Round(point.Position);
            if (byPosition.TryGetValue(key, out int existing)) return existing;
            int index = points.Count;
            points.Add(point); adjacent.Add([]); byPosition[key] = index;
            return index;
        }
        Vector3 Delta(in Vertex vertex)
        {
            uint slot = BitConverter.SingleToUInt32Bits(vertex.Colour.X);
            return morphs is not null && slot > 0 && slot <= morphs.Length ? new(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : default;
        }
        Point Edge(in Vertex a, in Vertex b, float t)
        {
            var weights = new Dictionary<uint, float>();
            for (int k = 0; k < 4; k++)
            {
                uint ba = a.Bones >> (8 * k) & 255, bb = b.Bones >> (8 * k) & 255;
                weights[ba] = weights.GetValueOrDefault(ba) + a.Weights[k] * (1 - t);
                weights[bb] = weights.GetValueOrDefault(bb) + b.Weights[k] * t;
            }
            var top = weights.Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(4).ToArray();
            float sum = top.Sum(p => p.Value);
            uint bones = 0;
            var w = Vector4.Zero;
            for (int k = 0; k < top.Length; k++) { bones |= top[k].Key << (k * 8); w[k] = top[k].Value / sum; }
            return new(Vector3.Lerp(a.Position, b.Position, t), Vector3.Lerp(Delta(a), Delta(b), t), bones, w);
        }
        for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
        {
            var a = source[triangle]; var b = source[triangle + 1]; var c = source[triangle + 2];
            if (Group(a, groups) != 0 || Group(b, groups) != 0 || Group(c, groups) != 0) continue;
            var hit = new List<int>(3);
            void Intersect(Vertex p, Vertex q)
            {
                float dp = Vector3.Dot(n, p.Position) + normalized.Plane.W, dq = Vector3.Dot(n, q.Position) + normalized.Plane.W;
                if (MathF.Abs(dp) < 1e-5f) hit.Add(Add(Edge(p, q, 0)));
                else if ((dp < 0) != (dq < 0)) hit.Add(Add(Edge(p, q, dp / (dp - dq))));
            }
            Intersect(a, b); Intersect(b, c); Intersect(c, a);
            var unique = hit.Distinct().ToArray();
            if (unique.Length != 2 || unique[0] == unique[1]) continue;
            adjacent[unique[0]].Add(unique[1]); adjacent[unique[1]].Add(unique[0]);
        }
        if (points.Count < 3) return null;
        var u = Vector3.Normalize(Vector3.UnitX - n * n.X);
        var v = Vector3.Cross(n, u);
        var visited = new HashSet<int>();
        List<Point>? rim = null;
        float largest = 0;
        for (int start = 0; start < points.Count; start++)
        {
            if (!visited.Add(start)) continue;
            var component = new List<int>();
            var queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.TryDequeue(out int next))
            {
                component.Add(next);
                foreach (int other in adjacent[next]) if (visited.Add(other)) queue.Enqueue(other);
            }
            if (component.Count < 3) continue;
            var centre = component.Aggregate(Vector3.Zero, (sum, i) => sum + points[i].Position) / component.Count;
            var ordered = component.Select(i => points[i]).OrderBy(p => MathF.Atan2(Vector3.Dot(p.Position - centre, v), Vector3.Dot(p.Position - centre, u))).ToList();
            float area = 0;
            for (int i = 0; i < ordered.Count; i++) area += Vector3.Dot(Vector3.Cross(ordered[i].Position - centre, ordered[(i + 1) % ordered.Count].Position - centre), n);
            if (MathF.Abs(area) > largest) { largest = MathF.Abs(area); rim = ordered; }
        }
        if (rim is not { Count: >= 3 } || largest < 1e-4f) return null;
        if (rim.Count > 512)
        {
            var original = rim;
            rim = Enumerable.Range(0, 512).Select(i => original[i * original.Count / 512]).ToList();
        }
        var middle = rim.Aggregate(Vector3.Zero, (sum, p) => sum + p.Position) / rim.Count;
        var middleMorph = rim.Aggregate(Vector3.Zero, (sum, p) => sum + p.Morph) / rim.Count;
        var outward = n * -normalized.KeepSide;
        Vertex At(Point point, int band)
        {
            float radius = band switch { 0 => 1.005f, 1 => 0.90f, _ => 0.72f };
            float depth = band switch { 0 => Ragged(point.Position), 1 => 0.025f, _ => 0.20f };
            var p = middle + (point.Position - middle) * radius + middleMorph + (point.Morph - middleMorph) * radius + n * (depth * normalized.KeepSide);
            float grain = 0.90f + 0.10f * MathF.Sin(point.Position.X * 31 + point.Position.Z * 23);
            var colour = (band == 1 ? new Vector4(0.43f, 0.17f, 0.10f, 0) : new Vector4(0.29f, 0.018f, 0.025f, 0)) * grain;
            return new() { Position = p, Normal = outward, Uv = new(band, 0), Tangent = new(p, Layer), Colour = colour, Bones = point.Bones, Weights = point.Weights };
        }
        var result = new List<Vertex>(rim.Count * 12);
        void Triangle(Vertex a, Vertex b, Vertex c)
        {
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.LengthSquared() < 1e-12f) return;
            var normal = Vector3.Normalize(cross);
            if (Vector3.Dot(normal, outward) < 0) { (b, c) = (c, b); normal = -normal; }
            result.Add(a with { Normal = normal }); result.Add(b with { Normal = normal }); result.Add(c with { Normal = normal });
        }
        for (int i = 0; i < rim.Count; i++)
            for (int band = 0; band < 2; band++)
            {
                var next = rim[(i + 1) % rim.Count];
                var a = At(rim[i], band); var b = At(next, band); var c = At(rim[i], band + 1); var d = At(next, band + 1);
                Triangle(a, b, c); Triangle(b, d, c);
            }
        return result.Count > 0 ? result.ToArray() : null;
    }

    public static string ShaderField => "const float torsoExposureDepth = " + ExposureDepth.ToString("0.0########", System.Globalization.CultureInfo.InvariantCulture) + ";\n" + """
        float torsoCutRagged(vec3 p) { return 0.018 * sin(p.x * 13.0 + p.y * 7.0 + p.z * 11.0) + 0.012 * sin(p.x * 29.0 + p.z * 17.0); }
        """;
}
