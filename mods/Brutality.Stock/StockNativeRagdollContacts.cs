using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints.Contact;
using BepuUtilities;
using HarmonyLib;
using Meitou.Data.Physics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;
using NativeBody = Meitou.Simulation.Ragdolls.RagdollBody;
using NativePose = Meitou.Simulation.Ragdolls.RigidTransform;

namespace Meitou.Brutality.Physics;

/// <summary>Mirrors nearby native colliders in each fragment solver and returns their contact impulses to the native simulation.</summary>
internal static class StockNativeRagdollContacts
{
    internal enum Scene { Torso, Limb, Head }
    const int MaxRagdolls = 12, MaxProxies = 48, MaxFocus = 16;
    const int MaxPending = MaxProxies * 3;
    const float Nearby = 34 * 34;
    const float MaxReactionSpeed = 12, MaxReactionSpin = 2;
    const long FreshTicks = 12;
    static readonly ConditionalWeakTable<World, Feed> feeds = new();
    static NativeFields? fields;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed record NativeFields(FieldInfo Actors, FieldInfo Definition, FieldInfo Pose, FieldInfo Bodies, FieldInfo Mask,
        FieldInfo Frozen, FieldInfo Dynamic, FieldInfo Generation, FieldInfo Physics, FieldInfo Simulation);
    sealed record Interest(long Tick, Vector3[] Positions);
    sealed record Frame(long Tick, Collider[] Colliders);
    readonly record struct Key(CharacterId Owner, int Body);
    readonly record struct MovingBody(BodyHandle Handle, int Generation);
    readonly record struct Collider(Key Key, NativeBody Shape, NativePose Pose, MovingBody? Moving);
    readonly record struct Reaction(long Tick, MovingBody Target, Vector3 Impulse, Vector3 Torque);

    sealed class Feed
    {
        public readonly Interest?[] Interests = new Interest[3];
        public readonly Dictionary<Key, Reaction> Reactions = [];
        public Frame? Current;
        public bool ReactionReported;
    }

    internal static void Install(ModContext context)
    {
        var type = typeof(RagdollSystem);
        var instance = AccessTools.Inner(type, "Ragdoll");
        if (instance is null || AccessTools.Field(type, "_ragdolls") is not { } actors
            || AccessTools.Field(instance, "Definition") is not { } definition || AccessTools.Field(instance, "Pose") is not { } pose
            || AccessTools.Field(instance, "Bodies") is not { } bodies || AccessTools.Field(instance, "Mask") is not { } mask
            || AccessTools.Field(instance, "Frozen") is not { } frozen || AccessTools.Field(instance, "Dynamic") is not { } dynamic
            || AccessTools.Field(instance, "Tag") is not { } generation || AccessTools.Field(type, "_physics") is not { } physics
            || AccessTools.Field(physics.FieldType, "Simulation") is not { } simulation
            || AccessTools.Method(type, "Step", [typeof(World), typeof(float), typeof(float)]) is not { } step) return;
        try
        {
            context.Harmony.Patch(AccessTools.Method(type, nameof(RagdollSystem.SlowWorld)), postfix: new HarmonyMethod(
                typeof(StockNativeRagdollContacts), nameof(Publish)));
            context.Harmony.Patch(step, prefix: new HarmonyMethod(typeof(StockNativeRagdollContacts), nameof(ApplyReactions)));
            fields = new(actors, definition, pose, bodies, mask, frozen, dynamic, generation, physics, simulation);
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
            var dynamic = access.Frozen.GetValue(actor) is false ? access.Dynamic.GetValue(actor) as bool[] : null;
            int generation = (int)access.Generation.GetValue(actor)!;
            for (int body = 0; body < handles.Length; body++)
            {
                if (handles[body] is not { } handle || !Valid(poses[body])) continue;
                MovingBody? moving = dynamic is not null && body < dynamic.Length && dynamic[body] ? new(handle, generation) : null;
                colliders.Add(new(new(owner, body), definition.Bodies[body], poses[body], moving));
            }
        }
        Volatile.Write(ref feed.Current, new(world.Tick, colliders.ToArray()));
    }

