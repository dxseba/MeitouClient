using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using Meitou.Brutality.Physics;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality;

/// <summary>Transient head shards in a bounded physics scene on the display thread.</summary>
internal sealed class StockHeadShardPhysics(IRagdollEnvironment ground) : IDisposable
{
    internal const int Capacity = 45;
    internal const float Lifetime = 6;
    const float StepSeconds = 1f / 60;
    const float MaxCatchUp = 4 * StepSeconds;

    readonly Dictionary<long, Piece> pieces = [];
    readonly List<long> expired = [];
    readonly bool preview = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") is not null;
    RagdollPhysics? physics;
    double? previousTime;
    float due;

    sealed class Piece
    {
        public required BodyHandle Body;
        public required TypedIndex Shape;
        public required Vector3 Start, HalfExtent;
        public required Quaternion InverseInitialRotation;
        public required Matrix4x4 SourceWorld;
        public required float Bounce;
        public required float Lifetime;
        public RigidPose Previous;
        public float Age, DownwardSpeed;
        public float CollisionDelay = .13f;
        public bool Landed;
    }

    internal void SyncNativeContacts(World world, long tick, Func<CharacterId, bool> replaces)
    {
        if (physics is not { } active) return;
        active.NativeContacts.Sync(active, world, tick, StockNativeRagdollContacts.Scene.Head,
            pieces.Values.Where(piece => active.Simulation.Bodies.GetBodyReference(piece.Body).Awake)
                .Select(piece => active.Simulation.Bodies.GetBodyReference(piece.Body).Pose.Position), replaces);
    }

    internal bool HasRoom(int count) => count >= 0 && count <= Capacity - pieces.Count;

    internal bool Resting(long key) => physics is { } active && pieces.TryGetValue(key, out var piece)
        && !active.Simulation.Bodies.GetBodyReference(piece.Body).Awake;

    internal void Advance(double shownSeconds, float speed, long tick)
    {
        if (!double.IsFinite(shownSeconds)) return;
        float elapsed = previousTime is { } previous
            ? (float)Math.Clamp((shownSeconds - previous) / (speed >= 1 ? speed : 1), 0, MaxCatchUp) : 0;
        previousTime = shownSeconds;
        if (physics is not { } active) return;

        expired.Clear();
        foreach (var (key, piece) in pieces)
            if ((piece.Age += elapsed) >= piece.Lifetime) expired.Add(key);
        foreach (long key in expired) Remove(key);
        if (physics is null) return;
        if (!pieces.Values.Any(piece => active.Simulation.Bodies.GetBodyReference(piece.Body).Awake))
        {
            due = 0;
            return;
        }
        due = Math.Min(due + elapsed, MaxCatchUp);
        if (due < StepSeconds) return;

        int tiles = 8;
        foreach (var piece in pieces.Values)
        {
            var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
            if (body.Awake) active.Cover(body.Pose.Position, 5, ground, tick, ref tiles);
        }
        while (due >= StepSeconds)
        {
            foreach (var piece in pieces.Values)
            {
                var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
                piece.Previous = body.Pose;
                piece.DownwardSpeed = body.Awake ? body.Velocity.Linear.Y : 0;
                if (piece.CollisionDelay > 0 && (piece.CollisionDelay -= StepSeconds) <= 0)
                    active.Tag(piece.Body).Joined = 0;
            }
            active.Step(StepSeconds);
            foreach (var piece in pieces.Values)
                if (piece.DownwardSpeed < 0) ResolveLanding(active, piece);
            due -= StepSeconds;
        }
        active.Sweep(tick);
    }

