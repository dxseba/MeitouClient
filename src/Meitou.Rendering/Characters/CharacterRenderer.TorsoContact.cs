using System.Numerics;

namespace Meitou.Rendering.Characters;

internal sealed partial class CharacterRenderer
{
    readonly record struct ContactPoint(Vector3 Position, float Band, float LimbBand, float Influence, Vector3 Barycentric);
    internal readonly record struct SurfaceContact(int Triangle, Vector3 Barycentric, Vector3 Position);

    /// <summary>Closest retained posed skin point. preferMiddle measures bounds in the existing posing frame, using point only for arm-facing ties; defaults remain unrestricted.</summary>
    public Vector3? ClosestTorsoSurfacePoint(long key, in CharacterTorsoCut cut, Vector3 point, int? component = null, Matrix4x4? placement = null, bool preferUpperBand = false, bool preferMiddle = false, Matrix4x4? boundsFrame = null)
    {
        if (!states.TryGetValue(key, out var state) || state.Seen != updateStamp || !state.InWorld) return null;
        return ClosestTorsoContact(state, cut, point, component, placement, preferUpperBand, preferMiddle, boundsFrame, ContactLimbCuts(key))?.Position;
    }

    uint ContactLimbCuts(long key)
    {
        for (int i = 0; i < liveCount; i++) if (live[i].Key == key) return live[i].LimbCuts;
        return 0;
    }

