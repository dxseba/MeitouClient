using System.Collections.Immutable;
using System.Numerics;

namespace Meitou.Brutality.Physics;

/// <summary>The loose bowel mesh's physical rest lattice, in its bounds-centred local space.</summary>
public sealed record BowelRigGeometry(Vector3[] RestNodes, float Radius);

/// <summary>Maps rest-local mesh points through each tissue node into coordinates relative to <see cref="Centre"/>.</summary>
public sealed record BowelPose(long Key, Vector3 Centre, Matrix4x4[] Transforms, Vector3[] RestNodes);

internal readonly record struct SavedBowelNode(Vector3 Position, Quaternion Rotation, Vector3 Linear, Vector3 Angular);

/// <summary>A copy of the nine bodies and attachment state, independent of the simulation handles and render pose.</summary>
internal sealed record SavedBowelRig(ImmutableArray<SavedBowelNode> Nodes, float Age, bool Frozen, int PinNode, Vector3 AttachmentVelocity)
{
    internal bool IsValid => !Nodes.IsDefault && Nodes.Length == BowelRagdollScene.NodeCount
        && float.IsFinite(Age) && Age is >= 0 and <= 600
        && PinNode is >= -1 and < BowelRagdollScene.NodeCount && Bounded(AttachmentVelocity, 1_000)
        && Nodes.All(node => Bounded(node.Position, 10_000_000) && Bounded(node.Linear, 1_000)
            && Bounded(node.Angular, 1_000) && MathF.Abs(node.Rotation.LengthSquared() - 1) <= .01f);

    static bool Bounded(Vector3 value, float limit) => value.LengthSquared() <= limit * limit;
}
