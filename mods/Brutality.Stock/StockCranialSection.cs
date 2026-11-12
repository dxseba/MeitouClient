using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Rendering.Surface;

internal static partial class NativeSectionGeometry
{
    // Fill the bone's rim only. A disk across the skull would cover the brain section.
    static Vertex[]? BuildCranialShell(List<(List<Point> Points, float Area)> rims, in NativeSectionCut cut,
        Vector3 normal, Vector3 u, Vector3 v, Vector4 corticalColour)
    {
        const int PointBudget = 384;
        var kept = normal * cut.KeepSide;
        var outward = -kept;
        var output = new List<Vertex>();
        int remaining = PointBudget;
        foreach (var (points, area) in rims)
        {
            if (area < .002f || remaining < 3) continue;
            var rim = points.Count <= remaining ? points : Enumerable.Range(0, remaining)
                .Select(index => points[index * points.Count / remaining]).ToList();
            remaining -= rim.Count;
            var inset = Insets(rim, normal, u, v);
            for (int i = 0; i < rim.Count; i++)
            {
                int j = (i + 1) % rim.Count;
                var a = At(i, inner: false); var b = At(j, inner: false);
                var c = At(i, inner: true); var d = At(j, inner: true);
                Triangle(a, b, c);
                Triangle(b, d, c);
            }

            Vertex At(int index, bool inner)
            {
                var point = rim[index];
                var position = point.Position + point.Morph + kept * (inner ? .043f : .015f);
                if (inner) position += inset[index] * .35f;
                float shade = inner ? .78f : .93f + .07f * MathF.Sin(position.X * 33 + position.Z * 29);
                return new Vertex { Position = position, Normal = outward, Tangent = new(u, 1),
                    Colour = (corticalColour * shade) with { W = 1 }, Bones = point.Bones, Weights = point.Weights };
            }
        }
        return output.Count == 0 ? null : output.ToArray();

        void Triangle(Vertex a, Vertex b, Vertex c)
        {
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(cross, outward) < 0) (b, c) = (c, b);
            output.Add(a); output.Add(b); output.Add(c);
        }
    }
}
