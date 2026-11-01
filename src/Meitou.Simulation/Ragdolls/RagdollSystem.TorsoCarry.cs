using System.Numerics;
using BepuPhysics;
using Meitou.Data.Characters;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Saving;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    void UpdateTorsoCarry(World world, TorsoFragments fragments, CharacterCold cold, in CharacterHot hot)
    {
        bool negativeCarried = !cold.CarriedBy.IsNone;
        UpdateTorsoPieceCarry(fragments.Negative, cold, negativeCarried, Placement(fragments.Negative, cold, hot),
            ref fragments.NegativeCarried, ref fragments.NegativeCarriedBodies);

        var table = world.Characters;
        bool positiveCarried = table.TryResolveNext(cold.TorsoPositiveCarriedBy, out int holder);
        var placement = Matrix4x4.Identity;
        if (positiveCarried)
        {
            ref readonly var carrier = ref table.Next[holder];
            var (at, rotation) = (cold.TorsoPositiveCarryAttach ?? CarryPose.Fallback).World(carrier.Position, carrier.Yaw);
            placement = CharacterRigs.Placement(fragments.Positive.Setup, at, rotation, fragments.Positive.Scale);
            cold.TorsoPositiveYaw = carrier.Yaw;
        }
        UpdateTorsoPieceCarry(fragments.Positive, cold, positiveCarried, placement,
            ref fragments.PositiveCarried, ref fragments.PositiveCarriedBodies,
            release: cold.TorsoPositiveReleaseAt is not null, forceCarriedPose: true);
        if (!positiveCarried && cold.TorsoPositiveReleaseAt is { } released)
        {
            var piece = fragments.Positive;
            var by = released - piece.Pose[piece.Definition.Root].Position;
            for (int b = 0; b < piece.Bodies.Length; b++)
            {
                if (!fragments.PositiveCarriedBodies![b] || piece.Bodies[b] is not { } handle) continue;
                var body = physics.Simulation.Bodies[handle];
                body.Pose.Position += by;
                body.Velocity = default;
                body.Awake = true;
                piece.Pose[b] = new(body.Pose.Position, body.Pose.Orientation);
            }
        }
        cold.TorsoPositiveReleaseAt = null;
    }

    void UpdateTorsoPieceCarry(Ragdoll piece, CharacterCold cold, bool carried, Matrix4x4 placement,
        ref bool wasCarried, ref bool[]? carriedBodies, bool release = false, bool forceCarriedPose = false)
    {
        if (!carried && !wasCarried && !release) return;
        if (carried != wasCarried || release)
        {
            carriedBodies ??= RootComponent(piece.Definition);
            foreach (var joint in piece.Joints) physics.Simulation.Solver.Remove(joint);
            piece.Joints.Clear();
            SetTone(piece, Tone.None);
            var kinds = Shapes(piece.Definition);
            for (int b = 0; b < piece.Bodies.Length; b++)
            {
                if (piece.Bodies[b] is not { } handle) continue;
                var body = physics.Simulation.Bodies[handle];
                if (carriedBodies[b])
                {
                    if (carried) body.BecomeKinematic();
                    else body.SetLocalInertia(kinds[b].Inertia);
                    body.Velocity = default;
                    body.Awake = true;
                }
                piece.Dynamic[b] = !body.Kinematic;
            }
            piece.Frozen = false;
            piece.Age = 0;
            foreach (var joint in piece.Definition.Joints)
                if (piece.Dynamic[joint.A] || piece.Dynamic[joint.B]) AddJoint(piece, joint);
            SetTone(piece, Tone.Dead);
            wasCarried = carried;
        }
        if (!carried) return;
        var target = forceCarriedPose ? AnimatedTorsoCarry(piece, cold, placement) : Animated(piece, cold, placement);
        for (int b = 0; b < piece.Bodies.Length; b++)
        {
            if (!carriedBodies![b] || piece.Bodies[b] is not { } handle) continue;
            var body = physics.Simulation.Bodies[handle];
            body.Pose = new RigidPose(target[b].Position, target[b].Rotation);
            body.Velocity = default;
            piece.Pose[b] = target[b];
        }
    }

    // The logical negative half may lie on the ground while the positive half is on another person's shoulder.
    // Pose that half as carried without changing the corpse's own animation or the other half's physical bones.
    RigidTransform[] AnimatedTorsoCarry(Ragdoll piece, CharacterCold cold, Matrix4x4 placement)
    {
        var rig = piece.Setup.Rig;
        if (rig.Layer(rigs.Database, CarrySystem.CarriedClip, piece.Setup.Animal) is not { } carried)
            return Animated(piece, cold, placement);
        var scratch = rig.Rent();
        try
        {
            var layers = new List<ActiveLayer> { new(carried, 0, 1) };
            foreach (var (def, time) in piece.Setup.Fixed) layers.Add(new ActiveLayer(def, time, 1));
            scratch.PoseDerived(piece.Setup.Shape, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(layers));
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            return [.. piece.Definition.Bodies.Select(body => body.BoneInBody.Inverse().Then(new RigidTransform(
                Vector3.Transform(scratch.DerivedPosition[body.Bone], placement), Quaternion.Normalize(turn * scratch.DerivedRotation[body.Bone]))))];
        }
        finally { rig.Return(scratch); }
    }

    static bool FiniteFragment(SavedRagdoll saved) => saved.Pose.Length > 0 && saved.Pose.All(p =>
        float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z)
        && float.IsFinite(p.Rotation.X) && float.IsFinite(p.Rotation.Y) && float.IsFinite(p.Rotation.Z) && float.IsFinite(p.Rotation.W)
        && p.Rotation.LengthSquared() > 1e-8f);

    static bool SavedFragmentRootNear(SavedRagdoll saved, int root, Vector3 owner) =>
        FiniteFragment(saved) && (uint)root < (uint)saved.Pose.Length && Vector3.DistanceSquared(saved.Pose[root].Position, owner) < 500 * 500;
}
