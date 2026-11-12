using System.Numerics;
using BepuPhysics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Physics;

internal sealed partial class AdvancedFragmentScene
{
    public void SetCarried(Piece piece, RigidTransform[] target, bool[] connected, bool[] pinned, bool entering)
    {
#if BRUTALITY_STOCK_MASTER
        bool wasFrozen = piece.Frozen;
        if (wasFrozen)
        {
            MakeRoom(piece.Bodies.Where((handle, b) => handle is not null && connected[b] && !pinned[b]).Count());
            piece.Frozen = false;
        }
        bool rebuild = wasFrozen || !piece.Carried || entering || Enumerable.Range(0, pinned.Length)
            .Any(b => connected[b] && piece.Bodies[b] is not null && piece.Dynamic[b] == pinned[b]);
#else
        if (piece.Frozen) Impulse(piece, -1, Vector3.Zero);
        bool rebuild = !piece.Carried || entering || Enumerable.Range(0, pinned.Length).Any(b => piece.Bodies[b] is not null && piece.Dynamic[b] == pinned[b]);
#endif
        piece.Carried = true;
        if (rebuild)
        {
            foreach (var joint in piece.Joints) physics.Simulation.Solver.Remove(joint);
            piece.Joints.Clear();
            var shapes = Shapes(piece.Definition);
            for (int b = 0; b < piece.Bodies.Length; b++)
            {
                if (piece.Bodies[b] is not { } handle) continue;
#if BRUTALITY_STOCK_MASTER
                ref var tag = ref physics.Tag(handle);
                tag.CarryPin = pinned[b];
                tag.CarryOrphan = !connected[b];
                if (!connected[b])
                {
                    // Lifting the torso leaves a settled loose fragment at rest. A falling one keeps its momentum.
                    if (wasFrozen) piece.Dynamic[b] = false;
                    continue;
                }
#endif
                var body = physics.Simulation.Bodies.GetBodyReference(handle);
                if (pinned[b]) body.BecomeKinematic();
                else body.SetLocalInertia(shapes[b].Inertia);
                if (entering && connected[b])
                {
                    body.Pose = new RigidPose(target[b].Position, target[b].Rotation);
                    body.Velocity = default; piece.Pose[b] = target[b];
                }
                piece.Dynamic[b] = !pinned[b];
            }
            RebuildCarryJoints(piece);
        }
        for (int b = 0; b < piece.Bodies.Length; b++)
        {
            if (!pinned[b]) continue;
            piece.Pose[b] = target[b];
            if (piece.Bodies[b] is not { } handle) continue;
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.Pose = new RigidPose(target[b].Position, target[b].Rotation);
            body.Velocity = default; body.Awake = true;
        }
        piece.Age = 0;
    }

    public void ReleaseCarried(Piece piece, bool preserveDynamic = false)
    {
        if (!piece.Carried) return;
#if BRUTALITY_STOCK_MASTER
        var connected = piece.StockGripBodies ??= ConnectedRoot(piece.Definition);
        if (preserveDynamic) MakeRoom(piece.Bodies.Where((handle, b) => handle is not null && connected[b] && !piece.Dynamic[b]).Count());
#else
        if (preserveDynamic) MakeRoom(piece.Bodies.Where((handle, b) => handle is not null && !piece.Dynamic[b]).Count());
#endif
        piece.Carried = false;
        foreach (var joint in piece.Joints) physics.Simulation.Solver.Remove(joint);
        piece.Joints.Clear();
        var shapes = Shapes(piece.Definition);
        for (int b = 0; b < piece.Bodies.Length; b++)
        {
            if (piece.Bodies[b] is not { } handle) continue;
#if BRUTALITY_STOCK_MASTER
            ref var tag = ref physics.Tag(handle);
            tag.CarryPin = tag.CarryOrphan = false;
            if (preserveDynamic && !connected[b]) continue;
#endif
            if (preserveDynamic && piece.Dynamic[b]) continue;
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.SetLocalInertia(shapes[b].Inertia); body.Velocity = default; body.Awake = true;
            piece.Dynamic[b] = true;
        }
        RebuildCarryJoints(piece);
        piece.Age = 0;
    }
    void RebuildCarryJoints(Piece piece)
    {
        foreach (var joint in piece.Definition.Joints)
            if (piece.Bodies[joint.A] is not null && piece.Bodies[joint.B] is not null && (piece.Dynamic[joint.A] || piece.Dynamic[joint.B]))
                AddJoint(piece, joint);
    }

    internal static bool[] ConnectedRoot(RagdollDefinition definition)
    {
        var connected = new bool[definition.Bodies.Length];
        var pending = new Queue<int>(); pending.Enqueue(definition.Root); connected[definition.Root] = true;
        while (pending.TryDequeue(out int body))
            foreach (var joint in definition.Joints)
            {
                int other = joint.A == body ? joint.B : joint.B == body ? joint.A : -1;
                if (other < 0 || connected[other]) continue;
                connected[other] = true; pending.Enqueue(other);
            }
        return connected;
    }

    internal static void RefreshCarryOffsets(Piece piece, SkeletonRig rig, RigidTransform[] bones, RigidTransform[] targets, bool[] connected)
    {
        EnsureAnchors(piece, rig);
        for (int h = 0; h < rig.BoneCount; h++)
        {
            int body = piece.Anchors![h];
            if (connected[body] && piece.Definition.BodyOfBone[h] < 0) piece.Offsets![h] = bones[h].Then(targets[body].Inverse());
        }
    }
#if !BRUTALITY_STOCK_MASTER
    internal static BonePose CarryPose(Piece piece, SkeletonRig rig, RigidTransform[] poses)
    {
        EnsureAnchors(piece, rig);
        var result = new BonePose(rig.BoneCount)
        { AbsolutePositions = true, Components = piece.Components ??= BoneComponents(piece.Definition, piece.Anchors!) };
        for (int h = 0; h < rig.BoneCount; h++)
        {
            var at = piece.Offsets![h].Then(poses[piece.Anchors![h]]);
            result.Set[h] = true; result.Position[h] = at.Position; result.Rotation[h] = at.Rotation;
        }
        return result;
    }
#endif
}
