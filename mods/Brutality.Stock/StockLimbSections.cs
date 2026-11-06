using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Brutality;

/// <summary>Compact skin, muscle and bone detail on a measured bind-space limb opening.</summary>
internal static class StockLimbSections
{
    internal static void Append(List<Vertex> output, IReadOnlyList<Vector3> contour, SkeletonRig rig,
        LimbCutState shape, Vector4 plane, int side, int bone, StockTissuePalette palette)
    {
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        var kept = normal * side;
        var u = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal));
        var v = Vector3.Cross(normal, u);
        var centre = contour.Aggregate(Vector3.Zero, (sum, point) => sum + point) / contour.Count;
        var outline = contour.Select(point => Project(point - centre)).ToArray();
        var axis = LongAxis(outline);
        float span = outline.Max(point => Vector2.Dot(point, axis)) - outline.Min(point => Vector2.Dot(point, axis));
        float scale = Math.Clamp(span * .5f, .10f, 1.2f);

        // Flesh is a closed, shallow cross-section. Alternating bundles have slightly different profiles and tones.
        const int rings = 4;
        var tissue = new Vertex[contour.Count * rings];
        for (int i = 0; i < contour.Count; i++)
        {
            var edge = outline[i];
            float angle = MathF.Atan2(edge.Y, edge.X);
            float bundle = .5f + .5f * MathF.Sin(angle * 7 + shape.Fraction * 5);
            tissue[i * rings] = At(edge, .009f, Shade(palette.Wet, .80f + .19f * bundle));
            tissue[i * rings + 1] = At(edge * .91f, .018f * scale, Shade(Vector4.Lerp(palette.Muscle, palette.Fat, .40f), .85f + .14f * bundle));
            tissue[i * rings + 2] = At(edge * .73f, (.07f + .04f * bundle) * scale, Shade(palette.Muscle, .80f + .20f * bundle));
            tissue[i * rings + 3] = At(edge * .39f, (.13f + .07f * bundle) * scale, Shade(palette.Flesh, .67f + .16f * bundle));
        }
        var floor = At(Vector2.Zero, .23f * scale, Shade(palette.Muscle, .71f));
        for (int i = 0; i < contour.Count; i++)
        {
            int a = i * rings, b = ((i + 1) % contour.Count) * rings;
            for (int band = 0; band < rings - 1; band++)
            {
                Triangle(tissue[a + band], tissue[b + band], tissue[a + band + 1]);
                Triangle(tissue[a + band + 1], tissue[b + band], tissue[b + band + 1]);
            }
            Triangle(tissue[a + rings - 1], tissue[b + rings - 1], floor);
        }

        // The two sides share the same projected bone centres. The rig's joint, rather than world Y or
        // a fixed fraction, separates single humerus/femur sections from double forearm/calf sections.
        string hand = shape.LimbSlot % 2 == 0 ? "L" : "R";
        var (upper, middle, lower) = shape.LimbSlot < 2 ? ("UpperArm", "Forearm", "Hand") : ("Thigh", "Calf", "Foot");
        if (rig.Handle($"Bip01 {hand} {upper}") is not int first || rig.Handle($"Bip01 {hand} {middle}") is not int second
            || rig.Handle($"Bip01 {hand} {lower}") is not int third) return;
        var aBind = rig.BindPosition[first];
        var bBind = rig.BindPosition[second];
        var cBind = rig.BindPosition[third];
        float firstLength = Vector3.Distance(aBind, bBind), secondLength = Vector3.Distance(bBind, cBind);
        float distance = shape.Fraction * (firstLength + secondLength);
        bool paired = distance > firstLength;
        var bind = paired ? Vector3.Lerp(bBind, cBind, (distance - firstLength) / secondLength)
            : Vector3.Lerp(aBind, bBind, distance / firstLength);
        var joint = Project(bind - centre);
        // Some skin morphs shift away from the raw joint. Fit the bone inside that cut, without poking through the silhouette.
        if (!Inside(joint)) joint = Vector2.Zero;
        joint = Vector2.Lerp(Vector2.Zero, joint, .53f);
        float offset = paired ? span * .125f : 0;
        AppendBone(joint - axis * offset, span * (paired ? .107f : .165f));
        if (paired) AppendBone(joint + axis * offset, span * .086f);

        Vector2 Project(Vector3 delta) => new(Vector3.Dot(delta, u), Vector3.Dot(delta, v));

        bool Inside(Vector2 p)
        {
            bool result = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                var a = outline[i]; var b = outline[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    result = !result;
            }
            return result;
        }

        Vertex At(Vector2 point, float depth, Vector4 colour) => new()
        {
            Position = centre + u * point.X + v * point.Y + kept * depth,
            Normal = -kept, Colour = colour, Bones = (uint)bone, Weights = Vector4.UnitX,
        };

        void Triangle(Vertex a, Vertex b, Vertex c)
        {
            var surface = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (surface.LengthSquared() < 1e-12f) return;
            surface = Vector3.Normalize(surface);
            if (Vector3.Dot(surface, -kept) < 0) { (b, c) = (c, b); surface = -surface; }
            output.Add(a with { Normal = surface }); output.Add(b with { Normal = surface }); output.Add(c with { Normal = surface });
        }

        void AppendBone(Vector2 origin, float radius)
        {
            if (!Inside(origin)) return;
            for (int i = 0; i < outline.Length; i++)
            {
                var p = outline[i]; var step = outline[(i + 1) % outline.Length] - p;
                float length = step.LengthSquared();
                float at = length > 1e-9f ? Math.Clamp(Vector2.Dot(origin - p, step) / length, 0, 1) : 0;
                radius = MathF.Min(radius, Vector2.Distance(origin, p + step * at) * .82f);
            }
            if (radius < .025f) return;
            const int points = 12;
            float top = -.017f * scale, baseDepth = .16f * scale;
            var marrow = At(origin, top + .030f * scale, palette.Marrow);
            for (int i = 0; i < points; i++)
            {
                float a = MathF.Tau * i / points, b = MathF.Tau * (i + 1) / points;
                var outerA = BoneAt(a, 1, top, palette.Bone); var outerB = BoneAt(b, 1, top, palette.Bone);
                var innerA = BoneAt(a, .52f, top, palette.Bone); var innerB = BoneAt(b, .52f, top, palette.Bone);
                var baseA = BoneAt(a, 1.08f, baseDepth, Shade(palette.Bone, .75f));
                var baseB = BoneAt(b, 1.08f, baseDepth, Shade(palette.Bone, .75f));
                var redA = BoneAt(a, .52f, top + .030f * scale, palette.Marrow);
                var redB = BoneAt(b, .52f, top + .030f * scale, palette.Marrow);
                Triangle(outerA, outerB, innerA); Triangle(outerB, innerB, innerA);
                Triangle(baseA, baseB, outerA); Triangle(baseB, outerB, outerA);
                Triangle(innerA, innerB, redA); Triangle(innerB, redB, redA);
                Triangle(redA, redB, marrow);
            }

            Vertex BoneAt(float angle, float size, float depth, Vector4 colour)
            {
                var cross = new Vector2(-axis.Y, axis.X);
                var direction = axis * MathF.Cos(angle) + cross * (.78f * MathF.Sin(angle));
                float uneven = 1 + .05f * MathF.Sin(angle * 5 + shape.Fraction * 8);
                return At(origin + direction * (radius * size * uneven), depth, colour);
            }
        }
    }

    static Vector2 LongAxis(Vector2[] outline)
    {
        float xx = 0, yy = 0, xy = 0;
        foreach (var point in outline) { xx += point.X * point.X; yy += point.Y * point.Y; xy += point.X * point.Y; }
        float angle = .5f * MathF.Atan2(2 * xy, xx - yy);
        return new(MathF.Cos(angle), MathF.Sin(angle));
    }

    static Vector4 Shade(Vector4 colour, float factor) => colour * new Vector4(factor, factor, factor, 1);
}
