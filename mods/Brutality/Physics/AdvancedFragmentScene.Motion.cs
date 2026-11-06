using System.Numerics;
using BepuPhysics;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay.Bodies;

namespace Meitou.Brutality.Physics;

internal readonly record struct FragmentBodyMotion(int Bone, RigidTransform Body, Vector3 Linear, Vector3 Angular);
internal sealed partial class AdvancedFragmentScene
{
    internal FragmentBodyMotion[] CaptureMotion(Piece piece)
    {
        Read(piece);
        var result = new FragmentBodyMotion[piece.Pose.Length];
        for (int b = 0; b < result.Length; b++)
        {
            Vector3 linear = default, angular = default;
            if (piece.Bodies[b] is { } handle)
            { var velocity = physics.Simulation.Bodies[handle].Velocity; linear = velocity.Linear; angular = velocity.Angular; }
            result[b] = new(piece.Definition.Bodies[b].Bone, piece.Pose[b], linear, angular);
        }
        return result;
    }
    internal void InheritMotion(Piece piece, IReadOnlyList<FragmentBodyMotion> previous, Vector3 separation = default)
    {
        for (int b = 0; b < piece.Pose.Length; b++)
        {
            if (piece.Bodies[b] is not { } handle) continue;
            var actor = physics.Simulation.Bodies[handle];
            var prior = previous.FirstOrDefault(motion => motion.Bone == piece.Definition.Bodies[b].Bone);
            if (!previous.Any(motion => motion.Bone == piece.Definition.Bodies[b].Bone))
            { actor.Velocity.Linear += separation; continue; }
            actor.Velocity = new BodyVelocity(prior.Linear + Vector3.Cross(prior.Angular, piece.Pose[b].Position - prior.Body.Position) + separation, prior.Angular);
        }
    }
    internal bool TryImpactSite(Piece piece, SkeletonRig rig, BodyPartTemplate part, out int actor, out Vector3 local)
    {
        local = default;
        int? bone = rig.Handle(part.BoneName) ?? rig.Handle(part.BoneName2);
        if (bone is null)
        {
            string side = part.Side == BodySide.Left ? "L" : "R";
            bone = rig.Handle(part.Type switch { BodyPartType.Head => "Bip01 Head", BodyPartType.Arm => $"Bip01 {side} Forearm",
                BodyPartType.Leg => $"Bip01 {side} Calf", _ => "Bip01 Spine1" });
        }
        actor = bone is { } h && h < piece.Definition.BodyOfBone.Length ? piece.Definition.BodyOfBone[h] : -1;
        var bind = bone is { } bindBone ? rig.BindPosition[bindBone] : piece.Definition.Bodies[piece.Definition.Root].Bind.Position;
        if (actor < 0)
        {
            float nearest = float.PositiveInfinity;
            for (int b = 0; b < piece.Pose.Length; b++)
                if (piece.Dynamic[b] && (piece.Frozen || piece.Bodies[b] is not null) && Vector3.DistanceSquared(bind, piece.Definition.Bodies[b].Bind.Position) is var distance && distance < nearest)
                    (actor, nearest) = (b, distance);
        }
        if (actor < 0 || actor >= piece.Pose.Length || !piece.Dynamic[actor] || !piece.Frozen && piece.Bodies[actor] is null) return false;
        local = piece.Definition.Bodies[actor].Bind.Inverse().Apply(bind);
        return true;
    }
    internal void Impact(Piece piece, int actor, Vector3 local, Vector3 velocity)
    {
        if (piece.Frozen) Impulse(piece, -1, Vector3.Zero);
        if ((uint)actor >= (uint)piece.Bodies.Length || piece.Bodies[actor] is not { } handle || !piece.Dynamic[actor]) return;
        piece.Age = 0;
        var body = physics.Simulation.Bodies[handle]; body.Awake = true;
        body = physics.Simulation.Bodies[handle];
        if (body.LocalInertia.InverseMass <= 0) return;
        body.ApplyImpulse(velocity / body.LocalInertia.InverseMass, Vector3.Transform(local, body.Pose.Orientation));
    }
}
