using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality.Rendering;

internal readonly record struct HeadReliefFrame(Vector3 Centre, Vector3 Normal, Vector3 Along, float HalfLength, float HalfWidth)
{
    public Vector3 Side => Vector3.Normalize(Vector3.Cross(Normal, Along));
}

internal sealed record HeadReliefPlan(IReadOnlyList<CutVolume> Skin, IReadOnlyList<CutVolume> Inside,
    IReadOnlyList<CutVolume> Hair, IReadOnlyList<CutHalfspace> Rules, OgreMesh? Relief, HeadReliefFrame Frame);

/// <summary>
/// The pin's blade profiles and projected relief, expressed as CPU pocket subtraction rather than shader discard.
/// Only a projected, outward-facing local head surface is opened; the opposite scalp remains intact.
/// </summary>
internal static class HeadReliefGeometry
{
    const int Sections = 48, PocketSections = 12;
    static readonly Vector3 DefaultNormal = Vector3.Normalize(new(.81257f, .30728f, .49528f));

    public static HeadReliefPlan? Build(OgreMesh body, SkeletonRig rig, CharacterAppearance appearance, in SevereHeadWoundState state)
    {
        if (state.Kind == SevereHeadWoundKind.None || rig.Handle("Bip01 Head") is not { } headBone) return null;
        var groups = MeshCut.BoneGroups(rig);
        var triangles = new List<Vertex>();
        foreach (var part in Model.Build(body, rig.BoneCount, gameBinormals: true).Parts)
        {
            var morph = MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, appearance.PoseWeights, appearance.Character?.GetBool("shaved") == true);
            for (int i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                if (!Enumerable.Range(0, 3).Any(k => MeshCut.Group(part.Vertices[part.Indices[i + k]], groups) is 6 or 7)) continue;
                for (int k = 0; k < 3; k++)
                {
                    uint index = part.Indices[i + k];
                    triangles.Add(part.Vertices[index] with { Position = part.Vertices[index].Position + morph[index] });
                }
            }
        }
        if (triangles.Count == 0) return null;
        var (min, max) = AnatomyGeometry.Bounds(HeadPoints(body, rig, appearance));
        var extent = max - min;
        if (extent.X <= 0 || extent.Y <= 0 || extent.Z <= 0) return null;
        float fit = Math.Clamp(MathF.Min(extent.Y / 2.8f, extent.X / 1.7f), .45f, 1.7f);
        bool jaw = state.Kind == SevereHeadWoundKind.JawLost;
        int mechanism = (int)state.Mechanism;
        var normal = jaw ? Vector3.UnitZ : state.Placement is { IsValid: true } placed ? Unit(placed.Normal) : DefaultNormal;
        var across = Unit(Vector3.Cross(Vector3.UnitY, normal));
        var up = Unit(Vector3.Cross(normal, across));
        int direction = state.Profile % 3;
        var along = jaw ? Vector3.UnitX : state.Placement is { IsValid: true } p ? Unit(p.Tangent - normal * Vector3.Dot(p.Tangent, normal))
            : direction switch { 1 => Unit(-across * .83f + up * .55f), 2 => up, _ => Unit(across * .83f + up * .55f) };
        var estimated = jaw ? new Vector3((min.X + max.X) * .5f, min.Y + extent.Y * .20f, (min.Z + max.Z) * .5f)
            : state.Placement is { IsValid: true } placement ? placement.Point
            : new Vector3(min.X + extent.X * .70f, min.Y + extent.Y * .78f, min.Z + extent.Z * .72f);
        // The saved estimate is human bind space. A head outside it gets the same relative forehead location on its own box.
        if (!jaw && Vector3.DistanceSquared(Vector3.Clamp(estimated, min, max), estimated) > fit * fit)
            estimated = new((min.X + max.X) * .5f, min.Y + extent.Y * .75f, (min.Z + max.Z) * .5f);
        float reach = Math.Max(2, extent.Length());
        if (Project(triangles, estimated, normal, reach) is not { } projected)
        {
            normal = Vector3.UnitZ;
            along = jaw ? Vector3.UnitX : Unit(along - normal * Vector3.Dot(along, normal));
            if (Project(triangles, estimated, normal, reach) is not { } fallback) return null;
            projected = fallback;
        }
        normal = Unit(projected.Normal);
        along = Unit(along - normal * Vector3.Dot(along, normal));
        var (length, width) = Dimensions(state.Profile, mechanism);
        length *= fit * (state.Placement?.LengthScale ?? 1);
        width *= fit * (state.Placement?.WidthScale ?? 1);
        if (jaw) { length = extent.X * .48f; width = extent.Y * .23f; }
        var frame = new HeadReliefFrame(projected.Position, normal, along, length, width);
        uint blood = unchecked((uint)appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
        bool broken = state.Kind == SevereHeadWoundKind.BrokenSkull;
        var relief = Patch(triangles, frame, headBone, mechanism, broken, jaw, blood, reach);
        var skin = Pockets(frame, mechanism, .65f * fit, .65f * fit, false, false);
        var hair = Pockets(frame, mechanism, .65f * fit, reach, false, false).Select(pocket => pocket with { FrontOnly = false }).ToArray();
        var inside = broken ? Pockets(frame, mechanism, .65f * fit, .65f * fit, true, !appearance.Race.GetBool("is robot"))
            : jaw ? skin : Array.Empty<CutVolume>();
        CutHalfspace[] rules = jaw ? [new(new(0, 1, 0, -1_000_000), 1, 6)] : [];
        return new(skin, inside, hair, rules, relief.Count == 0 ? null : MeshCut.FromTriangles(relief, body.SkeletonName), frame);
    }

