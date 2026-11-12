using System.Numerics;
using Meitou.Data.Characters;
namespace Meitou.Brutality.Physics;

internal sealed partial class AdvancedFragmentScene
{
    static void EnsureAnchors(Piece piece, SkeletonRig rig)
    {
        if (piece.Anchors is { } known && known.Length == rig.BoneCount && piece.Offsets?.Length == rig.BoneCount) return;
        var initial = piece.InitialPose.Length == piece.Pose.Length ? piece.InitialPose : piece.Pose;
        var world = piece.InitialBones;
        if (world?.Length != rig.BoneCount)
        {
            world = new RigidTransform[rig.BoneCount];
            int root = piece.Definition.Root;
            var bindToWorld = piece.Definition.Bodies[root].Bind.Inverse().Then(initial[root]);
            for (int h = 0; h < rig.BoneCount; h++)
                world[h] = new RigidTransform(rig.BindPosition[h], Quaternion.Inverse(rig.BindRotationInverse[h])).Then(bindToWorld);
            for (int b = 0; b < piece.Definition.Bodies.Length; b++)
                world[piece.Definition.Bodies[b].Bone] = piece.Definition.Bodies[b].BoneInBody.Then(initial[b]);
        }
        (piece.Anchors, piece.Offsets) = FragmentAnchors(piece, rig, world, initial);
        piece.Components = null;
    }

    static (int[] Anchors, RigidTransform[] Offsets) FragmentAnchors(Piece piece, SkeletonRig rig, RigidTransform[] world, RigidTransform[]? initial = null)
    {
        var anchors = new int[rig.BoneCount];
        var offsets = new RigidTransform[rig.BoneCount];
        for (int h = 0; h < rig.BoneCount; h++)
        {
            int body = FragmentAnchor(piece.Definition, rig, h);
            anchors[h] = body;
            offsets[h] = world[h].Then((initial ?? piece.Pose)[body].Inverse());
            if (piece.Definition.BodyOfBone[h] is int own and >= 0) offsets[h] = piece.Definition.Bodies[own].BoneInBody;
        }
        return (anchors, offsets);
    }

    static int FragmentAnchor(RagdollDefinition definition, SkeletonRig rig, int bone)
    {
        int at = bone;
        while (at >= 0 && definition.BodyOfBone[at] < 0) at = rig.Parent[at];
        int body = at >= 0 ? definition.BodyOfBone[at] : -1;
        int family = LimbFamily(rig.ByHandle[bone]?.Name ?? "");

        // A retained forearm/calf owns the missing upper limb's cut-edge influences, not the torso across its severed joint.
        if (definition.BodyOfBone[bone] < 0 && family != 0 && (body < 0 || LimbFamily(Name(body)) != family))
        {
            int limb = Nearest(b => LimbFamily(Name(b)) == family);
            if (limb >= 0) body = limb;
        }
        if (body >= 0) return body;

        // A missing pelvis/spine/root must follow the retained axial body even when a loose arm is geometrically nearer.
        int axial = Nearest(b => Axial(Name(b)));
        return axial >= 0 ? axial : Nearest(_ => true);

        string Name(int b) => rig.ByHandle[definition.Bodies[b].Bone]?.Name ?? definition.Bodies[b].Name;

        int Nearest(Func<int, bool> allowed)
        {
            int best = -1;
            float nearest = float.PositiveInfinity;
            for (int b = 0; b < definition.Bodies.Length; b++)
            {
                if (!allowed(b)) continue;
                float distance = Vector3.DistanceSquared(rig.BindPosition[bone], definition.Bodies[b].Bind.Position);
                if (distance < nearest) (best, nearest) = (b, distance);
            }
            return best;
        }
    }

#if BRUTALITY_STOCK_MASTER
    /// <summary>
    /// The joint component each mesh bone will follow when the stock fragment animates.
    /// </summary>
    internal static int[] StockBoneComponents(RagdollDefinition definition, SkeletonRig rig)
    {
        var anchors = new int[rig.BoneCount];
        for (int bone = 0; bone < anchors.Length; bone++)
            anchors[bone] = FragmentAnchor(definition, rig, bone);

        return BoneComponents(definition, anchors);
    }
#endif

    static int[] BoneComponents(RagdollDefinition definition, int[] anchors)
    {
        var bodyComponents = new int[definition.Bodies.Length];
        Array.Fill(bodyComponents, -1);
        var pending = new Stack<int>();
        int component = 0;
        for (int b = 0; b < bodyComponents.Length; b++)
        {
            if (bodyComponents[b] >= 0) continue;
            bodyComponents[b] = component;
            pending.Push(b);
            while (pending.TryPop(out int body))
                foreach (var joint in definition.Joints)
                {
                    int other = joint.A == body ? joint.B : joint.B == body ? joint.A : -1;
                    if (other < 0 || bodyComponents[other] >= 0) continue;
                    bodyComponents[other] = component;
                    pending.Push(other);
                }
            component++;
        }
        return [.. anchors.Select(body => bodyComponents[body])];
    }

    static int LimbFamily(string name)
    {
        int side = name.Contains(" L ", StringComparison.Ordinal) ? 1 : name.Contains(" R ", StringComparison.Ordinal) ? 2 : 0;
        if (side == 0 || name.Contains("Clavicle", StringComparison.Ordinal)) return 0;
        if (name.Contains("Thigh", StringComparison.Ordinal) || name.Contains("Calf", StringComparison.Ordinal)
            || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal)) return side + 2;
        return name.Contains("Arm", StringComparison.OrdinalIgnoreCase) || name.Contains("Hand", StringComparison.Ordinal) || name.Contains("Finger", StringComparison.Ordinal) ? side : 0;
    }

    static bool Axial(string name) => name == "Bip01" || name.Contains("Pelvis", StringComparison.Ordinal) || name.Contains("Spine", StringComparison.Ordinal)
        || name.Contains("Neck", StringComparison.Ordinal) || name.Contains("Head", StringComparison.Ordinal) || name.Contains("Jaw", StringComparison.Ordinal);

    // Carrying owns each cut side's root component independently; disconnected shoulder chains remain free.
}
