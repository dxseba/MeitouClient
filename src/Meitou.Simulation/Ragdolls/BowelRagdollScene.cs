using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using Meitou.Data.Physics;

namespace Meitou.Simulation.Ragdolls;

/// <summary>Transient jointed bowel props, owned and stepped by one display thread, independently of simulation ragdolls.</summary>
public sealed class BowelRagdollScene : IDisposable
{
    public const int Capacity = 32, NodeCount = 9;
    public const float FixedStep = 1f / 120, FreezeEarliest = 2, FreezeLatest = 12;
    const float NodeMass = 0.03f;
    static readonly SpringSettings TissueSpring = new(5, 1);

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
        public bool Frozen, Placed;
    }

    readonly RagdollPhysics physics = new();
    readonly Dictionary<long, Rig> rigs = [];
    int nextTag = 1;
    float due;
    bool disposed;

    public int Count => rigs.Count;
    public int Moving => rigs.Values.Count(r => !r.Frozen);

    public bool Add(long key, BowelRigGeometry geometry, Vector3 position, Quaternion rotation, Vector3 velocity, bool resting = false)
    {
        if (disposed || rigs.ContainsKey(key) || rigs.Count >= Capacity || !Valid(geometry)
            || !Finite(position) || !Finite(velocity) || !float.IsFinite(rotation.LengthSquared())
            || rotation.LengthSquared() < 1e-8f) return false;
        rotation = Quaternion.Normalize(rotation);
        if (velocity.LengthSquared() > 10000) velocity = Vector3.Normalize(velocity) * 100;
        var sphere = new Sphere(geometry.Radius);
        var shape = physics.Simulation.Shapes.Add(sphere);
        var rig = new Rig { Key = key, Rest = (Vector3[])geometry.RestNodes.Clone(), Bodies = new BodyHandle[NodeCount],
            Shape = shape, Radius = geometry.Radius, Transforms = new Matrix4x4[NodeCount] };
        int created = 0, tag = nextTag++;
        try
        {
            for (int i = 0; i < NodeCount; i++)
            {
                var at = position + Vector3.Transform(rig.Rest[i], rotation);
                var description = BodyDescription.CreateDynamic(new RigidPose(at, rotation), new BodyVelocity(velocity),
                    sphere.ComputeInertia(NodeMass), RagdollPhysics.Collidable(shape), new BodyActivityDescription(0.01f));
                rig.Bodies[i] = physics.AddBody(description,
                    new BodyTag { Ragdoll = tag, Index = i, Joined = (1UL << NodeCount) - 1 });
                created++;
            }
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                {
                    int a = row * 3 + column;
                    if (column < 2) Join(rig, a, a + 1);
                    if (row < 2) Join(rig, a, a + 3);
                }
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

    void Join(Rig rig, int a, int b)
    {
        var centre = (rig.Rest[a] + rig.Rest[b]) * 0.5f;
        rig.Joints.Add(physics.Simulation.Solver.Add(rig.Bodies[a], rig.Bodies[b], new BallSocket
        {
            LocalOffsetA = centre - rig.Rest[a], LocalOffsetB = centre - rig.Rest[b], SpringSettings = TissueSpring,
        }));
    }

    public BowelPose? PoseOf(long key) => rigs.TryGetValue(key, out var rig) ? rig.Pose : null;

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
        due += MathF.Min(seconds, 0.25f);
        while (due >= FixedStep)
        {
            foreach (var rig in rigs.Values)
                if (!rig.Frozen)
                {
                    physics.Cover(rig.Pose!.Centre, MathF.Max(4, rig.Rest.Max(p => p.Length()) + rig.Radius + 1), environment, tick);
                    Surface(rig, environment);
                }
            if (rigs.Values.Any(r => !r.Frozen)) physics.Step(FixedStep);
            foreach (var rig in rigs.Values)
                if (!rig.Frozen)
                {
                    rig.Age += FixedStep;
                    Surface(rig, environment);
                    ReadPose(rig);
                    if (rig.Age >= FreezeLatest || rig.Age >= FreezeEarliest && Still(rig)) Freeze(rig);
                }
            due -= FixedStep;
        }
        physics.Sweep(tick);
    }

    void Surface(Rig rig, IRagdollEnvironment environment, bool initial = false)
    {
        foreach (var handle in rig.Bodies)
        {
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            var p = body.Pose.Position;
            float floor = environment.GroundHeight(p.X, p.Z, p.Y);
            if (!float.IsFinite(floor) || p.Y >= floor + rig.Radius * (initial ? 1 : 0.5f)) continue;
            if (!body.Awake) body.Awake = true;
            body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.Pose.Position.Y = floor + rig.Radius;
            body.Velocity.Linear.Y = MathF.Max(0, body.Velocity.Linear.Y);
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
