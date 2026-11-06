using System.Numerics;
using BepuPhysics;

namespace Meitou.Brutality.Physics;

internal sealed partial class AdvancedFragmentScene
{
    internal void MoveStockGrip(Piece piece, Vector3 from, Vector3 to, Quaternion turn)
    {
        for (int i = 0; i < piece.Pose.Length; i++)
        {
            var prior = piece.Pose[i];
            var next = new RigidTransform(to + Vector3.Transform(prior.Position - from, turn), Quaternion.Normalize(turn * prior.Rotation));
            piece.Pose[i] = next;
            if (piece.Bodies[i] is not { } handle) continue;
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.Pose = new RigidPose(next.Position, next.Rotation);
            body.UpdateBounds();
        }
    }
}
