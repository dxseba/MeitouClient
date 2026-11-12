using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality.Physics;

/// <summary>One world-local articulated scene for independently cut chains, using the reference cut topology and checked collision hulls.</summary>
internal sealed partial class AdvancedFragmentScene : IDisposable
{
    internal sealed class Piece
    {
        public required int Number;
        public required RagdollDefinition Definition;
        public BodyHandle?[] Bodies = [];
        public bool[] Dynamic = [];
        public RigidTransform[] Pose = [];
        public RigidTransform[] InitialPose = [];
        public RigidTransform[]? InitialBones;
#if !BRUTALITY_STOCK_MASTER
        public BonePose? Published;
        public SkeletonRig? PublishedRig;
        public Matrix4x4 PublishedPlacement;
        public RigidTransform[]? PublishedPose, PublishedOffsets;
#endif
        public int[]? Anchors;
        public int[]? Components;
        public RigidTransform[]? Offsets;
        public List<ConstraintHandle> Joints = [];
        public bool Soft = true, Frozen, Carried;
#if BRUTALITY_STOCK_MASTER
        internal bool[]? StockGripBodies;
        internal RigidTransform[]? StockGripTargets;
#endif
        public float Age;
#if BRUTALITY_STOCK_MASTER
        public float PairedCutTime;
#endif
    }
    const int AwakeBudget = 384;
    const float SoftBand = .15f;
    static readonly SpringSettings SoftLimit = new(5, 3);
    readonly RagdollPhysics physics = new();
    readonly List<Piece> pieces = [];
    readonly Dictionary<RagdollDefinition, (TypedIndex Shape, BodyInertia Inertia)[]> shapes = new(ReferenceEqualityComparer.Instance);
    int serial;
    const int ThreadedBodies = 256;
    float physicsDue;
    BepuUtilities.ThreadDispatcher? threads;
    internal long SolverSteps { get; private set; }
    // Frozen actors remain kinematic collision obstacles. They no longer consume the moving-body budget,
    // even if Bepu has not slept them yet (there need not be another solver step after the last piece freezes).
    public int AwakeBodies
    {
        get
        {
            int count = 0;
            foreach (var piece in pieces)
            {
                if (piece.Frozen) continue;
                for (int b = 0; b < piece.Bodies.Length; b++)
                    if (piece.Dynamic[b] && piece.Bodies[b] is { } body && physics.Simulation.Bodies.GetBodyReference(body).Awake) count++;
            }
            return count;
        }
    }
    public int Count => pieces.Count;
#if BRUTALITY_STOCK_MASTER
    internal void SyncNativeContacts(Meitou.Simulation.World world, long tick, StockNativeRagdollContacts.Scene scene,
        Func<Meitou.Simulation.CharacterId, bool> replaces) => physics.NativeContacts.Sync(physics, world, tick, scene,
            pieces.Where(piece => !piece.Frozen && !piece.Carried).Select(piece => piece.Pose[piece.Definition.Root].Position), replaces);
#endif

    public Piece Create(RagdollDefinition definition, Func<int, RigidTransform> boneWorld, Vector3 velocity, Vector3 push, ulong seed, RigidTransform[]? initialBones = null)
    {
        MakeRoom(definition.Bodies.Length);
        var piece = new Piece { Number = ++serial, Definition = definition, Bodies = new BodyHandle?[definition.Bodies.Length],
            Dynamic = Enumerable.Repeat(true, definition.Bodies.Length).ToArray(), Pose = new RigidTransform[definition.Bodies.Length] };
        var made = Shapes(definition);
        velocity = Clamp(velocity, RagdollPhysics.MaxSpeed / 2);
        for (int b = 0; b < piece.Pose.Length; b++)
        {
            var body = definition.Bodies[b];
            var at = body.BoneInBody.Inverse().Then(boneWorld(body.Bone));
            piece.Pose[b] = at;
            if (!made[b].Shape.Exists) continue;
            var spin = new Vector3(Meitou.Simulation.Rng.Float(Meitou.Simulation.Rng.Mix(seed + (ulong)b)) - .5f,
                Meitou.Simulation.Rng.Float(Meitou.Simulation.Rng.Mix(seed + (ulong)b + 17)) - .5f,
                Meitou.Simulation.Rng.Float(Meitou.Simulation.Rng.Mix(seed + (ulong)b + 31)) - .5f);
            var description = BodyDescription.CreateDynamic(new RigidPose(at.Position, at.Rotation),
                new BodyVelocity(velocity, spin * MathF.Min(push.Length() * .12f, 3)), made[b].Inertia,
                RagdollPhysics.Collidable(made[b].Shape), new BodyActivityDescription(.01f));
            piece.Bodies[b] = physics.AddBody(description, new BodyTag { Ragdoll = piece.Number, Index = b, Joined = definition.Joined[b] });
        }
        piece.InitialPose = piece.Pose.ToArray(); piece.InitialBones = initialBones?.ToArray();
        foreach (var joint in definition.Joints)
            if (piece.Bodies[joint.A] is not null && piece.Bodies[joint.B] is not null) AddJoint(piece, joint);
        pieces.Add(piece);
        return piece;
    }