    SurfaceContact? ClosestTorsoContact(CharState state, in CharacterTorsoCut cut, Vector3 point, int? component = null,
        Matrix4x4? placement = null, bool preferUpperBand = false, bool preferMiddle = false, Matrix4x4? boundsFrame = null,
        uint limbCuts = 0, bool axialOnly = false)
    {
        if (!float.IsFinite(point.LengthSquared()) || !TorsoCutSurface.TryNormalize(cut, out var kept)
            || state.Asset is not { IsIcon: false, BodySource: { } source } asset || state.Posed < 0) return null;
        var components = state.PosedRagdoll?.Components;
        if (component is not null && components is null) return null;
        var groups = woundMasks.BoneGroups(asset.Rig);
        var morphs = asset.Morph?.Poses.Deltas(asset.Morph.Weights, asset.Morph.CutHorns);
        var rebase = Matrix4x4.Identity;
        if (placement is { } world)
        {
            if (!Matrix4x4.Invert(state.World, out var inverse)) return null;
            rebase = inverse * world;
        }
        var reference = boundsFrame ?? placement ?? state.World;
        var toBounds = Matrix4x4.Identity;
        if (preferMiddle && !Matrix4x4.Invert(reference, out toBounds)) return null;
        Span<ContactPoint> polygon = stackalloc ContactPoint[8], clipped = stackalloc ContactPoint[8];
        Span<int> ids = stackalloc int[12];
        Span<float> votes = stackalloc float[12];
        SurfaceContact? closest = null;
        float distance = float.PositiveInfinity, armDistance = float.PositiveInfinity;
        var minimum = new Vector3(float.PositiveInfinity); var maximum = new Vector3(float.NegativeInfinity);
        bool measureBounds = preferUpperBand || preferMiddle;
        for (int pass = 0; pass < (measureBounds ? 2 : 1); pass++)
        {
        for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
        {
            if (axialOnly && !AxialContactTriangle(asset.Rig, source, triangle)) continue;
            bool hidden = false;
            int count = 0;
            for (int vertex = 0; vertex < 3; vertex++)
            {
                var skin = source[triangle + vertex];
                int group = TorsoCutSurface.Group(skin, groups), strongest = 0;
                for (int k = 1; k < 4; k++) if (skin.Weights[k] > skin.Weights[strongest]) strongest = k;
                int primary = (int)(skin.Bones >> (strongest * 8) & 255);
                bool head = false;
                for (int h = primary; h >= 0 && h < asset.Rig.Parent.Length; h = asset.Rig.Parent[h])
                    if (h == asset.HeadBone) { head = true; break; }
                if (asset.Appearance is { } look)
                {
                    int hiddenLimbs = look.HiddenLimbs & ~CutBits(limbCuts);
                    float below = group <= 2 ? look.Female ? 14.1f : 13.83f : 8.6f;
                    // BodySource precedes MarkBody, so derive the same anatomical hide rule rather than reading absent colour marks.
                    if (look.Headless && head || group is >= 1 and <= 4 && (hiddenLimbs & (1 << (group - 1))) != 0 && skin.Position.Y < below) hidden = true;
                }
                float influence = component is null ? 1 : 0;
                if (component is { } wanted && components is not null)
                    for (int k = 0; k < 4; k++)
                    {
                        int bone = (int)(skin.Bones >> (k * 8) & 255);
                        float weight = skin.Weights[k];
                        if (weight <= 0 || bone >= components.Length) continue;
                        int id = components[bone], at = 0;
                        while (at < count && ids[at] != id) at++;
                        if (at == count) { ids[count] = id; votes[count++] = 0; }
                        votes[at] += weight;
                        if (id == wanted) influence += weight;
                    }
                float band = TorsoCutSurface.Band(skin.Position, group, kept);
                uint height = group is >= 1 and <= 4 ? limbCuts >> (8 * (group - 1)) & 255 : 0;
                float limbBand = height == 0 ? 1000 : skin.Position.Y - height * 0.1f;
                uint slot = BitConverter.SingleToUInt32Bits(skin.Colour.X);
                if (morphs is not null && slot > 0 && slot <= morphs.Length)
                {
                    var delta = morphs[slot - 1];
                    skin = skin with { Position = skin.Position + new Vector3(delta.X, delta.Y, delta.Z) };
                }
                polygon[vertex] = new(Vector3.Transform(PoseCutVertex(skin, state), rebase), band, limbBand, influence,
                    vertex == 0 ? Vector3.UnitX : vertex == 1 ? Vector3.UnitY : Vector3.UnitZ);
            }
            if (hidden) continue;
            if (component is { } selected)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                    if (best < 0 || votes[i] > votes[best] || votes[i] == votes[best] && ids[i] < ids[best]) best = i;
                if (best < 0 || ids[best] != selected) continue;
            }
            int retained = ClipContact(polygon, clipped, 3, plane: 0);
            retained = ClipContact(clipped, polygon, retained, plane: 1);
            Span<ContactPoint> face = polygon;
            if (component is not null)
            {
                retained = ClipContact(polygon, clipped, retained, plane: 2);
                face = clipped;
            }
            if (measureBounds && pass == 0)
            {
                for (int i = 1; i + 1 < retained; i++)
                {
                    if (Vector3.Cross(face[i].Position - face[0].Position, face[i + 1].Position - face[0].Position).LengthSquared() < 1e-10f) continue;
                    var a = Vector3.Transform(face[0].Position, toBounds); var b = Vector3.Transform(face[i].Position, toBounds); var c = Vector3.Transform(face[i + 1].Position, toBounds);
                    minimum = Vector3.Min(minimum, Vector3.Min(a, Vector3.Min(b, c)));
                    maximum = Vector3.Max(maximum, Vector3.Max(a, Vector3.Max(b, c)));
                }
                continue;
            }
            if (preferUpperBand && !preferMiddle)
            {
                Span<ContactPoint> upper = component is null ? clipped : polygon;
                retained = ClipContact(face, upper, retained, plane: 3, floor: maximum.Y - MathF.Max(0.6f, (maximum.Y - minimum.Y) * 0.12f));
                face = upper;
            }
            for (int i = 1; i + 1 < retained; i++)
            {
                if (Vector3.Cross(face[i].Position - face[0].Position, face[i + 1].Position - face[0].Position).LengthSquared() < 1e-10f) continue;
                var aim = preferMiddle ? Vector3.Transform((minimum + maximum) * 0.5f, reference) : point;
                var candidate = ClosestContactTriangle(aim, face[0].Position, face[i].Position, face[i + 1].Position);
                float gap = Vector3.DistanceSquared(aim, candidate), armGap = Vector3.DistanceSquared(point, candidate);
                bool nearer = preferMiddle ? gap < distance - 1e-5f || MathF.Abs(gap - distance) <= 1e-5f && armGap < armDistance : gap < distance;
                if (float.IsFinite(gap) && nearer)
                {
                    var bary = ContactBarycentric(candidate, face[0].Position, face[i].Position, face[i + 1].Position);
                    var material = face[0].Barycentric * bary.X + face[i].Barycentric * bary.Y + face[i + 1].Barycentric * bary.Z;
                    distance = gap; armDistance = armGap; closest = new(triangle, material, candidate);
                }
            }
        }
        if (measureBounds && pass == 0 && (!float.IsFinite(minimum.LengthSquared()) || !float.IsFinite(maximum.LengthSquared()))) return null;
        }
        return closest;
    }

    static int ClipContact(ReadOnlySpan<ContactPoint> source, Span<ContactPoint> target, int count, int plane, float floor = 0)
    {
        if (count == 0) return 0;
        int kept = 0;
        var previous = source[count - 1];
        float before = ContactBand(previous, plane, floor);
        for (int i = 0; i < count; i++)
        {
            var current = source[i];
            float after = ContactBand(current, plane, floor);
            if (before < 0 && after > 0 || before > 0 && after < 0)
            {
                float t = before / (before - after);
                target[kept++] = new(Vector3.Lerp(previous.Position, current.Position, t),
                    previous.Band + (current.Band - previous.Band) * t, previous.LimbBand + (current.LimbBand - previous.LimbBand) * t,
                    previous.Influence + (current.Influence - previous.Influence) * t, Vector3.Lerp(previous.Barycentric, current.Barycentric, t));
            }
            if (after >= 0) target[kept++] = current;
            (previous, before) = (current, after);
        }
        return kept;
    }

    static float ContactBand(ContactPoint point, int plane, float floor) => plane switch { 0 => point.Band, 1 => point.LimbBand, 2 => point.Influence - 0.5f, 3 => point.Position.Y - floor, _ => floor - point.Position.Y };

    static bool AxialContactTriangle(Meitou.Data.Characters.SkeletonRig rig, Vertex[] source, int triangle)
    {
        float axial = 0, other = 0;
        for (int i = 0; i < 3; i++)
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(source[triangle + i].Bones >> (k * 8) & 255);
                float weight = source[triangle + i].Weights[k];
                if (weight <= 0 || bone >= rig.ByHandle.Length) continue;
                bool head = false;
                for (int at = bone; at >= 0; at = rig.Parent[at])
                    if (rig.ByHandle[at]?.Name == "Bip01 Head") { head = true; break; }
                string name = rig.ByHandle[bone]?.Name ?? "";
                if (!head && WoundMasks.BodyGroup(name) == 0) axial += weight;
                else other += weight;
            }
        return axial > 0 && axial >= other;
    }

    static Vector3 ContactBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var first = b - a; var second = c - a; var offset = p - a;
        float aa = Vector3.Dot(first, first), ab = Vector3.Dot(first, second), bb = Vector3.Dot(second, second);
        float pa = Vector3.Dot(offset, first), pb = Vector3.Dot(offset, second), denominator = aa * bb - ab * ab;
        if (MathF.Abs(denominator) < 1e-14f) return Vector3.UnitX;
        float y = (bb * pa - ab * pb) / denominator, z = (aa * pb - ab * pa) / denominator;
        return new(1 - y - z, y, z);
    }

    static Vector3 ClosestContactTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a;
        if (Vector3.Cross(ab, ac).LengthSquared() < 1e-12f)
        {
            var first = ClosestContactEdge(point, a, b); var second = ClosestContactEdge(point, b, c); var third = ClosestContactEdge(point, c, a);
            var best = Vector3.DistanceSquared(point, first) < Vector3.DistanceSquared(point, second) ? first : second;
            return Vector3.DistanceSquared(point, best) < Vector3.DistanceSquared(point, third) ? best : third;
        }
        var ap = point - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = point - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = point - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denominator = 1 / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    static Vector3 ClosestContactEdge(Vector3 point, Vector3 a, Vector3 b)
    {
        var edge = b - a;
        return a + edge * (edge.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(point - a, edge) / edge.LengthSquared(), 0, 1) : 0);
    }
}