    internal Matrix4x4? Place(long key, int shardKey, Vector3 shardWorld, Vector3 cutOrigin,
        Quaternion initialOrientation, Matrix4x4 sourceWorld, Vector3 halfExtent, Vector2 travel,
        bool brain, bool burst, float lifetime, ulong seed, int group, bool robot = false, bool native = false)
    {
        if (!pieces.TryGetValue(key, out var piece))
        {
            float rotationLength = initialOrientation.LengthSquared();
            if (!HasRoom(1) || !float.IsFinite(shardWorld.LengthSquared()) || !float.IsFinite(cutOrigin.LengthSquared())
                || !float.IsFinite(rotationLength) || rotationLength < .00001f || !float.IsFinite(sourceWorld.GetDeterminant())
                || !float.IsFinite(halfExtent.LengthSquared()) || !float.IsFinite(travel.LengthSquared())
                || !(lifetime > 0)) return null;

            var orientation = Quaternion.Normalize(initialOrientation);
            var half = Vector3.Clamp(Vector3.Abs(halfExtent) * (native ? .90f : .76f), new(.06f), new(native ? 3 : 2));
            ulong roll = Rng.Mix(seed ^ (ulong)shardKey);
            float angle = MathF.Tau * Rng.Float(roll);
            var scatter = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            var sideways = shardWorld - cutOrigin;
            sideways.Y = 0;
            var radial = sideways.LengthSquared() > .0025f ? Vector3.Normalize(sideways) : scatter;
            float speed = (brain ? 5 : 7) + 4 * Rng.Float(Rng.Mix(roll));
            var velocity = radial * speed + scatter * 2 + new Vector3(travel.X * 3, 0, travel.Y * 3);
            var spin = Vector3.Normalize(new Vector3(scatter.X, .45f, scatter.Z)) * (3 + 2 * Rng.Float(Rng.Mix(roll ^ 103)));
            if (burst) velocity.Y = 5 + 3 * Rng.Float(Rng.Mix(roll ^ 61));
            else
            {
                velocity *= .24f;
                spin *= .55f;
            }
            if (robot) { velocity *= .42f; spin *= .50f; }
            if (!float.IsFinite(velocity.LengthSquared())) return null;

            var active = physics ??= new RagdollPhysics();
            var box = new Box(half.X * 2, half.Y * 2, half.Z * 2);
            var shape = active.Simulation.Shapes.Add(box);
            var initial = new RigidPose(shardWorld + radial * .12f, orientation);
            var description = BodyDescription.CreateDynamic(initial, new BodyVelocity(velocity, spin),
                box.ComputeInertia(robot ? .5f : brain ? .08f : .18f), new CollidableDescription(shape, 1.2f),
                new BodyActivityDescription(.06f));
            // Fresh pieces start adjacent in the intact pose. Allow their mutual contacts once they separate.
            var handle = active.AddBody(description, new BodyTag { Ragdoll = group, Index = shardKey & 63, Joined = ulong.MaxValue });
            pieces.Add(key, piece = new Piece
            {
                Body = handle, Shape = shape, Start = shardWorld, HalfExtent = half,
                InverseInitialRotation = Quaternion.Conjugate(orientation), SourceWorld = sourceWorld,
                Previous = initial, Bounce = burst && !brain ? .14f : .08f, Lifetime = lifetime,
            });
            if (preview) Console.WriteLine($"Brutality head shard ragdoll: part={shardKey}, brain={brain}, start={shardWorld}, velocity={velocity}");
        }

        return Transform(key);
    }

    internal StockHeadSplitSave.Pose? SavePose(long key)
    {
        if (physics is not { } active || !pieces.TryGetValue(key, out var piece)
            || Position(key) is not { } position || Transform(key) is not { } transform) return null;
        var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
        var orientation = Quaternion.Slerp(piece.Previous.Orientation, body.Pose.Orientation, Math.Clamp(due / StepSeconds, 0, 1));
        StockHeadSplitSave.Motion? moving = body.Awake
            ? new(orientation, body.Velocity.Linear, body.Velocity.Angular, piece.HalfExtent) : null;
        return new(position, transform, moving);
    }

    internal bool Resume(long key, int shardKey, StockHeadSplitSave.Pose pose, int group, bool robot)
    {
        if (pose.Moving is not { } motion || !StockHeadSplitSave.ValidMotion(motion)
            || !HasRoom(1) || pieces.ContainsKey(key)) return false;
        var active = physics ??= new RagdollPhysics();
        var half = motion.HalfExtent;
        var box = new Box(half.X * 2, half.Y * 2, half.Z * 2);
        var shape = active.Simulation.Shapes.Add(box);
        var orientation = Quaternion.Normalize(motion.Orientation);
        var initial = new RigidPose(pose.Position, orientation);
        var description = BodyDescription.CreateDynamic(initial, new BodyVelocity(motion.Linear, motion.Angular),
            box.ComputeInertia(robot ? .5f : .18f), new CollidableDescription(shape, 1.2f), new BodyActivityDescription(.06f));
        var handle = active.AddBody(description, new BodyTag { Ragdoll = group, Index = shardKey & 63, Joined = ulong.MaxValue });
        pieces.Add(key, new Piece
        {
            Body = handle, Shape = shape, Start = pose.Position, HalfExtent = half,
            InverseInitialRotation = Quaternion.Conjugate(orientation), SourceWorld = pose.Transform,
            Previous = initial, Bounce = .08f, Lifetime = float.PositiveInfinity,
        });
        return true;
    }

