using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Physics;

internal sealed partial class RagdollSkinSurface
{
    internal (Vector3 Min, Vector3 Max)? FootBounds(SkeletonRig rig, ReadOnlySpan<Matrix4x4> worldSkin)
    {
        var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity); int count = 0;
        foreach (var triangle in triangles)
        {
            Add(triangle.A, worldSkin); Add(triangle.B, worldSkin); Add(triangle.C, worldSkin);
        }
        return count > 0 ? (min, max) : null;
        void Add(Point point, ReadOnlySpan<Matrix4x4> skin)
        {
            bool foot = false;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(point.Bones >> (k * 8) & 255);
                string? name = bone < rig.BoneCount ? rig.ByHandle[bone]?.Name : null;
                if (point.Weights[k] > .05f && name is not null && (name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal))) foot = true;
            }
            if (!foot) return;
            var at = Skin(point, skin);
            if (!float.IsFinite(at.LengthSquared())) return;
            min = Vector3.Min(min, at); max = Vector3.Max(max, at); count++;
        }
    }
}
