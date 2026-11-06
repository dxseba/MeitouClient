using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Physics;

internal sealed partial class RagdollSkinSurface
{
    internal (Vector3 A, Vector3 B, Vector3 C)? EquipmentSupport(SkeletonRig rig, Vector3 at, int group)
    {
        (Vector3, Vector3, Vector3)? result = null;
        float nearest = float.MaxValue;
        foreach (var triangle in triangles)
        {
            if (!Matches(triangle.A) || !Matches(triangle.B) || !Matches(triangle.C)) continue;
            var a = triangle.A.Position; var b = triangle.B.Position; var c = triangle.C.Position;
            if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-10f) continue;
            var bary = ClosestBarycentric(at, a, b, c);
            float distance = Vector3.DistanceSquared(at, a * bary.X + b * bary.Y + c * bary.Z);
            if (distance < nearest) { nearest = distance; result = (a, b, c); }
        }
        return result;

        bool Matches(Point point)
        {
            int strongest = 0;
            for (int k = 1; k < 4; k++) if (point.Weights[k] > point.Weights[strongest]) strongest = k;
            int bone = (int)(point.Bones >> (8 * strongest) & 255);
            return bone < rig.BoneCount && CutWeaponDrops.Group(rig.ByHandle[bone]?.Name ?? "") == group;
        }
    }
}
