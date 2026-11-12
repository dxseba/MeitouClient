using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;

namespace Meitou.Brutality;

/// <summary>Skin, muscle and bone detail fitted to the skinned edge of a section.</summary>
internal static class StockLimbSections
{
    const float Muscle = .2f, Fascia = .4f, Cortex = .7f, Marrow = 1f;
    static readonly float[] innerRadii = [.965f, .90f, .77f, .61f, .45f, .29f, .13f];

    readonly record struct Bundle(Vector2 Centre, Vector2 Axis, Vector2 Radii, float Shade);
    readonly record struct TissueSample(float Depth, Vector4 Colour);

    internal static void Append(List<Vertex> output, IReadOnlyList<Vertex> contour, SkeletonRig rig,
        LimbCutState? shape, Vector4 plane, int side, int bone, StockTissuePalette palette)
    {
        var normal = Vector3.Normalize(new Vector3(plane.X, plane.Y, plane.Z));
        var kept = normal * side;
        var u = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal));
        var v = Vector3.Cross(normal, u);
        var centre = contour.Aggregate(Vector3.Zero, (sum, point) => sum + point.Position) / contour.Count;
        var outline = contour.Select(point => Project(point.Position - centre)).ToArray();
        var axis = LongAxis(outline);
        var across = new Vector2(-axis.Y, axis.X);
        float span = outline.Max(point => Vector2.Dot(point, axis)) - outline.Min(point => Vector2.Dot(point, axis));
        float scale = Math.Clamp(span * .5f, .10f, 1.2f);
        float halfLong = MathF.Max(.035f, span * .5f);
        float halfShort = MathF.Max(.035f, (outline.Max(point => Vector2.Dot(point, across)) - outline.Min(point => Vector2.Dot(point, across))) * .5f);
        float phase = shape is { } limb ? limb.Fraction * 16 + limb.LimbSlot * .91f : 2.4f;
        var bundles = Bundles(phase);
        var muscle = Vector4.Lerp(palette.Muscle, palette.Flesh, .22f);
        var connective = Vector4.Lerp(palette.Fat, palette.Muscle, .36f);
        // Sample the interior more finely than the boundary while retaining every original rim edge.
        int perEdge = Math.Clamp(78 / contour.Count, 1, 6);
        int samples = contour.Count * perEdge;
        int bands = innerRadii.Length;
        int centreIndex = contour.Count + samples * bands;
        var tissue = new Vertex[centreIndex + 1];
        var normals = new Vector3[tissue.Length];
        var faces = new List<(int A, int B, int C)>(samples * (bands * 2 + 1) + contour.Count);
        var tangent = new Vector4(u * axis.X + v * axis.Y, -side);
        for (int i = 0; i < contour.Count; i++)
        {
            var edge = contour[i];
            var lip = Sample(outline[i], 0);
            tissue[i] = edge with { Normal = -kept, Colour = lip.Colour, Uv = Texture(outline[i]), Tangent = tangent };
            int next = (i + 1) % contour.Count;
            for (int j = 0; j < perEdge; j++)
            {
                var seam = j == 0 ? edge : MeshCut.Interpolate(edge, contour[next], j / (float)perEdge);
                var point = Project(seam.Position - centre);
                var uv = Texture(point);
                float angle = MathF.Atan2(uv.Y, uv.X);
                float seamHeight = Vector3.Dot(seam.Position - centre, normal);
                int sample = i * perEdge + j;
                for (int band = 0; band < bands; band++)
                {
                    float radius = innerRadii[band] + (band == 0 ? .009f : .032f) * Ripple(angle, band);
                    float inset = 1 - radius;
                    var detail = Sample(point * radius, inset);
                    var inner = At(point * radius, detail.Depth, detail.Colour);
                    // The section gradually changes from the boundary's skinning to its local bone.
                    var weighted = MeshCut.Interpolate(seam, inner, Math.Clamp(inset * 1.45f, 0, 1));
                    tissue[Inner(sample, band)] = inner with { Position = inner.Position + normal * (seamHeight * radius),
                        Bones = weighted.Bones, Weights = weighted.Weights };
                }
            }
        }
        var centreTissue = Sample(Vector2.Zero, 1);
        tissue[centreIndex] = At(Vector2.Zero, centreTissue.Depth, centreTissue.Colour);
        for (int i = 0; i < contour.Count; i++)
        {
            int start = i * perEdge, end = (i + 1) * perEdge;
            for (int sample = start; sample < end; sample++)
                Face(i, Inner(sample, 0), Inner((sample + 1) % samples, 0));
            Face(i, (i + 1) % contour.Count, Inner(end % samples, 0));
        }
        for (int i = 0; i < samples; i++)
        {
            int next = (i + 1) % samples;
            for (int band = 0; band < bands - 1; band++)
            {
                Face(Inner(i, band), Inner(next, band), Inner(i, band + 1));
                Face(Inner(i, band + 1), Inner(next, band), Inner(next, band + 1));
            }
            Face(Inner(i, bands - 1), Inner(next, bands - 1), centreIndex);
        }
        foreach (var (a, b, c) in faces)
        {
            output.Add(Shaded(a)); output.Add(Shaded(b)); output.Add(Shaded(c));
        }

        int Inner(int sample, int band) => contour.Count + sample * bands + band;
        Vector2 Texture(Vector2 point) => new(Vector2.Dot(point, axis) / halfLong, Vector2.Dot(point, across) / halfShort);
        float Ripple(float angle, int band) => .58f * MathF.Sin(angle * 5 + phase + band * .73f)
            + .42f * MathF.Sin(angle * 11 - phase + band * 1.18f);

        TissueSample Sample(Vector2 point, float inset)
        {
            var uv = Texture(point);
            float grain = .58f * MathF.Sin(uv.X * 19 + uv.Y * 13 + phase) + .42f * MathF.Sin(uv.X * 35 - uv.Y * 28 - phase);
            if (inset < .06f)
                return new(scale * inset * (.62f + .22f * grain), Shade(Vector4.Lerp(palette.Wet, palette.Flesh, .35f), .63f + .15f * grain) with { W = Fascia });
            float best = float.PositiveInfinity, next = best;
            Bundle bundle = default;
            foreach (var candidate in bundles)
            {
                var local = uv - candidate.Centre;
                float x = Vector2.Dot(local, candidate.Axis) / candidate.Radii.X;
                float y = (local.Y * candidate.Axis.X - local.X * candidate.Axis.Y) / candidate.Radii.Y;
                float distance = x * x + y * y;
                if (distance < best) (best, next, bundle) = (distance, best, candidate);
                else next = MathF.Min(next, distance);
            }
            var fibre = uv - bundle.Centre;
            float longitudinal = Vector2.Dot(fibre, bundle.Axis);
            float cross = fibre.Y * bundle.Axis.X - fibre.X * bundle.Axis.Y;
            float striation = MathF.Sin(cross * 78 + 1.4f * MathF.Sin(longitudinal * 9 + phase));
            float septum = 1 - Ease(.04f, .34f, next - best);
            float crown = 1 - Ease(.12f, 1.65f, best);
            float depth = scale * (.108f + .116f * septum - .045f * crown + .014f * striation);
            float fat = (1 - Ease(.085f, .23f, inset)) * (.48f + .35f * grain);
            var tinted = Vector4.Lerp(muscle, palette.Marrow, .25f - .14f * crown);
            tinted = Shade(tinted, bundle.Shade + .11f * striation + .08f * crown - .19f * septum);
            tinted = Vector4.Lerp(tinted, connective, Math.Clamp(fat + .48f * septum, 0, .84f));
            return new(depth * Ease(.017f, .15f, inset), tinted with { W = fat > .31f || septum > .69f ? Fascia : Muscle });
        }

        void Face(int a, int b, int c)
        {
            var surface = Vector3.Cross(tissue[b].Position - tissue[a].Position, tissue[c].Position - tissue[a].Position);
            if (surface.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(surface, -kept) < 0) { (b, c) = (c, b); surface = -surface; }
            normals[a] += surface; normals[b] += surface; normals[c] += surface;
            faces.Add((a, b, c));
        }

        Vertex Shaded(int i)
        {
            var average = normals[i];
            var smooth = average.LengthSquared() > 1e-12f ? Vector3.Normalize(average) : -kept;
            return tissue[i] with { Normal = Vector3.Normalize(Vector3.Lerp(-kept, smooth, .88f)) };
        }

        if (shape is not { } cut)
        {
            var neckBind = rig.BindPosition[rig.Handle("Bip01 Neck") ?? bone];
            var vertebra = Project(neckBind - centre) * .72f;
            if (!Inside(vertebra)) vertebra = Vector2.Zero;
            AppendBone(vertebra, span * .145f, phase);
            return;
        }
        // The two sides share the same projected bone centres. The rig's joint, rather than world Y or
        // a fixed fraction, separates single humerus/femur sections from double forearm/calf sections.
        string hand = cut.LimbSlot % 2 == 0 ? "L" : "R";
        var (upper, middle, lower) = cut.LimbSlot < 2 ? ("UpperArm", "Forearm", "Hand") : ("Thigh", "Calf", "Foot");
        if (rig.Handle($"Bip01 {hand} {upper}") is not int first || rig.Handle($"Bip01 {hand} {middle}") is not int second
            || rig.Handle($"Bip01 {hand} {lower}") is not int third) return;
        var aBind = rig.BindPosition[first];
        var bBind = rig.BindPosition[second];
        var cBind = rig.BindPosition[third];
        float firstLength = Vector3.Distance(aBind, bBind), secondLength = Vector3.Distance(bBind, cBind);
        if (firstLength < 1e-5f || secondLength < 1e-5f) return;
        float distance = cut.Fraction * (firstLength + secondLength);
        bool paired = distance > firstLength;
        var bind = paired ? Vector3.Lerp(bBind, cBind, (distance - firstLength) / secondLength)
            : Vector3.Lerp(aBind, bBind, distance / firstLength);
        var joint = Project(bind - centre);
        // Some skin morphs shift away from the raw joint. Fit the bone inside that cut, without poking through the silhouette.
        if (!Inside(joint)) joint = Vector2.Zero;
        joint *= .64f;
        var shifted = joint + span * (.04f * axis + (cut.LimbSlot % 2 == 0 ? .065f : -.065f) * across);
        if (Inside(shifted)) joint = shifted;
        float offset = paired ? span * .108f : 0;
        AppendBone(joint - axis * offset, span * (paired ? .094f : .134f), phase);
        if (paired) AppendBone(joint + axis * offset, span * .073f, phase + 1.7f);

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
            Normal = -kept, Colour = colour, Bones = (uint)bone, Weights = Vector4.UnitX, Uv = Texture(point), Tangent = tangent,
        };

        void Triangle(Vertex a, Vertex b, Vertex c)
        {
            var surface = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (surface.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(surface, -kept) < 0) { (b, c) = (c, b); surface = -surface; }
            var face = Vector3.Normalize(surface);
            output.Add(a with { Normal = face }); output.Add(b with { Normal = face }); output.Add(c with { Normal = face });
        }

        void AppendBone(Vector2 origin, float radius, float seed)
        {
            if (!Inside(origin)) return;
            for (int i = 0; i < outline.Length; i++)
            {
                var p = outline[i]; var step = outline[(i + 1) % outline.Length] - p;
                float length = step.LengthSquared();
                float at = length > 1e-9f ? Math.Clamp(Vector2.Dot(origin - p, step) / length, 0, 1) : 0;
                radius = MathF.Min(radius, Vector2.Distance(origin, p + step * at) * .68f);
            }
            if (radius < .025f) return;
            const int points = 22;
            float depth = Sample(origin, 1 - Math.Clamp(Texture(origin).Length(), 0, .72f)).Depth;
            float top = MathF.Max(.012f * scale, depth * .26f);
            var mineral = Vector4.Lerp(palette.Bone, connective, .14f);
            var porous = Vector4.Lerp(palette.Marrow, palette.Bone, .21f);
            var core = At(origin + radius * (axis * .095f - across * .08f), depth * .66f, Shade(porous, .48f) with { W = Marrow });
            var shell = new Vertex[points];
            var crown = new Vertex[points];
            var lip = new Vertex[points];
            var cavity = new Vertex[points];
            var marrow = new Vertex[points];
            for (int i = 0; i < points; i++)
            {
                float angle = MathF.Tau * i / points;
                var direction = axis * MathF.Cos(angle) + across * (.73f * MathF.Sin(angle));
                float fracture = MathF.Sin(angle * 3 + seed), chip = MathF.Sin(angle * 7 - seed), pores = MathF.Sin(angle * 9 + seed * 2);
                var delta = direction * radius * (1 + .13f * fracture + .055f * chip);
                float opening = .58f + .08f * fracture - .05f * chip;
                var colour = Shade(Vector4.Lerp(mineral, porous, .12f + .11f * chip), .92f + .10f * fracture);
                float ridge = MathF.Max(.007f * scale, top + depth * (.095f * chip + .05f * fracture));
                shell[i] = At(origin + delta * 1.04f, depth * 1.12f, Shade(colour, .54f) with { W = Cortex });
                crown[i] = At(origin + delta, ridge, colour with { W = Cortex });
                lip[i] = At(origin + delta * opening, ridge + depth * (.12f + .065f * pores), Shade(colour, .82f) with { W = Cortex });
                cavity[i] = At(origin + delta * (opening * .91f), depth * .58f, Shade(porous, .55f + .13f * chip) with { W = Marrow });
                marrow[i] = At(origin + delta * (opening * .44f), depth * (.66f + .065f * pores),
                    Shade(porous, .56f + .18f * pores) with { W = Marrow });
            }
            for (int i = 0; i < points; i++)
            {
                int next = (i + 1) % points;
                Strip(shell, crown, i, next);
                Strip(crown, lip, i, next);
                Strip(lip, cavity, i, next);
                Strip(cavity, marrow, i, next);
                Triangle(marrow[i], marrow[next], core);
            }
        }

        void Strip(Vertex[] outer, Vertex[] inner, int i, int next)
        {
            Triangle(outer[i], outer[next], inner[i]);
            Triangle(inner[i], outer[next], inner[next]);
        }
    }

    static Vector2 LongAxis(Vector2[] outline)
    {
        float xx = 0, yy = 0, xy = 0;
        foreach (var point in outline) { xx += point.X * point.X; yy += point.Y * point.Y; xy += point.X * point.Y; }
        float angle = .5f * MathF.Atan2(2 * xy, xx - yy);
        return new(MathF.Cos(angle), MathF.Sin(angle));
    }

    static Bundle[] Bundles(float phase)
    {
        float drift = .1f * MathF.Sin(phase);
        return
        [
            new(new(-.43f, -.24f + drift), Direction(.32f + drift), new(.72f, .42f), .85f),
            new(new(.31f + drift, -.33f), Direction(-.55f + drift), new(.60f, .39f), 1.04f),
            new(new(.37f, .33f + drift), Direction(.58f - drift), new(.56f, .47f), .97f),
            new(new(-.28f - drift, .43f), Direction(-.20f - drift), new(.54f, .39f), .79f),
        ];

        static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    }

    static float Ease(float start, float end, float value)
    {
        float t = Math.Clamp((value - start) / (end - start), 0, 1);
        return t * t * (3 - 2 * t);
    }

    static Vector4 Shade(Vector4 colour, float factor) => colour * new Vector4(factor, factor, factor, 1);
}