    /// <summary>The pin excludes articulated horn slots and torso UV charts when fitting the actual cranial cavity.</summary>
    internal static Vector3[] HeadPoints(OgreMesh body, SkeletonRig rig, CharacterAppearance appearance)
    {
        var groups = MeshCut.BoneGroups(rig);
        var points = new List<(Vector3 Position, bool HeadChart)>();
        foreach (var part in Model.Build(body, rig.BoneCount).Parts)
        {
            bool shared = body.SubMeshes[part.SubMeshIndex].UseSharedVertices;
            var horns = body.Poses.Where(p => p.Target == (shared ? 0 : part.SubMeshIndex + 1) && p.Name.Contains("horn", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Vertices).Where(v => v.Offset.LengthSquared() > 0).Select(v => v.Index).ToHashSet();
            var morph = MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, appearance.PoseWeights, appearance.Character?.GetBool("shaved") == true);
            for (int i = 0; i < part.Vertices.Length; i++)
                if (!horns.Contains((uint)i) && MeshCut.Group(part.Vertices[i], groups) is 6 or 7)
                    points.Add((part.Vertices[i].Position + morph[i], part.Vertices[i].Uv.Y < 0));
        }
        bool hasHeadChart = points.Any(p => p.HeadChart);
        return points.Where(p => !hasHeadChart || p.HeadChart).Select(p => p.Position).ToArray();
    }

    static CutVolume[] Pockets(HeadReliefFrame frame, int mechanism, float back, float front, bool cleft, bool boneOnly)
    {
        var result = new List<CutVolume>();
        for (int row = 0; row < PocketSections; row++)
        {
            float a = frame.HalfLength * (2f * row / PocketSections - 1), b = frame.HalfLength * (2f * (row + 1) / PocketSections - 1);
            float wa = ShapeWidth(a, frame.HalfLength, frame.HalfWidth, mechanism), wb = ShapeWidth(b, frame.HalfLength, frame.HalfWidth, mechanism);
            if (cleft) { wa *= ShapeCleft(a, frame.HalfLength, mechanism); wb *= ShapeCleft(b, frame.HalfLength, mechanism); }
            wa = Math.Max(wa, .0001f); wb = Math.Max(wb, .0001f);
            Vector3 P(float u, float side) => frame.Centre + frame.Along * u + frame.Side * (ShapeBend(u, mechanism) + side);
            Vector3[] polygon = [P(a, -wa), P(b, -wb), P(b, wb), P(a, wa)];
            var planes = new List<CutHalfspace>();
            for (int edge = 0; edge < polygon.Length; edge++)
            {
                var n = Unit(Vector3.Cross(frame.Normal, polygon[(edge + 1) % polygon.Length] - polygon[edge]));
                planes.Add(new(new(n, -Vector3.Dot(n, polygon[edge])), MorphedDistance: true));
            }
            planes.Add(new(new(frame.Normal, -Vector3.Dot(frame.Normal, frame.Centre - frame.Normal * back)), MorphedDistance: true));
            planes.Add(new(new(-frame.Normal, Vector3.Dot(frame.Normal, frame.Centre + frame.Normal * front)), MorphedDistance: true));
            result.Add(new(planes, frame.Normal, BoneOnly: boneOnly));
        }
        return result.ToArray();
    }