    internal Matrix4x4? Transform(long key)
    {
        if (physics is not { } active || !pieces.TryGetValue(key, out var piece)) return null;
        var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
        float alpha = body.Awake ? due / StepSeconds : 1;
        var at = Vector3.Lerp(piece.Previous.Position, body.Pose.Position, alpha);
        var spinDelta = Quaternion.Slerp(piece.Previous.Orientation, body.Pose.Orientation, alpha) * piece.InverseInitialRotation;
        return piece.SourceWorld * Matrix4x4.CreateTranslation(-piece.Start)
            * Matrix4x4.CreateFromQuaternion(spinDelta) * Matrix4x4.CreateTranslation(at);
    }

    internal Vector3? Position(long key)
    {
        if (physics is not { } active || !pieces.TryGetValue(key, out var piece)) return null;
        var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
        return Vector3.Lerp(piece.Previous.Position, body.Pose.Position, body.Awake ? due / StepSeconds : 1);
    }

    /// <summary>Gets the floor under a head once the collider is touching it and the head has slowed down.</summary>
    internal Vector3? PoolContact(long key)
    {
        if (physics is not { } active || !pieces.TryGetValue(key, out var piece)) return null;
        var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
        if (body.Awake && (body.Velocity.Linear.LengthSquared() > 2.25f || body.Velocity.Angular.LengthSquared() > 2.25f)) return null;
        var at = body.Pose.Position;
        float floor = ground.GroundHeight(at.X, at.Z, at.Y);
        float gap = at.Y - Height(piece, body.Pose.Orientation) - floor;
        return gap is >= -.40f and <= .28f ? new(at.X, floor, at.Z) : null;
    }

    static float Height(Piece piece, Quaternion rotation)
    {
        var axes = Matrix4x4.CreateFromQuaternion(rotation);
        return Vector3.Dot(piece.HalfExtent, Vector3.Abs(new(axes.M12, axes.M22, axes.M32)));
    }

    void ResolveLanding(RagdollPhysics active, Piece piece)
    {
        var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
        var at = body.Pose.Position;
        float floor = ground.GroundHeight(at.X, at.Z, MathF.Max(at.Y, piece.Previous.Position.Y));
        if (!float.IsFinite(floor)) return;
        float height = Height(piece, body.Pose.Orientation);
        float gap = at.Y - height - floor;
        bool contact = gap <= .10f && body.Velocity.Linear.Y - piece.DownwardSpeed > 3;
        // Catch fast shards that cross a one-sided floor before the coverage budget reaches their tile.
        if (gap < -.12f)
        {
            body.Awake = true;
            body = active.Simulation.Bodies.GetBodyReference(piece.Body);
            body.Pose.Position.Y = floor + height;
            body.Velocity.Linear.Y = MathF.Max(0, body.Velocity.Linear.Y);
            body.UpdateBounds();
            contact = true;
        }
        if (!contact || piece.Landed) return;
        piece.Landed = true;
        body.Awake = true;
        body = active.Simulation.Bodies.GetBodyReference(piece.Body);
        body.Velocity.Linear.Y = MathF.Min(-piece.DownwardSpeed * piece.Bounce, 4);
        if (preview) Console.WriteLine($"Brutality head shard contacted ground: position={body.Pose.Position}, rebound={body.Velocity.Linear.Y:F2}");
    }

    internal void Remove(long key)
    {
        if (!pieces.Remove(key, out var piece) || physics is not { } active) return;
        active.Simulation.Bodies.Remove(piece.Body);
        active.Simulation.Shapes.RemoveAndDispose(piece.Shape, active.Pool);
        if (pieces.Count == 0) Dispose();
    }

    public void Dispose()
    {
        physics?.Dispose();
        physics = null;
        pieces.Clear();
        previousTime = null;
        due = 0;
    }
}
