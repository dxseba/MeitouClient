using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using HarmonyLib;
using Meitou.Data.Physics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;
using NativeBody = Meitou.Simulation.Ragdolls.RagdollBody;
using NativePose = Meitou.Simulation.Ragdolls.RigidTransform;

namespace Meitou.Brutality.Physics;

/// <summary>Publishes nearby native colliders on the simulation thread and mirrors them in each fragment solver.</summary>
internal static class StockNativeRagdollContacts
{
    internal enum Scene { Torso, Limb, Head }
    const int MaxRagdolls = 12, MaxProxies = 48, MaxFocus = 16;
    const float Nearby = 34 * 34;
    const long FreshTicks = 12;
    static readonly ConditionalWeakTable<World, Feed> feeds = new();
    static NativeFields? fields;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed record NativeFields(FieldInfo Actors, FieldInfo Definition, FieldInfo Pose, FieldInfo Bodies, FieldInfo Mask);
    sealed record Interest(long Tick, Vector3[] Positions);
    sealed record Frame(long Tick, Collider[] Colliders);
    readonly record struct Key(CharacterId Owner, int Body);
    readonly record struct Collider(Key Key, NativeBody Shape, NativePose Pose);

    sealed class Feed
    {
        public readonly Interest?[] Interests = new Interest[3];
        public Frame? Current;
    }

    internal static void Install(ModContext context)
    {
        var type = typeof(RagdollSystem);
        var instance = AccessTools.Inner(type, "Ragdoll");
        if (instance is null || AccessTools.Field(type, "ragdolls") is not { } actors
            || AccessTools.Field(instance, "Definition") is not { } definition || AccessTools.Field(instance, "Pose") is not { } pose
            || AccessTools.Field(instance, "Bodies") is not { } bodies || AccessTools.Field(instance, "Mask") is not { } mask) return;
        try
        {
            context.Harmony.Patch(AccessTools.Method(type, nameof(RagdollSystem.SlowWorld)), postfix: new HarmonyMethod(
                typeof(StockNativeRagdollContacts), nameof(Publish)));
            fields = new(actors, definition, pose, bodies, mask);
        }
        catch (Exception error)
        {
            context.Log($"Brutality native ragdoll contacts unavailable: {error.GetBaseException().Message}");
        }
    }

    static void Publish(RagdollSystem __instance, World world)
    {
        if (fields is not { } access || !feeds.TryGetValue(world, out var feed)) return;
        var interest = new List<Interest>(feed.Interests.Length);
        for (int i = 0; i < feed.Interests.Length; i++)
            if (Volatile.Read(ref feed.Interests[i]) is { } row && Fresh(world.Tick, row.Tick)) interest.Add(row);
        if (interest.Count == 0 || access.Actors.GetValue(__instance) is not IDictionary actors)
        {
            Volatile.Write(ref feed.Current, null);
            return;
        }

        var nearby = new List<(CharacterId Owner, object Actor, float Distance)>();
        foreach (DictionaryEntry pair in actors)
        {
            if (pair.Key is not CharacterId owner || pair.Value is not { } actor
                || access.Mask.GetValue(actor) is not RagdollPart mask || (mask & (RagdollPart.Whole | RagdollPart.CarryMode)) == 0
                || access.Pose.GetValue(actor) is not NativePose[] { Length: > 0 } poses
                || StockTorsoRules.TryGetSaved(world, owner, out _)) continue;
            float distance = Nearby;
            foreach (var watcher in interest)
                distance = MathF.Min(distance, Distance(poses[0].Position, watcher.Positions));
            if (distance < Nearby) nearby.Add((owner, actor, distance));
        }

        var colliders = new List<Collider>();
        foreach (var (owner, actor, _) in nearby.OrderBy(row => row.Distance).Take(MaxRagdolls))
        {
            if (access.Definition.GetValue(actor) is not Meitou.Simulation.Ragdolls.RagdollDefinition definition
                || access.Pose.GetValue(actor) is not NativePose[] poses || access.Bodies.GetValue(actor) is not BodyHandle?[] handles
                || handles.Length != poses.Length || poses.Length != definition.Bodies.Length) continue;
            for (int body = 0; body < handles.Length; body++)
                if (handles[body].HasValue && Valid(poses[body])) colliders.Add(new(new(owner, body), definition.Bodies[body], poses[body]));
        }
        Volatile.Write(ref feed.Current, new(world.Tick, colliders.ToArray()));
    }