    static List<Vertex> Patch(List<Vertex> source, HeadReliefFrame frame, int bone, int mechanism, bool broken, bool jaw, uint blood, float reach)
    {
        float[] bands = jaw ? [-1.2f, -.85f, .85f, 1.2f] : broken ? [-1.2f, -.85f, -.52f, -.24f, -.24f, .24f, .24f, .52f, .85f, 1.2f]
            : [-1.2f, -.85f, -.52f, 0, .52f, .85f, 1.2f];
        float[] depths = jaw ? [-.004f, -.04f, -.04f, -.004f] : broken ? [-.004f, -.012f, -.045f, -.045f, -.13f, -.13f, -.045f, -.045f, -.012f, -.004f]
            : [-.004f, -.012f, -.045f, -.045f, -.045f, -.012f, -.004f];
        var points = new Vertex?[Sections + 1, bands.Length];
        for (int row = 0; row <= Sections; row++)
        {
            float u = frame.HalfLength * (2f * row / Sections - 1), width = ShapeWidth(u, frame.HalfLength, frame.HalfWidth, mechanism);
            for (int band = 0; band < bands.Length; band++)
            {
                float side = bands[band];
                if (broken && !jaw && band is >= 3 and <= 6) side = MathF.Sign(side) * ShapeCleft(u, frame.HalfLength, mechanism);
                var p = frame.Centre + frame.Along * u + frame.Side * (ShapeBend(u, mechanism) + side * width);
                if (Project(source, p, frame.Normal, reach) is not { } hit) continue;
                float lift = width / frame.HalfWidth * .004f * MathF.Sin(u * 19 + side * 2);
                bool flesh = jaw || MathF.Abs(side) >= .85f, inner = broken && band is 4 or 5;
                float grain = .94f + .06f * MathF.Sin(u * 31 + side * 5);
                float exposure = Math.Clamp(1.3f - MathF.Abs(u + .03f) / .36f, 0, 1) * (side > 0 ? .60f : 1);
                var tissue = AnatomyGeometry.Recolour(new(.28f, .025f, .03f, 1), blood);
                var colour = (flesh ? tissue : inner ? AnatomyGeometry.Recolour(new(.32f, .19f, .15f, 1), blood)
                    : Vector4.Lerp(tissue, new(.61f, .55f, .44f, 1), exposure)) * grain;
                points[row, band] = new Vertex { Position = hit.Position + frame.Normal * (depths[band] * Math.Clamp(width / frame.HalfWidth * 4, 0, 1) + lift),
                    Normal = frame.Normal, Tangent = new(frame.Along, 1), Colour = colour with { W = 1 }, Bones = (uint)bone, Weights = Vector4.UnitX };
            }
        }
        var output = new List<Vertex>();
        void Triangle(Vertex? a, Vertex? b, Vertex? c)
        {
            if (a is not { } va || b is not { } vb || c is not { } vc) return;
            var cross = Vector3.Cross(vb.Position - va.Position, vc.Position - va.Position);
            if (cross.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(cross, frame.Normal) < 0) (vb, vc) = (vc, vb);
            output.Add(va); output.Add(vb); output.Add(vc);
        }
        for (int row = 0; row < Sections; row++)
            for (int band = 0; band + 1 < bands.Length; band++)
            {
                if (broken && band == 4 || jaw && band == 1) continue;
                Triangle(points[row, band], points[row + 1, band], points[row, band + 1]);
                Triangle(points[row + 1, band], points[row + 1, band + 1], points[row, band + 1]);
            }
        return output;
    }

