using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality.Rendering;

/// <summary>Transient head fragments in a bounded scene on the display thread.</summary>
internal sealed class HeadBurstRagdolls(IRagdollEnvironment ground) : IDisposable
{
    internal const int Capacity = 45;
    internal const float Lifetime = 6;
    internal bool HasRoom(int count) => pieces.Count <= Capacity - count;
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
        public required Quaternion InitialRotation;
        public required float Radius, Bounce;
        public required double Expires;
        public RigidPose Previous;
        public float DownwardSpeed;
        public float CollisionDelay = .13f;
        public bool Landed;
    }

    internal void Advance(double shownSeconds, float speed, long tick)
    {
        if (!double.IsFinite(shownSeconds)) return;
        float elapsed = previousTime is { } previous
            ? (float)Math.Clamp((shownSeconds - previous) / Math.Max(1, speed), 0, MaxCatchUp) : 0;
        previousTime = shownSeconds;
        if (physics is not { } active) return;
        expired.Clear();
        foreach (var (key, piece) in pieces)
            if (shownSeconds >= piece.Expires) expired.Add(key);
        foreach (long key in expired) Remove(key);
        if (physics is null) return;
        if (active.Simulation.Bodies.ActiveSet.Count == 0) { due = 0; return; }
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
                if (piece.DownwardSpeed < -4) ResolveLanding(active, piece);
            due -= StepSeconds;
        }
        active.Sweep(tick);
    }

    void ResolveLanding(RagdollPhysics active, Piece piece)
    {
        var body = active.Simulation.Bodies.GetBodyReference(piece.Body);
        var at = body.Pose.Position;
        float floor = ground.GroundHeight(at.X, at.Z, at.Y);
        if (!float.IsFinite(floor)) return;
        var axes = Matrix4x4.CreateFromQuaternion(body.Pose.Orientation);
        float height = Vector3.Dot(piece.HalfExtent, Vector3.Abs(new(axes.M12, axes.M22, axes.M32)));
        float gap = at.Y - height - floor;
        bool contact = gap <= .10f && body.Velocity.Linear.Y - piece.DownwardSpeed > 3;
        // A small body may cross a one-sided floor before a new terrain tile reaches the coverage budget.
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
        body.Velocity.Linear.Y = MathF.Max(body.Velocity.Linear.Y, -piece.DownwardSpeed * piece.Bounce);
        if (preview) Console.WriteLine($"Brutality head shard contacted ground: position={body.Pose.Position}, rebound={body.Velocity.Linear.Y:F2}");
    }

    internal CharacterInstance? Place(in CharacterInstance source, HeadBurstShard shard, FragmentBirthPresentation birth,
        Vector3 head, Quaternion headRotation, Vector3 bindHead, Vector2 travel, double expires, ulong seed, long key, int group)
    {
        if (!pieces.TryGetValue(key, out var piece))
        {
            if (pieces.Count >= Capacity
                || !Matrix4x4.Decompose(birth.Placement, out var size, out var turn, out _)) return null;
            var start = head + Vector3.TransformNormal(Vector3.Transform(shard.Centre - bindHead, headRotation), birth.Placement);
            var orientation = Quaternion.Normalize(Quaternion.Concatenate(headRotation, turn));
            var half = Vector3.Clamp(shard.HalfExtent * Vector3.Abs(size) * .76f, new(.06f), new(2));
            float radius = MathF.Max(.09f, shard.Radius * Vector3.Abs(size).Length() / MathF.Sqrt(3));
            if (!float.IsFinite(start.LengthSquared()) || !float.IsFinite(orientation.LengthSquared())
                || !float.IsFinite(half.LengthSquared()) || !float.IsFinite(radius)) return null;
            ulong roll = Rng.Mix(seed ^ (ulong)shard.Key);
            float angle = MathF.Tau * Rng.Float(roll);
            var scatter = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            var sideways = start - head;
            sideways.Y = 0;
            var radial = sideways.LengthSquared() > .0025f ? Vector3.Normalize(sideways) : scatter;
            float speed = (shard.Brain ? 7 : 10) + 7 * Rng.Float(Rng.Mix(roll));
            var velocity = radial * speed + scatter * 4
                + new Vector3(travel.X * 7, 10 + 9 * Rng.Float(Rng.Mix(roll ^ 61)), travel.Y * 7);
            var spin = Vector3.Normalize(new Vector3(scatter.X, .45f, scatter.Z)) * (4 + 3 * Rng.Float(Rng.Mix(roll ^ 103)));
            var active = physics ??= new RagdollPhysics();
            var box = new Box(half.X * 2, half.Y * 2, half.Z * 2);
            var shape = active.Simulation.Shapes.Add(box);
            var initial = new RigidPose(start + radial * .12f, orientation);
            var description = BodyDescription.CreateDynamic(initial, new BodyVelocity(velocity, spin),
                box.ComputeInertia(shard.Brain ? .08f : .18f), new CollidableDescription(shape, 1.2f),
                new BodyActivityDescription(.06f));
            // Fresh pieces start adjacent in the intact pose. Enable their mutual contacts once they have separated.
            var handle = active.AddBody(description, new BodyTag { Ragdoll = group, Index = shard.Key - 20, Joined = ulong.MaxValue });
            pieces.Add(key, piece = new Piece
            {
                Body = handle, Shape = shape, Start = start, HalfExtent = half, InitialRotation = orientation,
                Previous = initial, Radius = radius, Bounce = shard.Brain ? .12f : .24f, Expires = expires,
            });
            if (preview) Console.WriteLine($"Brutality head shard ragdoll: part={shard.Key}, brain={shard.Brain}, start={start}, velocity={velocity}");
        }
        var body = physics!.Simulation.Bodies.GetBodyReference(piece.Body);
        float alpha = body.Awake ? due / StepSeconds : 1;
        var at = Vector3.Lerp(piece.Previous.Position, body.Pose.Position, alpha);
        var spinDelta = Quaternion.Slerp(piece.Previous.Orientation, body.Pose.Orientation, alpha) * Quaternion.Inverse(piece.InitialRotation);
        var world = birth.Placement * Matrix4x4.CreateTranslation(-piece.Start)
            * Matrix4x4.CreateFromQuaternion(spinDelta) * Matrix4x4.CreateTranslation(at);
        return source with
        {
            Key = key, Appearance = birth.Appearance, Geometry = shard.Geometry, Position = at, Bones = birth.Bones,
            World = world, WorldBounds = new CharacterWorldBounds(at, piece.Radius), Poses = [], Bolts = [],
            FreezeFrom = null, BlendFrom = null, Blend = 0, RuntimeShaderData = null,
        };
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
        due = 0;
    }
}
