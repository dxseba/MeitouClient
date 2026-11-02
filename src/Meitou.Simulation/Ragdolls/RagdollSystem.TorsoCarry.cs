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
        bool negativeHolderChanged = fragments.NegativeGripHolder != cold.CarriedBy;
        if (negativeHolderChanged)
        {
            fragments.NegativeGrip = null;
            fragments.NegativeSupport = null;
            fragments.NegativeGripHolder = cold.CarriedBy;
        }
        bool negativeCarried = !cold.CarriedBy.IsNone;
        var publicationFrame = Placement(fragments.Negative, cold, hot);
        UpdateTorsoPieceCarry(fragments.Negative, cold, negativeCarried, publicationFrame, publicationFrame,
            ref fragments.NegativeCarried, ref fragments.NegativeCarriedBodies, fragments, -1, UpperArmOf(world.Characters, cold.CarriedBy, ref fragments.NegativeSupport), newHolder: negativeHolderChanged);

        var table = world.Characters;
        bool positiveCarried = table.TryResolveNext(cold.TorsoPositiveCarriedBy, out int holder);
        var positiveGripHolder = positiveCarried ? cold.TorsoPositiveCarriedBy : CharacterId.None;
        bool positiveHolderChanged = fragments.PositiveGripHolder != positiveGripHolder;
        if (positiveHolderChanged)
        {
            fragments.PositiveGrip = null;
            fragments.PositiveSupport = null;
            fragments.PositiveGripHolder = positiveGripHolder;
        }
        var placement = Matrix4x4.Identity;
        if (positiveCarried)
        {
            ref readonly var carrier = ref table.Next[holder];
            var (at, rotation) = (cold.TorsoPositiveCarryAttach ?? CarryPose.Fallback).World(carrier.Position, carrier.Yaw);
            placement = CharacterRigs.Placement(fragments.Positive.Setup, at, rotation, fragments.Positive.Scale);
            cold.TorsoPositiveYaw = carrier.Yaw;
        }
        UpdateTorsoPieceCarry(fragments.Positive, cold, positiveCarried, placement, publicationFrame,
            ref fragments.PositiveCarried, ref fragments.PositiveCarriedBodies, fragments, 1, UpperArmOf(table, cold.TorsoPositiveCarriedBy, ref fragments.PositiveSupport),
            release: cold.TorsoPositiveReleaseAt is not null, forceCarriedPose: true, newHolder: positiveHolderChanged);
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

    void UpdateTorsoPieceCarry(Ragdoll piece, CharacterCold cold, bool carried, Matrix4x4 placement, Matrix4x4 publicationFrame,
        ref bool wasCarried, ref bool[]? carriedBodies, TorsoFragments fragments, int side, Vector3? palm, bool release = false, bool forceCarriedPose = false, bool newHolder = false)
    {
        if (!carried && !wasCarried && !release) return;
        bool entering = carried && (!wasCarried || newHolder);
        carriedBodies ??= RootComponent(piece.Definition);
        var layers = carried ? TorsoCarryLayers(piece, cold, forceCarriedPose) : [];
        var target = carried ? AnimatedTorsoCarry(piece, placement, layers) : [];
        if (carried)
        {
            RefreshCarriedOffsets(fragments, piece, side, placement, layers, target, carriedBodies);
            if (palm is { } hand) AlignTorsoGrip(fragments, piece, cold, side, target, carriedBodies, hand, publicationFrame, layers, entering);
        }
        if (carried != wasCarried || release || entering)
        {
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
                    // Match ordinary carry: the head and forearms hang under their original joints.
                    if (carried && !Simulates(piece, RagdollPart.CarryMode, piece.Definition.Bodies[b])) body.BecomeKinematic();
                    else body.SetLocalInertia(kinds[b].Inertia);
                    if (entering)
                    {
                        body.Pose = new RigidPose(target[b].Position, target[b].Rotation);
                        piece.Pose[b] = target[b];
                    }
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
        for (int b = 0; b < piece.Bodies.Length; b++)
        {
            if (!carriedBodies[b] || piece.Bodies[b] is not { } handle) continue;
            var body = physics.Simulation.Bodies[handle];
            if (!piece.Dynamic[b])
            {
                body.Pose = new RigidPose(target[b].Position, target[b].Rotation);
                body.Velocity = default;
                piece.Pose[b] = target[b];
            }
            body.Awake = true;
        }
    }

    // Use the real carried animation when it exists. A positive-only holder cannot change the logical negative owner's animation.
    ActiveLayer[] TorsoCarryLayers(Ragdoll piece, CharacterCold cold, bool forceCarriedPose)
    {
        var layers = new List<ActiveLayer>();
        bool hasCarry = false;
        if (cold.Animation is { } animation && animations is not null)
            foreach (var layer in animations.Publish(animation, []))
                if (piece.Setup.Rig.Layer(rigs.Database, layer.Name, piece.Setup.Animal) is { } def)
                {
                    layers.Add(new(def, MathF.Max(layer.Time, 0), layer.Weight));
                    hasCarry |= layer.Name == CarrySystem.CarriedClip;
                }
        if (forceCarriedPose && !hasCarry && piece.Setup.Rig.Layer(rigs.Database, CarrySystem.CarriedClip, piece.Setup.Animal) is { } carry)
        {
            layers.Clear();
            layers.Add(new(carry, 0, 1));
        }
        foreach (var (def, time) in piece.Setup.Fixed) layers.Add(new(def, time, 1));
        return [.. layers];
    }

    static RigidTransform[] AnimatedTorsoCarry(Ragdoll piece, Matrix4x4 placement, ActiveLayer[] layers)
    {
        var rig = piece.Setup.Rig;
        var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(piece.Setup.Shape, layers);
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            return [.. piece.Definition.Bodies.Select(body => body.BoneInBody.Inverse().Then(new RigidTransform(
                Vector3.Transform(scratch.DerivedPosition[body.Bone], placement), Quaternion.Normalize(turn * scratch.DerivedRotation[body.Bone]))))];
        }
        finally { rig.Return(scratch); }
    }

    static void RefreshCarriedOffsets(TorsoFragments fragments, Ragdoll piece, int side, Matrix4x4 placement,
        ActiveLayer[] layers, RigidTransform[] target, bool[] root)
    {
        var anchors = side > 0 ? fragments.PositiveAnchors : fragments.NegativeAnchors;
        var offsets = side > 0 ? fragments.PositiveOffsets : fragments.NegativeOffsets;
        var rig = piece.Setup.Rig;
        var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(piece.Setup.Shape, layers);
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            for (int h = 0; h < anchors.Length; h++)
            {
                int b = anchors[h];
                if (!root[b] || piece.Definition.BodyOfBone[h] >= 0) continue;
                var animated = new RigidTransform(Vector3.Transform(scratch.DerivedPosition[h], placement), Quaternion.Normalize(turn * scratch.DerivedRotation[h]));
                offsets[h] = animated.Then(target[b].Inverse());
            }
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
