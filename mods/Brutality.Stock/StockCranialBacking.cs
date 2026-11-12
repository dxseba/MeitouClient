using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality;

internal static partial class StockHeadAnatomy
{
    // Open neck, eyelid and mouth shells need not have a watertight edge graph. Measure the
    // actual exterior crossing in the cut plane and put the tissue behind that silhouette.
    static Vertex[] CranialBacking(Vertex[] skin, Vector4 plane, int side, int bone,
        Vector4 wet, Vector4 flesh, Vector4 soft, float recess)
    {
        const int Samples = 48;
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        float length = normal.Length();
        if (!float.IsFinite(length) || length <= 1e-5f) return [];
        normal /= length;
        float offset = plane.W / length;
        var kept = normal * side;
        var reference = MathF.Abs(normal.Y) < .8f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(reference, normal));
        var v = Vector3.Cross(normal, u);
        var segments = new List<(Vector2 A, Vector2 B)>();
        var intersections = new Vector2[3];
        for (int i = 0; i + 2 < skin.Length; i += 3)
        {
            int count = 0;
            for (int edge = 0; edge < 3; edge++)
            {
                var a = skin[i + edge].Position;
                var b = skin[i + (edge + 1) % 3].Position;
                float da = Vector3.Dot(normal, a) + offset, db = Vector3.Dot(normal, b) + offset;
                if ((da < 0) == (db < 0)) continue;
                var crossing = Vector3.Lerp(a, b, da / (da - db));
                var point = new Vector2(Vector3.Dot(crossing, u), Vector3.Dot(crossing, v));
                if (float.IsFinite(point.LengthSquared())) intersections[count++] = point;
            }
            if (count == 2 && Vector2.DistanceSquared(intersections[0], intersections[1]) > 1e-10f)
                segments.Add((intersections[0], intersections[1]));
        }
        if (segments.Count < 3) return [];

        var outline = Hull(segments.SelectMany(segment => new[] { segment.A, segment.B }));
        if (outline.Count < 3) return [];
        var centre = outline.Aggregate(Vector2.Zero, (sum, p) => sum + p) / outline.Count;
        var samples = new Vector2[Samples];
        for (int i = 0; i < samples.Length; i++)
        {
            float angle = MathF.Tau * i / samples.Length;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            float distance = 0;
            foreach (var segment in segments) distance = MathF.Max(distance, Crossing(segment.A, segment.B, direction));
            // Bridge a gap only where the ray actually misses the cut edges. The inset keeps
            // that seam below the exterior, including the tapering Worker and Prince heads.
            if (distance <= .001f)
            {
                for (int j = 0; j < outline.Count; j++)
                    distance = MathF.Max(distance, Crossing(outline[j], outline[(j + 1) % outline.Count], direction));
                distance *= .91f;
            }
            samples[i] = direction * distance;
        }
        float radius = samples.Max(p => p.Length());
        if (radius < .025f) return [];
        float depth = Math.Clamp(radius * recess, .032f, .50f);
        var output = new List<Vertex>(Samples * 21);
        for (int i = 0; i < Samples; i++)
        {
            int next = (i + 1) % Samples;
            var a = Rim(i, 0); var b = Rim(next, 0);
            for (int band = 1; band <= 3; band++)
            {
                var c = Rim(i, band); var d = Rim(next, band);
                Triangle(a, b, c); Triangle(b, d, c);
                (a, b) = (c, d);
            }
            Triangle(a, b, At(Vector2.Zero, depth, Shade(soft, .77f)));
        }
        return output.ToArray();

        float Crossing(Vector2 a, Vector2 b, Vector2 direction)
        {
            var line = b - a;
            float cross = Cross(direction, line);
            if (MathF.Abs(cross) <= 1e-7f) return 0;
            var start = a - centre;
            float along = Cross(start, direction) / cross;
            return along is >= -1e-5f and <= 1.00001f ? Cross(start, line) / cross : 0;
        }

        Vertex Rim(int index, int band)
        {
            var point = samples[index];
            float variation = .9f + .1f * MathF.Sin(index * 1.41f + point.X * 5.17f);
            return band switch
            {
                0 => At(point * .994f, .004f, Shade(wet, variation)),
                1 => At(point * .922f, depth * .16f, Shade(flesh, variation)),
                2 => At(point * .76f, depth * .55f, Shade(soft, variation)),
                _ => At(point * .47f, depth * .87f, Shade(flesh, .68f * variation)),
            };
        }

        Vertex At(Vector2 point, float inset, Vector4 colour)
        {
            point += centre;
            return new Vertex { Position = u * point.X + v * point.Y - normal * offset + kept * inset,
                Normal = -kept, Tangent = new(u, 1), Colour = colour, Bones = (uint)bone, Weights = Vector4.UnitX };
        }

        void Triangle(Vertex a, Vertex b, Vertex c)
        {
            var face = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (face.LengthSquared() < 1e-12f) return;
            face = Vector3.Normalize(face);
            if (Vector3.Dot(face, kept) > 0) { (b, c) = (c, b); face = -face; }
            output.Add(a with { Normal = face }); output.Add(b with { Normal = face }); output.Add(c with { Normal = face });
        }
    }

    static List<Vector2> Hull(IEnumerable<Vector2> source)
    {
        var ordered = source.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (ordered.Length < 3) return [];
        var hull = new List<Vector2>(ordered.Length);
        foreach (var point in ordered) Push(point, 0);
        int lower = hull.Count - 1;
        for (int i = ordered.Length - 2; i >= 0; i--) Push(ordered[i], lower);
        hull.RemoveAt(hull.Count - 1);
        return hull;

        void Push(Vector2 point, int start)
        {
            while (hull.Count - start >= 2 && Cross(hull[^1] - hull[^2], point - hull[^1]) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
    }

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    static Vector4 Shade(Vector4 colour, float brightness) => (colour * brightness) with { W = 1 };
}