    static bool Fresh(long tick, long other) => other >= tick - FreshTicks && other <= tick + FreshTicks;

    static bool Valid(NativePose pose) => float.IsFinite(pose.Position.LengthSquared())
        && pose.Rotation.LengthSquared() is > .5f and < 1.5f;

    static float Distance(Vector3 point, Vector3[] positions)
    {
        float distance = float.PositiveInfinity;
        foreach (var position in positions) distance = MathF.Min(distance, Vector3.DistanceSquared(point, position));
        return distance;
    }

    internal sealed class Mirrors
    {
        readonly record struct Proxy(NativeBody Source, TypedIndex Shape, BodyHandle Body);
        readonly Dictionary<Key, Proxy> active = [];
        readonly HashSet<Key> keep = [];
        Frame? last;
        bool reported;

        internal void Sync(RagdollPhysics physics, World world, long tick, Scene scene, IEnumerable<Vector3> focus,
            Func<CharacterId, bool> replaces)
        {
            if (fields is null) return;
            var positions = focus.Take(MaxFocus).ToArray();
            var feed = feeds.GetValue(world, static _ => new());
            Volatile.Write(ref feed.Interests[(int)scene], positions.Length == 0 ? null : new(tick, positions));
            var frame = Volatile.Read(ref feed.Current);
            if (frame is not null && !Fresh(tick, frame.Tick)) frame = null;
            if (positions.Length == 0) frame = null;
            if (ReferenceEquals(frame, last)) return;
            last = frame;
            keep.Clear();
            if (frame is not null)
                foreach (var row in frame.Colliders.Where(row => !replaces(row.Key.Owner))
                    .Select(row => (Collider: row, Distance: Distance(row.Pose.Position, positions)))
                    .Where(row => row.Distance < Nearby).OrderBy(row => row.Distance).Take(MaxProxies))
                {
                    var collider = row.Collider;
                    if (Apply(physics, collider)) keep.Add(collider.Key);
                }
            foreach (var (key, proxy) in active)
                if (!keep.Contains(key)) Remove(physics, key, proxy);
            if (trace && !reported && active.Count > 0)
            {
                reported = true;
                Console.WriteLine($"Brutality stock native ragdoll contacts scene={scene} colliders={active.Count}");
            }
        }

        bool Apply(RagdollPhysics physics, Collider collider)
        {
            var pose = new RigidPose(collider.Pose.Position, collider.Pose.Rotation);
            if (active.TryGetValue(collider.Key, out var proxy))
            {
                if (ReferenceEquals(proxy.Source, collider.Shape))
                {
                    var body = physics.Simulation.Bodies.GetBodyReference(proxy.Body);
                    if (body.Pose.Position != pose.Position || body.Pose.Orientation != pose.Orientation)
                    {
                        body.Awake = true;
                        body.Pose = pose;
                        body.UpdateBounds();
                    }
                    return true;
                }
                Remove(physics, collider.Key, proxy);
            }

            var shapes = physics.Simulation.Shapes;
            var bodyShape = collider.Shape;
            var size = bodyShape.Size;
            if (!float.IsFinite(size.LengthSquared())) return false;
            TypedIndex shape;
            switch (bodyShape.Type)
            {
                case PhysicsShapeType.Sphere: shape = shapes.Add(new Sphere(MathF.Max(size.X, .005f))); break;
                case PhysicsShapeType.Capsule: shape = shapes.Add(new Capsule(MathF.Max(size.X, .005f), MathF.Max(size.Y, 0))); break;
                case PhysicsShapeType.Mesh:
                    if (!RagdollHull.TryCreate(bodyShape.Hull, physics.Pool, out var hull, out _)) return false;
                    shape = shapes.Add(hull); break;
                default:
                    size = Vector3.Max(size, new(.005f));
                    shape = shapes.Add(new Box(size.X, size.Y, size.Z)); break;
            }
            var handle = physics.AddBody(BodyDescription.CreateKinematic(pose, new CollidableDescription(shape, .4f),
                new BodyActivityDescription(.06f)), new BodyTag { NativeMirror = true });
            active.Add(collider.Key, new(bodyShape, shape, handle));
            return true;
        }

        void Remove(RagdollPhysics physics, Key key, Proxy proxy)
        {
            physics.Simulation.Bodies.Remove(proxy.Body);
            physics.Simulation.Shapes.RemoveAndDispose(proxy.Shape, physics.Pool);
            active.Remove(key);
        }
    }
}