    static Vertex? Project(List<Vertex> source, Vector3 point, Vector3 normal, float reach)
    {
        var start = point + normal * reach;
        var direction = -normal;
        float best = float.MaxValue;
        Vertex? found = null;
        for (int i = 0; i + 2 < source.Count; i += 3)
        {
            var a = source[i]; var b = source[i + 1]; var c = source[i + 2];
            var e1 = b.Position - a.Position; var e2 = c.Position - a.Position;
            var cross = Vector3.Cross(direction, e2);
            float determinant = Vector3.Dot(e1, cross);
            if (MathF.Abs(determinant) < 1e-7f) continue;
            var delta = start - a.Position;
            float u = Vector3.Dot(delta, cross) / determinant;
            if (u < -.00001f || u > 1.00001f) continue;
            var q = Vector3.Cross(delta, e1);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < -.00001f || u + v > 1.00001f) continue;
            float t = Vector3.Dot(e2, q) / determinant;
            if (t < 0 || t > reach * 2 || t >= best) continue;
            var n = Unit(a.Normal * (1 - u - v) + b.Normal * u + c.Normal * v);
            if (Vector3.Dot(n, normal) < .05f) continue;
            best = t;
            found = a with { Position = start + direction * t, Normal = n };
        }
        return found;
    }

    static Vector3 Unit(Vector3 p) => p.LengthSquared() > 1e-12f ? Vector3.Normalize(p) : Vector3.UnitX;
    static (float Length, float Width) Dimensions(int profile, int mechanism)
    {
        bool heavy = profile >= 3;
        if (mechanism == 2) return heavy ? (.24f, .12f) : (.20f, .095f);
        if (mechanism == 3) return heavy ? (.36f, .23f) : (.30f, .19f);
        return ((profile % 3) switch { 1 => .53f * (heavy ? 1.08f : 1), 2 => heavy ? .58f : .52f, _ => .62f * (heavy ? 1.12f : 1) }, .105f * (heavy ? 1.3f : 1));
    }
    static float ShapeWidth(float u, float length, float width, int mechanism) => mechanism switch
    {
        2 => width * MathF.Pow(MathF.Max(0, 1 - MathF.Abs(u) / length), .8f) * (1 + .08f * MathF.Sin(u * 69)),
        3 => width * MathF.Pow(MathF.Max(0, 1 - MathF.Abs(u) / length), .55f) * (1 + .20f * MathF.Sin(u * 51) + .12f * MathF.Sin(u * 97) + .08f * MathF.Cos(u * 39)),
        _ => width * MathF.Pow(MathF.Max(0, 1 - MathF.Abs(u) / length), .65f) * (1 + .10f * MathF.Sin(u * 47) + .06f * MathF.Sin(u * 83)),
    };
    static float ShapeBend(float u, int mechanism) => mechanism switch
    {
        2 => .003f * MathF.Sin(u * 53), 3 => .025f * MathF.Sin(u * 37) + .010f * MathF.Sin(u * 83),
        _ => .008f * MathF.Sin(u * 15) + .003f * MathF.Sin(u * 37),
    };
    static float ShapeCleft(float u, float length, int mechanism) => mechanism switch
    {
        2 => .34f + .12f * MathF.Exp(-MathF.Pow(u / (length * .45f), 2)),
        3 => Math.Clamp(.40f + .14f * MathF.Sin(u * 43) + .10f * MathF.Exp(-MathF.Pow(u / (length * .6f), 2)), .25f, .65f),
        _ => .24f + .20f * MathF.Exp(-MathF.Pow((u - .12f) / .20f, 2)),
    };
}