    // The host has finished rebuilding and pinning its ragdolls when this hook runs, and has yet to solve them.
    static void ApplyReactions(RagdollSystem __instance, World world)
    {
        if (fields is not { } access || !feeds.TryGetValue(world, out var feed)) return;
        KeyValuePair<Key, Reaction>[] pending;
        lock (feed.Reactions)
        {
            if (feed.Reactions.Count == 0) return;
            pending = feed.Reactions.ToArray();
            feed.Reactions.Clear();
        }

        if (access.Actors.GetValue(__instance) is not IDictionary actors || access.Physics.GetValue(__instance) is not { } scene
            || access.Simulation.GetValue(scene) is not BepuPhysics.Simulation simulation) return;
        foreach (var (key, reaction) in pending)
        {
            if (!Fresh(world.Tick, reaction.Tick) || actors[key.Owner] is not { } actor || access.Frozen.GetValue(actor) is not false
                || access.Mask.GetValue(actor) is not RagdollPart mask || (mask & (RagdollPart.Whole | RagdollPart.CarryMode)) == 0
                || StockTorsoRules.TryGetSaved(world, key.Owner, out _) || access.Generation.GetValue(actor) is not int generation
                || generation != reaction.Target.Generation || access.Bodies.GetValue(actor) is not BodyHandle?[] handles
                || access.Dynamic.GetValue(actor) is not bool[] moving || (uint)key.Body >= handles.Length || (uint)key.Body >= moving.Length
                || !moving[key.Body] || handles[key.Body] != reaction.Target.Handle) continue;

            var body = simulation.Bodies.GetBodyReference(reaction.Target.Handle);
            if (!body.Exists || body.Kinematic || !float.IsFinite(reaction.Impulse.LengthSquared()) || !float.IsFinite(reaction.Torque.LengthSquared())) continue;
            ApplyReaction(body, reaction);
            if (trace && !feed.ReactionReported)
            {
                feed.ReactionReported = true;
                Console.WriteLine($"Brutality stock native ragdoll received fragment impulse owner={key.Owner} body={key.Body} impulse={reaction.Impulse.Length():F3}");
            }
        }
    }

    static void ApplyReaction(BodyReference body, Reaction reaction)
    {
        if (!body.Awake) body.Awake = true;
        var before = body.Velocity;
        body.ApplyLinearImpulse(reaction.Impulse);
        body.ApplyAngularImpulse(reaction.Torque);
        ref var velocity = ref body.Velocity;
        velocity.Linear = Clamp(before.Linear + Clamp(velocity.Linear - before.Linear, MaxReactionSpeed), RagdollPhysics.MaxSpeed);
        velocity.Angular = Clamp(before.Angular + Clamp(velocity.Angular - before.Angular, MaxReactionSpin), RagdollPhysics.MaxSpin);
    }