    public void Step(float dt, IRagdollEnvironment environment, long tick, float? gameSeconds = null, int threadCount = 1)
    {
        float gameDt = gameSeconds ?? dt;
        bool moving = false;
        foreach (var piece in pieces)
        {
            if (piece.Frozen || piece.Carried && !piece.Dynamic.Contains(true)) continue;
            moving = true;
            foreach (var body in piece.Pose) physics.Cover(body.Position, 4, environment, tick);
        }
        // The pin accumulates real time until a normal 1x step is owed. Accelerated game ticks must not
        // repeat narrow-phase/solver setup for every tiny interval, while sleep timers remain in game seconds.
        physicsDue = moving ? physicsDue + Math.Clamp(dt, 0, .1f) : 0;
#if BRUTALITY_STOCK_MASTER
        float stepped = 0;
#endif
        if (moving && physicsDue >= RagdollPhysics.StepOf(gameDt, 1) - 1e-5f)
        {
            if (threads is null && threadCount > 1) threads = new BepuUtilities.ThreadDispatcher(threadCount);
            var workers = threadCount > 1 && AwakeBodies >= ThreadedBodies ? threads : null;
            float left = physicsDue;
            while (left > 1e-6f)
            {
                float step = MathF.Min(left, RagdollPhysics.MaxStep);
                physics.Step(step, workers); SolverSteps++; left -= step;
            }
#if BRUTALITY_STOCK_MASTER
            stepped = physicsDue;
#endif
            physicsDue = 0;
        }
        foreach (var piece in pieces)
        {
#if BRUTALITY_STOCK_MASTER
            AdvanceTorsoSeparation(piece, stepped);
#endif
            if (piece.Frozen) continue;
            Surface(piece, environment);
            Read(piece); piece.Age += gameDt;
            int still = 0, count = 0;
            foreach (var handle in piece.Bodies)
            {
                if (handle is not { } h) continue;
                var body = physics.Simulation.Bodies.GetBodyReference(h); count++;
                if (body.Velocity.Linear.LengthSquared() < 2.25f && body.Velocity.Angular.LengthSquared() < 1.96f) still++;
            }
            // Pinned torso fragments wait for every disconnected component, rather than freezing an airborne limb
            // as soon as most of the torso is resting (RagdollSystem.Fragments.FinishTorsoRagdolls at 3a15eeb9).
            if (!piece.Carried && (piece.Age >= 7 || piece.Age >= 2 && (count == 0 || still >= count * .99f))) Freeze(piece);
        }
        physics.Sweep(tick);
    }

    // Preserve the host's one-sided terrain safeguard: a newly tunnelling body is returned above the same floor.
    void Surface(Piece piece, IRagdollEnvironment environment)
    {
        for (int b = 0; b < piece.Bodies.Length; b++)
        {
            if (piece.Bodies[b] is not { } handle) continue;
            var body = physics.Simulation.Bodies.GetBodyReference(handle); var now = body.Pose.Position;
            float floor = environment.GroundHeight(now.X, now.Z, now.Y);
            if (now.Y >= floor - 1) continue;
            var before = piece.Pose[b].Position;
            if (before.Y < environment.GroundHeight(before.X, before.Z, before.Y) - 1) continue;
            body.Pose.Position = new(now.X, floor + 1, now.Z);
            body.Velocity.Linear = new(body.Velocity.Linear.X, MathF.Max(body.Velocity.Linear.Y, 0), body.Velocity.Linear.Z);
            body.Awake = true;
        }
    }

