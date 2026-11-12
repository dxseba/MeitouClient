using System.Collections.Immutable;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality.Physics;

/// <summary>Transient jointed bowel props, owned and stepped by one display thread, independently of simulation ragdolls.</summary>
public sealed class BowelRagdollScene : IDisposable
{
    public const int Capacity = 32, NodeCount = 9;
    public const float FixedStep = 1f / 120, FreezeEarliest = 2, FreezeLatest = 12;
    const float NodeMass = 0.03f;
    static readonly SpringSettings TissueSpring = new(7, 1);

    sealed class Rig
    {
        public required long Key;
        public required Vector3[] Rest;
        public required BodyHandle[] Bodies;
        public required TypedIndex Shape;
        public required float Radius;
        public required Matrix4x4[] Transforms;
        public readonly List<ConstraintHandle> Joints = [];
        public BowelPose? Pose;
        public float Age;
        public bool Frozen, Placed, PreserveAttachment;
        public int PinNode = -1;
        public Vector3 Anchor, AttachmentVelocity;
        public float AttachmentReach;
        public SavedBowelRig? FrozenSave;
    }

    readonly RagdollPhysics physics = new();
    readonly Dictionary<long, Rig> rigs = [];
    int nextTag = 1;
    float due, lastFrameSeconds = FixedStep;
    bool disposed;

    public int Count => rigs.Count;
    public int Moving => rigs.Values.Count(r => !r.Frozen);

    public bool Add(long key, BowelRigGeometry geometry, Vector3 position, Quaternion rotation, Vector3 velocity,
        bool resting = false, int pinNode = -1, bool preserveAttachment = false, Vector3 launchSpin = default)
    {
        if (pinNode < -1 || pinNode >= NodeCount || disposed || rigs.ContainsKey(key) || rigs.Count >= Capacity || !Valid(geometry)
            || !Finite(position) || !Finite(velocity) || !Finite(launchSpin) || !float.IsFinite(rotation.LengthSquared())
            || rotation.LengthSquared() < 1e-8f) return false;
        rotation = Quaternion.Normalize(rotation);
        if (velocity.LengthSquared() > 10000) velocity = Vector3.Normalize(velocity) * 100;
        float spinSpeed = launchSpin.Length();
        if (resting || !float.IsFinite(spinSpeed)) launchSpin = Vector3.Zero;
        else if (spinSpeed > 3.5f) launchSpin *= 3.5f / spinSpeed;
        var sphere = new Sphere(geometry.Radius);
        var shape = physics.Simulation.Shapes.Add(sphere);
        var rig = new Rig { Key = key, Rest = (Vector3[])geometry.RestNodes.Clone(), Bodies = new BodyHandle[NodeCount],
            Shape = shape, Radius = geometry.Radius, Transforms = new Matrix4x4[NodeCount],
            PinNode = resting ? -1 : pinNode, PreserveAttachment = preserveAttachment,
            AttachmentReach = MathF.Max(preserveAttachment ? 3 : 1, geometry.RestNodes.Max(p => p.Length()) * (preserveAttachment ? 6 : 4)) };
        int created = 0, tag = nextTag++;
        try
        {
            Vector3 pivot = rig.PinNode >= 0 ? rig.Rest[rig.PinNode] : Vector3.Zero;
            for (int i = 0; i < NodeCount; i++)
            {
                var at = position + Vector3.Transform(rig.Rest[i], rotation);
                // Start the fold progressively across the rows. A uniform impulse drops the original shape as one slab.
                float spinWeight = .6f + .4f * (i / 3);
                var spin = launchSpin * spinWeight;
                var offset = Vector3.Transform(rig.Rest[i] - pivot, rotation);
                var motion = new BodyVelocity(velocity + Vector3.Cross(spin, offset), spin);
                var description = BodyDescription.CreateDynamic(new RigidPose(at, rotation), motion,
                    sphere.ComputeInertia(NodeMass), RagdollPhysics.Collidable(shape), new BodyActivityDescription(0.01f));
                rig.Bodies[i] = physics.AddBody(description,
                    new BodyTag { Ragdoll = tag, Index = i, Joined = (1UL << NodeCount) - 1 });
                created++;
            }
            if (rig.PinNode >= 0)
            {
                var pin = physics.Simulation.Bodies.GetBodyReference(rig.Bodies[rig.PinNode]);
                rig.Anchor = pin.Pose.Position;
                rig.AttachmentVelocity = velocity;
                pin.Velocity = default;
                pin.BecomeKinematic();
            }
            JoinLattice(rig);
            rigs.Add(key, rig);
            ReadPose(rig);
            if (resting) Freeze(rig);
            return true;
        }
        catch
        {
            foreach (var joint in rig.Joints) physics.Simulation.Solver.Remove(joint);
            for (int i = 0; i < created; i++) physics.Simulation.Bodies.Remove(rig.Bodies[i]);
            physics.Simulation.Shapes.RemoveAndDispose(shape, physics.Pool);
            throw;
        }
    }

