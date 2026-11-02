using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Simulation.Ragdolls;

internal sealed partial class RagdollSkinSurface
{
    // Original posed triangle + barycentrics matches the renderer's interpolated clip band; re-skinning a blended edge vertex would drift.
    internal sealed record Grip(Triangle Triangle, Vector3 Barycentric, float RootShare)
    {
        public Vector3 Posed(ReadOnlySpan<Matrix4x4> skin) => Skin(Triangle.A, skin) * Barycentric.X
            + Skin(Triangle.B, skin) * Barycentric.Y + Skin(Triangle.C, skin) * Barycentric.Z;
    }

    internal Grip? FindGrip(CharacterAppearance look, SkeletonRig rig, Vector4 plane, int side, bool fullBody, int[] components, int root,
        RagdollDefinition definition, Matrix4x4[] skin, Vector3 palm, float[]? limbCuts = null)
    {
        var deltas = new Dictionary<(ushort, uint), Vector3>();
        int morphTarget = morphs.Where(p => p.Target > 0).Select(p => (int)p.Target).DefaultIfEmpty(-1).First();
        bool cutHorns = look.Character?.GetBool("shaved") == true && morphs.Any(p => p.Name == "bone_horns_top_short");
        foreach (var pose in morphs)
            if (pose.Target == morphTarget && look.PoseWeights.TryGetValue(pose.Name, out float stored)
                && CharacterShape.BakeWeight(pose.Name, stored, cutHorns) is { } weight)
                foreach (var vertex in pose.Vertices)
                    deltas[(pose.Target, vertex.Index)] = deltas.GetValueOrDefault((pose.Target, vertex.Index)) + vertex.Offset * weight;
        Grip? best = null;
        float nearest = float.MaxValue;
        var surfaces = new List<(Point[] Points, Vector3[] Posed, Vector3[] Polygon)>();
        float minimumY = float.MaxValue, maximumY = float.MinValue;
        foreach (var triangle in triangles)
        {
            Point[] original = [triangle.A, triangle.B, triangle.C];
            var votes = new Dictionary<int, float>();
            var roots = new float[3];
            bool hidden = false;
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 4; k++)
                {
                    int h = (int)(original[i].Bones >> (8 * k) & 255);
                    float w = original[i].Weights[k];
                    if (w <= 0 || h >= components.Length) continue;
                    int group = Group(h);
                    if (w >= 0.5f && ((group is >= 1 and <= 4 && (look.HiddenLimbs & 1 << (group - 1)) != 0 && (limbCuts is null || limbCuts[group - 1] <= 0)) || look.Headless && IsHead(h))) hidden = true;
                    int at = h;
                    while (at >= 0 && (at >= definition.BodyOfBone.Length || definition.BodyOfBone[at] < 0)) at = rig.Parent[at];
                    // A lost calf/arm cannot become a torso grip merely because its missing bones fall back to the pelvis.
                    if (group is >= 1 and <= 4 && (at < 0 || Group(at) != group)) continue;
                    int component = components[h];
                    votes[component] = votes.GetValueOrDefault(component) + w;
                    if (component == root) roots[i] += w;
                }
            if (hidden || votes.Count == 0 || votes.MaxBy(p => p.Value).Key != root) continue;
            var polygon = new List<Vector3> { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            var bands = original.Select(p => Band(p)).ToArray();
            Clip(p => Vector3.Dot(p, new(bands[0], bands[1], bands[2])));
            Clip(p => Vector3.Dot(p, new(roots[0], roots[1], roots[2])) - 0.5f);
            if (limbCuts is not null)
            {
                var limbBands = original.Select(p =>
                {
                    int strongest = 0;
                    for (int k = 1; k < 4; k++) if (p.Weights[k] > p.Weights[strongest]) strongest = k;
                    int group = Group((int)(p.Bones >> (strongest * 8) & 255));
                    return group is >= 1 and <= 4 && limbCuts[group - 1] >= 0
                        ? limbCuts[group - 1] == 0 ? -1000 : p.Position.Y - limbCuts[group - 1] : 1000;
                }).ToArray();
                Clip(p => Vector3.Dot(p, new(limbBands[0], limbBands[1], limbBands[2])));
            }
            if (polygon.Count < 3) continue;
            var shaped = original.Select(p => p with { Position = p.Position + deltas.GetValueOrDefault((p.Target, p.Vertex)) }).ToArray();
            var posed = shaped.Select(p => Skin(p, skin)).ToArray();
            Vector3 At(Vector3 bary) => posed[0] * bary.X + posed[1] * bary.Y + posed[2] * bary.Z;
            bool area = false;
            for (int i = 1; i + 1 < polygon.Count; i++)
                area |= Vector3.Cross(At(polygon[i]) - At(polygon[0]), At(polygon[i + 1]) - At(polygon[0])).LengthSquared() >= 1e-10f;
            if (!area) continue;
            surfaces.Add((shaped, posed, [.. polygon]));
            foreach (var bary in polygon)
            {
                float y = posed[0].Y * bary.X + posed[1].Y * bary.Y + posed[2].Y * bary.Z;
                minimumY = MathF.Min(minimumY, y); maximumY = MathF.Max(maximumY, y);
            }
            void Clip(Func<Vector3, float> distance)
            {
                var result = new List<Vector3>();
                for (int i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                    float da = distance(a), db = distance(b);
                    if (da >= 0) result.Add(a);
                    if ((da < 0) != (db < 0)) result.Add(Vector3.Lerp(a, b, da / (da - db)));
                }
                polygon = result;
            }
            float Band(Point p)
            {
                int strongest = 0;
                for (int k = 1; k < 4; k++) if (p.Weights[k] > p.Weights[strongest]) strongest = k;
                int h = (int)(p.Bones >> (strongest * 8) & 255), group = Group(h);
                if (!fullBody && group is 1 or 2 or 6) return side * 1000;
                if (!fullBody && group is 3 or 4) return -side * 1000;
                return side * (Vector3.Dot(new(plane.X, plane.Y, plane.Z), p.Position) + plane.W);
            }
        }
        float upper = Carrying.CarryPose.UpperGripBand(minimumY, maximumY);
        foreach (var surface in surfaces)
        {
            var posed = surface.Posed;
            Vector3 Posed(Vector3 bary) => posed[0] * bary.X + posed[1] * bary.Y + posed[2] * bary.Z;
            var polygon = new List<Vector3>();
            for (int i = 0; i < surface.Polygon.Length; i++)
            {
                var a = surface.Polygon[i]; var b = surface.Polygon[(i + 1) % surface.Polygon.Length];
                float da = Posed(a).Y - upper, db = Posed(b).Y - upper;
                if (da >= 0) polygon.Add(a);
                if ((da < 0) != (db < 0)) polygon.Add(Vector3.Lerp(a, b, da / (da - db)));
            }
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                var a = Posed(polygon[0]); var b = Posed(polygon[i]); var c = Posed(polygon[i + 1]);
                if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-10f) continue;
                var bary = ClosestBarycentric(palm, a, b, c);
                var point = a * bary.X + b * bary.Y + c * bary.Z;
                float distance = Vector3.DistanceSquared(point, palm);
                if (distance >= nearest) continue;
                nearest = distance;
                var originalBary = polygon[0] * bary.X + polygon[i] * bary.Y + polygon[i + 1] * bary.Z;
                var rootWeights = surface.Points.Select(point =>
                {
                    float share = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int bone = (int)(point.Bones >> (k * 8) & 255);
                        if (bone < components.Length && components[bone] == root) share += point.Weights[k];
                    }
                    return share;
                }).ToArray();
                best = new(new(surface.Points[0], surface.Points[1], surface.Points[2]), originalBary,
                    Vector3.Dot(originalBary, new(rootWeights[0], rootWeights[1], rootWeights[2])));
            }
        }
        return best;

        bool IsHead(int bone)
        {
            for (int h = bone; h >= 0; h = rig.Parent[h])
                if (rig.ByHandle[h]?.Name is "Bip01 Head" or "Bip01 Jaw") return true;
            return false;
        }

        int Group(int bone)
        {
            var name = rig.ByHandle[bone]?.Name ?? "";
            if (name == "Bip01 Jaw") return 6;
            bool left = name.Contains(" L ", StringComparison.Ordinal), right = name.Contains(" R ", StringComparison.Ordinal);
            if ((!left && !right) || name.Contains("Clavicle", StringComparison.Ordinal)) return 0;
            bool leg = name.Contains("Thigh", StringComparison.Ordinal) || name.Contains("Calf", StringComparison.Ordinal)
                || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal);
            return (leg ? 3 : 1) + (right ? 1 : 0);
        }
    }

    static Vector3 Skin(Point point, ReadOnlySpan<Matrix4x4> skin)
    {
        var result = Vector3.Zero;
        for (int k = 0; k < 4; k++)
        {
            int bone = (int)(point.Bones >> (8 * k) & 255);
            if (point.Weights[k] > 0 && bone < skin.Length) result += Vector3.Transform(point.Position, skin[bone]) * point.Weights[k];
        }
        return result;
    }

    internal static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return Vector3.UnitX;
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return Vector3.UnitY;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { float t = d1 / (d1 - d3); return new(1 - t, t, 0); }
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return Vector3.UnitZ;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { float t = d2 / (d2 - d6); return new(1 - t, 0, t); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { float t = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new(0, 1 - t, t); }
        float inv = 1 / (va + vb + vc), v = vb * inv, w = vc * inv;
        return new(1 - v - w, v, w);
    }
}