    public void Freeze(Piece piece)
    {
        if (piece.Frozen) return;
        Read(piece);
        foreach (var joint in piece.Joints) physics.Simulation.Solver.Remove(joint);
        piece.Joints.Clear();
        for (int b = 0; b < piece.Bodies.Length; b++)
            if (piece.Bodies[b] is { } handle)
            {
                var body = physics.Simulation.Bodies.GetBodyReference(handle);
                body.Velocity = default;
                if (!body.Kinematic) body.BecomeKinematic();
            }
        piece.Frozen = true;
    }
    public void Impulse(Piece piece, int bone, Vector3 velocity)
    {
        if (piece.Frozen)
        {
            MakeRoom(piece.Bodies.Count(handle => handle is not null));
            var kinds = Shapes(piece.Definition);
            for (int b = 0; b < piece.Bodies.Length; b++)
                if (piece.Bodies[b] is { } handle)
                {
                    var body = physics.Simulation.Bodies.GetBodyReference(handle);
                    body.SetLocalInertia(kinds[b].Inertia); body.Velocity = default;
                    piece.Dynamic[b] = true;
                }
            piece.Frozen = false;
            RebuildCarryJoints(piece);
        }
        piece.Age = 0;
        int struck = bone >= 0 && bone < piece.Definition.BodyOfBone.Length ? piece.Definition.BodyOfBone[bone] : -1;
        if (struck < 0) struck = piece.Definition.Root;
        for (int b = 0; b < piece.Bodies.Length; b++)
            if (piece.Bodies[b] is { } handle)
            {
                var body = physics.Simulation.Bodies.GetBodyReference(handle);
                body.Velocity.Linear = Clamp(body.Velocity.Linear + velocity * (b == struck ? 1 : .12f), RagdollPhysics.MaxSpeed / 2);
                body.Awake = true;
            }
    }

    void MakeRoom(int count)
    {
        while (AwakeBodies + count > AwakeBudget && pieces.FirstOrDefault(piece => !piece.Frozen && !piece.Carried) is { } oldest) Freeze(oldest);
    }
    public void Remove(Piece piece)
    {
        Freeze(piece);
        for (int b = 0; b < piece.Bodies.Length; b++)
            if (piece.Bodies[b] is { } handle) { physics.Simulation.Bodies.Remove(handle); piece.Bodies[b] = null; }
        pieces.Remove(piece);
#if BRUTALITY_STOCK_MASTER
        // Split definitions are usually unique. Keep a shape only while another piece can still reference it.
        if (!pieces.Any(active => ReferenceEquals(active.Definition, piece.Definition)) && shapes.Remove(piece.Definition, out var released))
            foreach (var (shape, _) in released)
                if (shape.Exists) physics.Simulation.Shapes.RemoveAndDispose(shape, physics.Pool);
#endif
    }
    void Read(Piece piece)
    {
        for (int b = 0; b < piece.Pose.Length; b++)
            if (piece.Bodies[b] is { } handle)
            {
                var at = physics.Simulation.Bodies.GetBodyReference(handle).Pose;
                piece.Pose[b] = new(at.Position, at.Orientation);
            }
    }
#if !BRUTALITY_STOCK_MASTER
    public BonePose Publish(Piece piece, SkeletonRig rig, Matrix4x4 placement)
    {
        if (piece.Frozen && ReferenceEquals(piece.PublishedRig, rig) && piece.PublishedPlacement == placement
            && ReferenceEquals(piece.PublishedOffsets, piece.Offsets) && piece.PublishedPose is { } saved
            && saved.AsSpan().SequenceEqual(piece.Pose) && piece.Published is { } ready) return ready;
        Matrix4x4.Invert(placement, out var inverse);
        var unturn = Quaternion.Inverse(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 })));
        var result = new BonePose(rig.BoneCount) { AbsolutePositions = true };
        EnsureAnchors(piece, rig);
        result.Components = piece.Components ??= BoneComponents(piece.Definition, piece.Anchors!);
        for (int h = 0; h < rig.BoneCount; h++)
        {
            var bone = piece.Offsets![h].Then(piece.Pose[piece.Anchors![h]]);
            result.Set[h] = true; result.Position[h] = Vector3.Transform(bone.Position, inverse);
            result.Rotation[h] = Quaternion.Normalize(unturn * bone.Rotation);
        }
        if (piece.Frozen)
        {
            piece.Published = result; piece.PublishedRig = rig; piece.PublishedPlacement = placement;
            piece.PublishedPose = piece.Pose.ToArray(); piece.PublishedOffsets = piece.Offsets;
        }
        else { piece.Published = null; piece.PublishedPose = null; piece.PublishedOffsets = null; }
        return result;
    }
