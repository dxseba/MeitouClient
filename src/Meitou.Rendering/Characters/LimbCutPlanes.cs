using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Rendering.Characters;

/// <summary>Four independent bind-space halfspaces, positive towards the original owner. The GPU reads the same packed rows.</summary>
internal readonly record struct LimbCutPlanes(Matrix4x4 Packed, uint Mask)
{
    public static LimbCutPlanes Resolve(SkeletonRig rig, IReadOnlyList<LimbCutState> cuts)
    {
        Span<Vector4> planes = stackalloc Vector4[4];
        planes.Clear();
        uint mask = 0;
        for (int i = 0; i < Math.Min(cuts.Count, 4); i++)
        {
            var cut = cuts[i];
            if (!cut.IsValid || (mask & 1u << cut.LimbSlot) != 0 || !LimbCutGeometry.TryPlane(rig, cut, out var plane)) continue;
            planes[cut.LimbSlot] = plane;
            mask |= 1u << cut.LimbSlot;
        }
        return new(new(planes[0].X, planes[0].Y, planes[0].Z, planes[0].W,
            planes[1].X, planes[1].Y, planes[1].Z, planes[1].W,
            planes[2].X, planes[2].Y, planes[2].Z, planes[2].W,
            planes[3].X, planes[3].Y, planes[3].Z, planes[3].W), mask);
    }

    public Vector4 Plane(int slot) => slot switch
    {
        0 => new(Packed.M11, Packed.M12, Packed.M13, Packed.M14),
        1 => new(Packed.M21, Packed.M22, Packed.M23, Packed.M24),
        2 => new(Packed.M31, Packed.M32, Packed.M33, Packed.M34),
        3 => new(Packed.M41, Packed.M42, Packed.M43, Packed.M44), _ => default,
    };

    public bool Has(int group) => group is >= 1 and <= 4 && (Mask & 1u << (group - 1)) != 0;

    public float Band(Vector3 position, int group, uint legacy = 0, uint partOnly = 0)
    {
        if (partOnly != 0 && group != (partOnly >> 8)) return -1000;
        if (Has(group)) return Vector4.Dot(Plane(group - 1), new(position, 1)) * (partOnly == 0 ? 1 : -1);
        if (partOnly != 0) return (partOnly & 255) * .1f - position.Y;
        uint height = group is >= 1 and <= 4 ? legacy >> (8 * (group - 1)) & 255 : 0;
        return height == 0 ? 1000 : position.Y - height * .1f;
    }

    /// <summary>Intersect all halfspaces as intervals: independently computed entry points cannot combine two cuts safely.</summary>
    public float? RayBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, int group,
        uint legacy = 0, uint partOnly = 0, CharacterTorsoCut? torso = null, int? torsoGroup = null)
    {
        if (partOnly != 0 && group != (partOnly >> 8)) return null;
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
        float band = Band(origin, group, legacy, partOnly);
        float slope = Band(origin + direction, group, legacy, partOnly) - band;
        if (!ClipRay(band, slope, ref near, ref far)) return null;
        if (torso is { } cut)
        {
            float at = TorsoCutSurface.Band(origin, torsoGroup ?? group, cut);
            float step = TorsoCutSurface.Band(origin + direction, torsoGroup ?? group, cut) - at;
            if (!ClipRay(at, step, ref near, ref far)) return null;
        }
        return near <= far ? near : null;
    }

    static bool ClipRay(float band, float slope, ref float near, ref float far)
    {
        if (MathF.Abs(slope) < 1e-9f) return band >= 0;
        float at = -band / slope;
        if (slope > 0) near = MathF.Max(near, at); else far = MathF.Min(far, at);
        return near <= far;
    }
}