    void JoinLattice(Rig rig)
    {
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                int a = row * 3 + column;
                if (column < 2) Join(rig, a, a + 1);
                // Join rows at their centres so the sheet can fold and twist around the middle.
                if (column == 1 && row < 2) Join(rig, a, a + 3);
            }
    }

    void Join(Rig rig, int a, int b)
    {
        var centre = (rig.Rest[a] + rig.Rest[b]) * 0.5f;
        rig.Joints.Add(physics.Simulation.Solver.Add(rig.Bodies[a], rig.Bodies[b], new BallSocket
        {
            LocalOffsetA = centre - rig.Rest[a], LocalOffsetB = centre - rig.Rest[b], SpringSettings = TissueSpring,
        }));
    }

    public BowelPose? PoseOf(long key) => rigs.TryGetValue(key, out var rig) ? rig.Pose : null;

    public bool IsAttached(long key) => rigs.TryGetValue(key, out var rig) && rig.PinNode >= 0;

    internal SavedBowelRig? Capture(long key)
    {
        if (!rigs.TryGetValue(key, out var rig)) return null;
        if (rig.FrozenSave is { } frozen) return frozen;
        var nodes = ImmutableArray.CreateBuilder<SavedBowelNode>(NodeCount);
        foreach (var handle in rig.Bodies)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            nodes.Add(new(body.Pose.Position, body.Pose.Orientation, body.Velocity.Linear, body.Velocity.Angular));
        }
        var saved = new SavedBowelRig(nodes.MoveToImmutable(), rig.Age, rig.Frozen, rig.PinNode, rig.AttachmentVelocity);
        if (rig.Frozen && rig.Placed) rig.FrozenSave = saved;
        return saved;
    }

    internal bool Restore(long key, BowelRigGeometry geometry, SavedBowelRig saved, bool preserveAttachment)
    {
        if (!saved.IsValid || !Valid(geometry)) return false;
        var first = saved.Nodes[0].Position;
        float reach = MathF.Max(16, geometry.RestNodes.Max(node => node.Length()) * 8 + geometry.Radius);
        if (saved.Nodes.Any(node => Vector3.DistanceSquared(first, node.Position) > reach * reach)
            || !Add(key, geometry, first, Quaternion.Identity, Vector3.Zero, pinNode: saved.PinNode, preserveAttachment: preserveAttachment))
            return false;

        var rig = rigs[key];
        for (int i = 0; i < NodeCount; i++)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(rig.Bodies[i]);
            var node = saved.Nodes[i];
            body.Pose = new RigidPose(node.Position, Quaternion.Normalize(node.Rotation));
            body.Velocity = i == saved.PinNode ? default : new(node.Linear, node.Angular);
            body.UpdateBounds();
        }
        if (saved.PinNode >= 0) rig.Anchor = saved.Nodes[saved.PinNode].Position;
        rig.Age = saved.Age;
        rig.Placed = true;
        if (saved.Frozen) Freeze(rig);
        else if (saved.PinNode >= 0) rig.AttachmentVelocity = saved.AttachmentVelocity;
        ReadPose(rig);
        return true;
    }

    /// <summary>Moves the pinned edge; retained tissue follows bounded pickup jumps as one piece.</summary>
    public bool MoveAttachment(long key, Vector3 target)
    {
        if (!Finite(target) || !rigs.TryGetValue(key, out var rig) || rig.PinNode < 0) return false;
        var delta = target - rig.Anchor;
        float distanceSquared = delta.LengthSquared();
        if (distanceSquared > (rig.PreserveAttachment ? 400 : 64))
        {
            ReleaseAttachment(key);
            return false;
        }
        if (rig.PreserveAttachment && distanceSquared < 1e-8f)
        {
            rig.AttachmentVelocity = default;
            return true;
        }
        if (rig.PreserveAttachment) rig.Age = 0;
        Wake(rig);
        if (rig.PreserveAttachment && distanceSquared > 1) Translate(rig, delta);
        if (Vector3.DistanceSquared(target, rig.Pose!.Centre) > rig.AttachmentReach * rig.AttachmentReach)
        {
            ReleaseAttachment(key);
            return false;
        }
        rig.AttachmentVelocity = delta / MathF.Max(lastFrameSeconds, FixedStep);
        float speed = rig.AttachmentVelocity.Length();
        if (speed > 100) rig.AttachmentVelocity *= 100 / speed;
        rig.Anchor = target;
        HoldAttachment(rig);
        ReadPose(rig);
        return true;
    }

    void Translate(Rig rig, Vector3 delta)
    {
        foreach (var handle in rig.Bodies)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.Pose.Position += delta;
            body.UpdateBounds();
        }
        ReadPose(rig);
    }

    void Wake(Rig rig)
    {
        if (!rig.Frozen) return;
        var inertia = new Sphere(rig.Radius).ComputeInertia(NodeMass);
        for (int i = 0; i < rig.Bodies.Length; i++)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(rig.Bodies[i]);
            if (i != rig.PinNode) body.SetLocalInertia(inertia);
            body.Velocity = default;
            body.Awake = true;
        }
        JoinLattice(rig);
        rig.Frozen = false;
        rig.FrozenSave = null;
        rig.Age = 0;
    }

    /// <summary>Lets the pinned edge fall without changing its current pose.</summary>
    public bool ReleaseAttachment(long key)
    {
        if (!rigs.TryGetValue(key, out var rig) || rig.PinNode < 0) return false;
        Wake(rig);
        var pin = physics.Simulation.Bodies.GetBodyReference(rig.Bodies[rig.PinNode]);
        pin.SetLocalInertia(new Sphere(rig.Radius).ComputeInertia(NodeMass));
        pin.Velocity.Linear = rig.AttachmentVelocity;
        pin.Awake = true;
        rig.PinNode = -1;
        rig.Age = 0;
        return true;
    }

    void ReleaseIfStretched(Rig rig)
    {
        if (rig.PinNode < 0) return;
        float distanceSquared = Vector3.DistanceSquared(rig.Anchor, rig.Pose!.Centre);
        if (!float.IsFinite(distanceSquared) || distanceSquared > rig.AttachmentReach * rig.AttachmentReach)
            ReleaseAttachment(rig.Key);
    }

    void HoldAttachment(Rig rig)
    {
        if (rig.PinNode < 0) return;
        foreach (var handle in rig.Bodies)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            if (!body.Awake) body.Awake = true;
        }
        var pin = physics.Simulation.Bodies.GetBodyReference(rig.Bodies[rig.PinNode]);
        pin.Pose.Position = rig.Anchor;
        pin.Velocity = default;
        pin.UpdateBounds();
    }

    /// <summary>Seconds are real physics time. The caller may pass zero while paused.</summary>
    public void Step(float seconds, IRagdollEnvironment environment, long tick)
    {
        if (disposed || !float.IsFinite(seconds) || seconds < 0) return;
        foreach (var rig in rigs.Values)
            if (!rig.Placed)
            {
                physics.Cover(rig.Pose!.Centre, MathF.Max(4, rig.Rest.Max(p => p.Length()) + rig.Radius + 1), environment, tick);
                Surface(rig, environment, initial: true);
                ReadPose(rig);
                rig.Placed = true;
            }
        if (seconds > 0) lastFrameSeconds = MathF.Min(seconds, 0.25f);
        due += MathF.Min(seconds, 0.25f);
        while (due >= FixedStep)
        {
            foreach (var rig in rigs.Values)
                if (!rig.Frozen)
                {
                    physics.Cover(rig.Pose!.Centre, MathF.Max(4, rig.Rest.Max(p => p.Length()) + rig.Radius + 1), environment, tick);
                    Surface(rig, environment);
                    ReleaseIfStretched(rig);
                    HoldAttachment(rig);
                }
            if (rigs.Values.Any(r => !r.Frozen)) physics.Step(FixedStep);
            foreach (var rig in rigs.Values)
                if (!rig.Frozen)
                {
                    rig.Age += FixedStep;
                    Surface(rig, environment);
                    ReadPose(rig);
                    ReleaseIfStretched(rig);
                    if ((rig.PinNode < 0 || rig.PreserveAttachment)
                        && (rig.Age >= FreezeLatest || rig.Age >= FreezeEarliest && Still(rig))) Freeze(rig);
                }
            due -= FixedStep;
        }
        physics.Sweep(tick);
    }

    void Surface(Rig rig, IRagdollEnvironment environment, bool initial = false)
    {
        for (int i = 0; i < rig.Bodies.Length; i++)
        {
            if (i == rig.PinNode) continue;
            var handle = rig.Bodies[i];
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            var p = body.Pose.Position;
            float floor = environment.GroundHeight(p.X, p.Z, p.Y);
            if (!float.IsFinite(floor) || p.Y >= floor + rig.Radius * (initial ? 1 : 0.5f)) continue;
            if (!body.Awake) body.Awake = true;
            body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.Pose.Position.Y = floor + rig.Radius;
            body.Velocity.Linear.Y = MathF.Max(0, body.Velocity.Linear.Y);
            body.UpdateBounds();
        }
    }

    bool Still(Rig rig)
    {
        foreach (var handle in rig.Bodies)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            if (body.Velocity.Linear.LengthSquared() > 0.04f || body.Velocity.Angular.LengthSquared() > 0.04f) return false;
        }
        return true;
    }

    void Freeze(Rig rig)
    {
        foreach (var joint in rig.Joints) physics.Simulation.Solver.Remove(joint);
        rig.Joints.Clear();
        foreach (var handle in rig.Bodies)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.Velocity = default;
            body.BecomeKinematic();
        }
        rig.Frozen = true;
        rig.AttachmentVelocity = default;
    }

    void ReadPose(Rig rig)
    {
        var centre = Vector3.Zero;
        foreach (var handle in rig.Bodies) centre += physics.Simulation.Bodies.GetBodyReference(handle).Pose.Position / NodeCount;
        for (int i = 0; i < NodeCount; i++)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(rig.Bodies[i]);
            rig.Transforms[i] = Matrix4x4.CreateTranslation(-rig.Rest[i]) * Matrix4x4.CreateFromQuaternion(body.Pose.Orientation)
                * Matrix4x4.CreateTranslation(body.Pose.Position - centre);
        }
        rig.Pose = new BowelPose(rig.Key, centre, rig.Transforms, rig.Rest);
    }

    public void Remove(long key)
    {
        if (!rigs.Remove(key, out var rig)) return;
        foreach (var joint in rig.Joints) physics.Simulation.Solver.Remove(joint);
        foreach (var body in rig.Bodies) physics.Simulation.Bodies.Remove(body);
        physics.Simulation.Shapes.RemoveAndDispose(rig.Shape, physics.Pool);
    }

    public void Clear()
    {
        foreach (long key in rigs.Keys.ToArray()) Remove(key);
        due = 0;
        lastFrameSeconds = FixedStep;
    }

    public void Dispose()
    {
        if (disposed) return;
        Clear();
        physics.Dispose();
        disposed = true;
    }

    static bool Valid(BowelRigGeometry geometry) => geometry is { RestNodes.Length: NodeCount }
        && float.IsFinite(geometry.Radius) && geometry.Radius > 0 && geometry.Radius <= 10
        && geometry.RestNodes.All(Finite)
        && geometry.RestNodes.Max(p => p.LengthSquared()) < 10000;

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