#endif

    public void Translate(Piece piece, Vector3 by)
    {
        for (int b = 0; b < piece.Pose.Length; b++)
        {
            piece.Pose[b] = piece.Pose[b] with { Position = piece.Pose[b].Position + by };
            if (piece.Bodies[b] is { } handle)
            {
                var body = physics.Simulation.Bodies.GetBodyReference(handle);
                body.Pose.Position += by;
                body.UpdateBounds();
                if (!piece.Frozen) body.Awake = true;
            }
        }
    }
    static Vector3 Clamp(Vector3 v, float max) => float.IsFinite(v.LengthSquared()) ? v.LengthSquared() > max * max ? Vector3.Normalize(v) * max : v : Vector3.Zero;

    (TypedIndex Shape, BodyInertia Inertia)[] Shapes(RagdollDefinition definition)
    {
        if (shapes.TryGetValue(definition, out var cached)) return cached;
        var made = new (TypedIndex Shape, BodyInertia Inertia)[definition.Bodies.Length];
        var all = physics.Simulation.Shapes;
        for (int b = 0; b < made.Length; b++)
        {
            var body = definition.Bodies[b]; float mass = MathF.Max(body.Mass, .01f);
            switch (body.Type)
            {
                case PhysicsShapeType.Sphere:
                    var sphere = new Sphere(MathF.Max(body.Size.X, .005f)); made[b] = (all.Add(sphere), sphere.ComputeInertia(mass)); break;
                case PhysicsShapeType.Capsule:
                    var capsule = new Capsule(MathF.Max(body.Size.X, .005f), MathF.Max(body.Size.Y, 0)); made[b] = (all.Add(capsule), capsule.ComputeInertia(mass)); break;
                case PhysicsShapeType.Mesh:
                    if (RagdollHull.TryCreate(body.Hull, physics.Pool, out var hull, out _)) made[b] = (all.Add(hull), hull.ComputeInertia(mass)); break;
                default:
                    var box = new Box(MathF.Max(body.Size.X, .005f), MathF.Max(body.Size.Y, .005f), MathF.Max(body.Size.Z, .005f));
                    made[b] = (all.Add(box), box.ComputeInertia(mass)); break;
            }
        }
        return shapes[definition] = made;
    }
    TwistServo HingeSpring(Piece r, RagdollJoint j)
    {
        var shapes = Shapes(r.Definition);
        float Inertia(int body, Vector3 axis)
        {
            var inverse = shapes[body].Inertia.InverseInertiaTensor;
            float along = inverse.XX * axis.X * axis.X + inverse.YY * axis.Y * axis.Y + inverse.ZZ * axis.Z * axis.Z + 2 * (inverse.YX * axis.X * axis.Y + inverse.ZX * axis.X * axis.Z + inverse.ZY * axis.Y * axis.Z);
            return along > 1e-9f ? 1 / along : 1;
        }
        float inertia = r.Dynamic[j.A] && r.Dynamic[j.B] ? 1 / (1 / Inertia(j.A, j.AxisA) + 1 / Inertia(j.B, j.AxisB)) : r.Dynamic[j.A] ? Inertia(j.A, j.AxisA) : Inertia(j.B, j.AxisB);
        float omega = MathF.Sqrt(j.SpringStiffness / inertia);
        float ratio = j.SpringDamper / (2 * MathF.Sqrt(j.SpringStiffness * inertia));
        return new TwistServo
        {
            LocalBasisA = j.BasisA, LocalBasisB = j.BasisB, TargetAngle = j.SpringTarget,
            SpringSettings = new SpringSettings(omega / (2 * MathF.PI), ratio),
            ServoSettings = new ServoSettings(float.MaxValue, 0, float.MaxValue),
        };
    }

    void AddJoint(Piece r, RagdollJoint j)
    {
        var a = r.Bodies[j.A]!.Value;
        var b = r.Bodies[j.B]!.Value;
        var solver = physics.Simulation.Solver;
        var spring = new SpringSettings(30, 1);
        switch (j.Type)
        {
            case PhysicsJointType.Revolute:
                r.Joints.Add(solver.Add(a, b, new Hinge { LocalOffsetA = j.OffsetA, LocalHingeAxisA = j.AxisA, LocalOffsetB = j.OffsetB, LocalHingeAxisB = j.AxisB, SpringSettings = spring }));
                if (j.Limits)
                    r.Joints.Add(solver.Add(a, b, new TwistLimit { LocalBasisA = j.BasisA, LocalBasisB = j.BasisB, MinimumAngle = j.Low, MaximumAngle = j.High, SpringSettings = spring }));
                if (j.SpringStiffness > 0) r.Joints.Add(solver.Add(a, b, HingeSpring(r, j)));
                if (j.Limits && r.Soft)
                {
                    float band = SoftBand * (j.High - j.Low);
                    r.Joints.Add(solver.Add(a, b, new TwistLimit { LocalBasisA = j.BasisA, LocalBasisB = j.BasisB, MinimumAngle = j.Low + band, MaximumAngle = j.High - band, SpringSettings = SoftLimit }));
                }
                break;
            case PhysicsJointType.Spherical:
                r.Joints.Add(solver.Add(a, b, new BallSocket { LocalOffsetA = j.OffsetA, LocalOffsetB = j.OffsetB, SpringSettings = spring }));
                if (j.Limits)
                {
                    r.Joints.Add(solver.Add(a, b, new SwingLimit { AxisLocalA = j.AxisA, AxisLocalB = j.AxisB, MaximumSwingAngle = j.Swing, SpringSettings = spring }));
                    r.Joints.Add(solver.Add(a, b, new TwistLimit { LocalBasisA = j.BasisA, LocalBasisB = j.BasisB, MinimumAngle = j.Low, MaximumAngle = j.High, SpringSettings = spring }));
                    if (r.Soft)
                    {
                        float band = SoftBand * (j.High - j.Low);
                        r.Joints.Add(solver.Add(a, b, new SwingLimit { AxisLocalA = j.AxisA, AxisLocalB = j.AxisB, MaximumSwingAngle = j.Swing * (1 - SoftBand), SpringSettings = SoftLimit }));
                        r.Joints.Add(solver.Add(a, b, new TwistLimit { LocalBasisA = j.BasisA, LocalBasisB = j.BasisB, MinimumAngle = j.Low + band, MaximumAngle = j.High - band, SpringSettings = SoftLimit }));
                    }
                }
                break;
            default:
                var (ra, rb) = (r.Definition.Bodies[j.A].Bind, r.Definition.Bodies[j.B].Bind);
                var relative = rb.Then(ra.Inverse());
                r.Joints.Add(solver.Add(a, b, new Weld { LocalOffset = relative.Position, LocalOrientation = relative.Rotation, SpringSettings = spring }));
                break;
        }
    }


    internal void Hash(Piece piece, ref Meitou.Simulation.StateHasher hash)
    {
        hash.Add(piece.Frozen); hash.Add(piece.Carried); hash.Add(piece.Age); hash.Add(piece.Pose.Length);
        hash.Add(piece.Anchors?.Length ?? 0);
        if (piece.Anchors is { } anchors) foreach (int anchor in anchors) hash.Add(anchor);
        hash.Add(piece.Offsets?.Length ?? 0);
        if (piece.Offsets is { } offsets)
            foreach (var at in offsets) { hash.Add(at.Position); hash.Add(at.Rotation.X); hash.Add(at.Rotation.Y); hash.Add(at.Rotation.Z); hash.Add(at.Rotation.W); }
        foreach (bool dynamic in piece.Dynamic) hash.Add(dynamic);
        foreach (var at in piece.Pose) { hash.Add(at.Position); hash.Add(at.Rotation.X); hash.Add(at.Rotation.Y); hash.Add(at.Rotation.Z); hash.Add(at.Rotation.W); }
        foreach (var handle in piece.Bodies)
        {
            hash.Add(handle is not null);
            if (handle is not { } h) continue;
            var body = physics.Simulation.Bodies.GetBodyReference(h);
            hash.Add(body.Velocity.Linear); hash.Add(body.Velocity.Angular);
        }
    }
    public void Dispose() { physics.Dispose(); threads?.Dispose(); pieces.Clear(); shapes.Clear(); }
}