    static Vector3 Clamp(Vector3 vector, float max)
    {
        float squared = vector.LengthSquared();
        if (!float.IsFinite(squared)) return Vector3.Zero;
        return squared > max * max ? vector * (max / MathF.Sqrt(squared)) : vector;
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
        readonly record struct Proxy(Collider Source, TypedIndex Shape, BodyHandle Body);
        readonly Dictionary<Key, Proxy> active = [];
        readonly Dictionary<Key, Reaction> solved = [];
        readonly HashSet<Key> keep = [];
        Feed? feed;
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
            this.feed = frame is not null && active.Values.Any(proxy => proxy.Source.Moving.HasValue) ? feed : null;
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
                if (ReferenceEquals(proxy.Source.Shape, collider.Shape))
                {
                    var body = physics.Simulation.Bodies.GetBodyReference(proxy.Body);
                    if (body.Pose.Position != pose.Position || body.Pose.Orientation != pose.Orientation)
                    {
                        body.Awake = true;
                        body.Pose = pose;
                        body.UpdateBounds();
                    }
                    active[collider.Key] = proxy with { Source = collider };
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
            active.Add(collider.Key, new(collider, shape, handle));
            return true;
        }

        internal void Step(BepuPhysics.Simulation simulation, float dt, IThreadDispatcher? threads)
        {
            if (feed is not { } destination || last is not { } frame)
            {
                simulation.Timestep(dt, threads);
                return;
            }

            solved.Clear();
            void Capture(int _) => CaptureSubstep(simulation, frame.Tick);
            simulation.Solver.SubstepEnded += Capture;
            try
            {
                simulation.Timestep(dt, threads);
            }
            finally
            {
                simulation.Solver.SubstepEnded -= Capture;
            }

            if (solved.Count == 0) return;
            lock (destination.Reactions)
                foreach (var (key, reaction) in solved)
                {
                    if (destination.Reactions.TryGetValue(key, out var previous))
                    {
                        destination.Reactions[key] = previous.Target == reaction.Target && Fresh(reaction.Tick, previous.Tick)
                            ? reaction with { Impulse = previous.Impulse + reaction.Impulse, Torque = previous.Torque + reaction.Torque }
                            : reaction;
                    }
                    else if (destination.Reactions.Count < MaxPending) destination.Reactions.Add(key, reaction);
                }
        }

        // Each substep has its own applied impulse. Read from the solver's completion callback before the next one overwrites it.
        void CaptureSubstep(BepuPhysics.Simulation simulation, long tick)
        {
            foreach (var (key, proxy) in active)
            {
                if (proxy.Source.Moving is not { } target) continue;
                var mirror = simulation.Bodies.GetBodyReference(proxy.Body);
                var extract = new ContactImpulse();
                ref var constraints = ref mirror.Constraints;
                for (int i = 0; i < constraints.Count; i++)
                {
                    var constraint = constraints[i];
                    var handle = constraint.ConnectingConstraintHandle;
                    if (simulation.Solver.HandleToConstraint[handle.Value].SetIndex != 0) continue;
                    extract.Index = constraint.BodyIndexInConstraint;
                    simulation.NarrowPhase.TryExtractSolverContactPrestepAndImpulses(handle, ref extract);
                }

                if (!(extract.Impulse.LengthSquared() > 1e-6f)) continue;
                solved.TryGetValue(key, out var previous);
                solved[key] = new(tick, target, previous.Impulse + extract.Impulse, previous.Torque + extract.Torque);
            }
        }

        struct ContactImpulse : ISolverContactPrestepAndImpulsesExtractor
        {
            public int Index;
            public Vector3 Impulse, Torque;

            void ISolverContactPrestepAndImpulsesExtractor.ConvexTwoBody<TPrestep, TImpulses>(ref TPrestep prestep, ref TImpulses impulses)
            {
                Vector3Wide.ReadFirst(prestep.GetNormal(ref prestep), out var normal);
                Vector3 fromA = default;
                if (Index == 1)
                {
                    Vector3Wide.ReadFirst(prestep.GetOffsetB(ref prestep), out fromA);
                    normal = -normal;
                }

                for (int i = 0; i < prestep.ContactCount; i++)
                {
                    float magnitude = impulses.GetPenetrationImpulseForContact(ref impulses, i)[0];
                    if (!(magnitude > 0 && float.IsFinite(magnitude))) continue;
                    Vector3Wide.ReadFirst(prestep.GetContact(ref prestep, i).OffsetA, out var offsetA);
                    var impulse = normal * magnitude;
                    Impulse += impulse;
                    Torque += Vector3.Cross(offsetA - fromA, impulse);
                }
            }

            // All native proxies and fragment shapes here are convex, and a mirror is a body rather than a static.
            void ISolverContactPrestepAndImpulsesExtractor.ConvexOneBody<TPrestep, TImpulses>(ref TPrestep prestep, ref TImpulses impulses) { }

            void ISolverContactPrestepAndImpulsesExtractor.NonconvexOneBody<TPrestep, TImpulses>(ref TPrestep prestep, ref TImpulses impulses) { }

            void ISolverContactPrestepAndImpulsesExtractor.NonconvexTwoBody<TPrestep, TImpulses>(ref TPrestep prestep, ref TImpulses impulses) { }
        }

        void Remove(RagdollPhysics physics, Key key, Proxy proxy)
        {
            physics.Simulation.Bodies.Remove(proxy.Body);
            physics.Simulation.Shapes.RemoveAndDispose(proxy.Shape, physics.Pool);
            active.Remove(key);
        }
    }
}
