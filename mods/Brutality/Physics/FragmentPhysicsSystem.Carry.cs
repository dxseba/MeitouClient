using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Brutality.Physics;

public sealed partial class FragmentPhysicsSystem
{
    sealed class CarryState
    {
        public required AdvancedFragmentScene.Piece Piece;
        public CharacterId Holder;
        public int Side = 1;
        public required bool[] Connected, Pinned, PinnedBones;
        public required int[] Components;
        public RagdollSkinSurface.Grip? Surface;
        public int Body;
        public Vector3 Local;
        public bool GripSelected;
        public TorsoBisectionState? Cut;
        public IReadOnlyList<LimbCutState> Limbs = [];
    }
    sealed class ContactShape
    {
        public BodyBuild Build;
        public CharacterAppearance? Look;
        public CharacterPoseSetup? Original, Setup;
    }
    readonly ConditionalWeakTable<CharacterCold, ContactShape> contactShapes = new();

    static Matrix4x4 PlacementFor(Owner owner, CharacterCold cold, in CharacterHot hot)
    {
        if (cold.CarriedBy.IsNone) return CharacterRigs.Placement(owner.Setup, hot.Position, hot.Yaw, 1);
        var (at, rotation) = (cold.CarryAttach ?? CarryPose.Fallback).World(hot.Position, hot.Yaw);
        return CharacterRigs.Placement(owner.Setup, at, rotation, 1);
    }
    void UpdateCarries(World world)
    {
        Dictionary<CharacterId, BrutalityActorView>? states = null;
        var proxies = BodyFragmentProxySystem.ForWorld(world);
        foreach (var (id, owner) in owners)
        {
            RestoreProxyPlacement(world, id, owner, proxies);
            foreach (int key in owner.Carries.Keys.Where(key => !owner.Pieces.TryGetValue(key, out var active)
                || !ReferenceEquals(active.Piece, owner.Carries[key].Piece)).ToArray())
            { scene.ReleaseCarried(owner.Carries[key].Piece); owner.Carries.Remove(key); }
            foreach (int key in new[] { 0, 1 })
            {
                if (!owner.Pieces.TryGetValue(key, out var entry)) continue;
                var piece = entry.Piece;
                CharacterId body = id; CharacterCold? cold = owner.Cold;
                if (key == 1)
                {
                    cold = null;
                    if (proxies is not null && proxies.TryProxy(id, key, out var proxy, out var proxyCold)) { body = proxy; cold = proxyCold; }
                }
                // A restored held half stays kinematic while its saved proxy/carrier relation is still resolving.
                if (key == 1 && piece.Carried && (cold is null || cold.CarriedBy.IsNone) && proxies?.HasPendingProxy(id, key) == true) continue;
                bool carried = cold is not null && world.Characters.TryResolveNext(cold.CarriedBy, out _);
                if (!carried || !world.Characters.TryResolveNext(body, out int bodySlot))
                {
                    if (owner.Carries.Remove(key, out var released)) scene.ReleaseCarried(released.Piece);
                    else if (piece.Carried) scene.ReleaseCarried(piece);
                    continue;
                }
                var carriedCold = cold ?? throw new InvalidOperationException("A carried half needs its native carry owner.");
                bool entering = !owner.Carries.TryGetValue(key, out var previous) || !ReferenceEquals(previous.Piece, piece)
                    || previous.Holder != carriedCold.CarriedBy;
                if (entering)
                {
                    if (previous is not null) scene.ReleaseCarried(previous.Piece);
                    states ??= gameplay.ActorViews.ToDictionary(view => view.Character);
                    owner.Carries[key] = CarryBinding(owner, piece, carriedCold.CarriedBy, states.GetValueOrDefault(id), key == 1 ? -1 : 1);
                }
                var state = owner.Carries[key];
                var placement = key == 0 ? owner.Placement : PlacementFor(owner, carriedCold, world.Characters.Next[bodySlot]);
                var layers = CarryLayers(owner.Setup, carriedCold, forceCarry: true);
                var (target, bones) = AnimatedCarry(owner, piece, layers, placement);
                AdvancedFragmentScene.RefreshCarryOffsets(piece, owner.Setup.Rig, bones, target, state.Connected);
                if (UpperArmSupport(world, state.Holder) is { } support)
                    AlignGrip(owner, state, target, layers, support, entering);
                scene.SetCarried(piece, target, state.Connected, state.Pinned, entering);
            }
        }
    }
    void RestoreProxyPlacement(World world, CharacterId id, Owner owner, BodyFragmentProxySystem? proxies)
    {
        if (proxies is null || !owner.Pieces.TryGetValue(1, out var negative)) return;
        if (!proxies.TryTakeRestoredPlacement(id, 1, out var placement))
        {
            if (!proxies.HasPendingProxy(id, 1)) owner.NegativePoseRestored = false;
            return;
        }
        // The proxy records only a picking/carry root; the articulated physics sidecar is authoritative.
        if (owner.NegativePoseRestored) { owner.NegativePoseRestored = false; return; }
        if (!world.Characters.TryResolveNext(id, out int slot)) return;
        RestoreLogicalPlacement(owner, negative.Piece, 1, placement, world.Characters.Next[slot].Yaw);
    }
    void RestoreLogicalPlacement(Owner owner, AdvancedFragmentScene.Piece piece, int key, BodyFragmentPlacement placement, float sourceYaw, bool release = false)
    {
        var original = scene.Capture(piece, key, "fallback-placement", owner.Setup.Rig);
        var centre = piece.Pose[piece.Definition.Root].Position;
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, placement.Yaw - sourceYaw);
        var connected = release ? AdvancedFragmentScene.ConnectedRoot(piece.Definition) : Enumerable.Repeat(true, piece.Pose.Length).ToArray();
        var moved = original with
        {
            Pose = original.Pose.Select((at, body) => connected[body] ? new RigidTransform(placement.Position + Vector3.Transform(at.Position - centre, turn),
                Quaternion.Normalize(turn * at.Rotation)) : at).ToArray(),
            Linear = original.Linear.Select((velocity, body) => !connected[body] ? velocity : release ? Vector3.Zero : Vector3.Transform(velocity, turn)).ToArray(),
            Angular = original.Angular.Select((velocity, body) => !connected[body] ? velocity : release ? Vector3.Zero : Vector3.Transform(velocity, turn)).ToArray(),
            Frozen = !release && original.Frozen, Carried = !release && original.Carried,
            Dynamic = release ? Enumerable.Repeat(true, original.Dynamic.Length).ToArray() : original.Dynamic, Age = release ? 0 : original.Age,
        };
        scene.Restore(piece, moved);
    }
    static CarryState CarryBinding(Owner owner, AdvancedFragmentScene.Piece piece, CharacterId holder, BrutalityActorView? actor, int side)
    {
        var definition = piece.Definition; var rig = owner.Setup.Rig;
        var connected = AdvancedFragmentScene.ConnectedRoot(definition);
        var pinned = definition.Bodies.Select((body, b) => connected[b] && body.Name is not ("Bip01 L Forearm" or "Bip01 R Forearm" or "Bip01 Head")).ToArray();
        if (!pinned.Contains(true)) pinned[definition.Root] = true;
        var components = new int[rig.BoneCount]; var pinnedBones = new bool[rig.BoneCount];
        for (int h = 0; h < components.Length; h++)
        {
            int at = h;
            while (at >= 0 && (at >= definition.BodyOfBone.Length || definition.BodyOfBone[at] < 0)) at = rig.Parent[at];
            int body = at >= 0 ? definition.BodyOfBone[at] : -1;
            components[h] = body >= 0 && connected[body] ? 0 : body + 2;
            pinnedBones[h] = body >= 0 && pinned[body];
        }
        return new() { Piece = piece, Holder = holder, Side = side, Connected = connected, Pinned = pinned, PinnedBones = pinnedBones, Components = components, Body = definition.Root, Cut = actor?.Torso, Limbs = actor?.Limbs ?? [] };
    }
    ActiveLayer[] CarryLayers(CharacterPoseSetup setup, CharacterCold cold, bool forceCarry)
    {
        var layers = new List<ActiveLayer>(); bool hasCarry = false;
        if (cold.Animation is { } animation && animations is not null)
            foreach (var layer in animations.Publish(animation, []))
                if (setup.Rig.Layer(rigs.Database, layer.Name, setup.Animal) is { } definition)
                { layers.Add(new(definition, MathF.Max(0, layer.Time), layer.Weight)); hasCarry |= layer.Name == CarrySystem.CarriedClip; }
        if (forceCarry && !hasCarry && setup.Rig.Layer(rigs.Database, CarrySystem.CarriedClip, setup.Animal) is { } carried)
        { layers.Clear(); layers.Add(new(carried, 0, 1)); }
        foreach (var (definition, time) in setup.Fixed) layers.Add(new(definition, time, 1));
        return layers.ToArray();
    }
    static (RigidTransform[] Targets, RigidTransform[] Bones) AnimatedCarry(Owner owner, AdvancedFragmentScene.Piece piece, ActiveLayer[] layers, Matrix4x4 placement)
    {
        var rig = owner.Setup.Rig; var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(owner.Setup.Shape, layers);
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            var bones = Enumerable.Range(0, rig.BoneCount).Select(h => new RigidTransform(Vector3.Transform(scratch.DerivedPosition[h], placement),
                Quaternion.Normalize(turn * scratch.DerivedRotation[h]))).ToArray();
            return (piece.Definition.Bodies.Select(body => body.BoneInBody.Inverse().Then(bones[body.Bone])).ToArray(), bones);
        }
        finally { rig.Return(scratch); }
    }
    void AlignGrip(Owner owner, CarryState carry, RigidTransform[] target, ActiveLayer[] layers, Vector3 support, bool entering)
    {
        var piece = carry.Piece; var rig = owner.Setup.Rig;
        var poses = target.Select((at, b) => carry.Connected[b] && (entering || carry.Pinned[b]) ? at : piece.Pose[b]).ToArray();
        var bones = AdvancedFragmentScene.CarryPose(piece, rig, poses);
        var skin = CarryContactSkin(owner.Cold, owner.Appearance, owner.Setup, layers, bones);
        if (!carry.GripSelected)
        {
            carry.GripSelected = true;
            var cut = carry.Cut;
            carry.Surface = owner.Skin.FindGrip(owner.Cold.Appearance ?? owner.Appearance, rig, cut?.Plane ?? default, carry.Side, cut?.FullBody ?? true,
                carry.Components, 0, piece.Definition, skin, support,
                query: new(Uncut: cut is null, PinnedBones: carry.PinnedBones, AreaCentre: true), limbSeverances: carry.Limbs);
            if (carry.Surface is null)
            {
                float mass = 0; var middle = Vector3.Zero;
                for (int b = 0; b < target.Length; b++) if (carry.Pinned[b]) { middle += target[b].Position * piece.Definition.Bodies[b].Mass; mass += piece.Definition.Bodies[b].Mass; }
                (carry.Body, carry.Local) = ColliderContact(piece.Definition, target, carry.Pinned, mass > 0 ? middle / mass : target[piece.Definition.Root].Position);
            }
        }
        var contact = carry.Surface is { } material ? material.Posed(skin) : target[carry.Body].Apply(carry.Local);
        var by = (support - contact) / MathF.Max(.5f, carry.Surface?.RootShare ?? 1);
        if (!float.IsFinite(by.LengthSquared())) return;
        for (int b = 0; b < target.Length; b++)
            if (carry.Connected[b] && (entering || carry.Pinned[b])) target[b] = target[b] with { Position = target[b].Position + by };
    }

    internal Matrix4x4[] CarryContactSkin(CharacterCold cold, CharacterAppearance natural, CharacterPoseSetup original, ActiveLayer[] layers, BonePose worldBones)
    {
        var setup = ContactSetup(cold, original, natural); var rig = original.Rig; var scratch = rig.Rent();
        try
        {
            var skin = new Matrix4x4[rig.BoneCount];
            // Absolute world-space bones already include placement. Only the renderer's current bone scale is missing.
            scratch.Pose(setup.Shape, layers, skin, worldBones);
            return skin;
        }
        finally { rig.Return(scratch); }
    }
    CharacterPoseSetup ContactSetup(CharacterCold cold, CharacterPoseSetup original, CharacterAppearance? displayed = null)
    {
        if (cold is not { Stats: { } stats, Medical: { } medical, Race.IsRobot: false } || (displayed ?? cold.Appearance) is not { Animal: null } look) return original;
        var build = CharacterShape.BuildFromStats(look.Female, stats, static (state, stat) => state[stat], medical.Hunger);
        var cached = contactShapes.GetValue(cold, static _ => new());
        if (cached.Setup is null || !cached.Build.Near(build) || !ReferenceEquals(cached.Look, look) || !ReferenceEquals(cached.Original, original))
        {
            cached.Build = build; cached.Look = look; cached.Original = original;
            cached.Setup = rigs.Reshape(look, build) is { } shaped ? new() { Rig = original.Rig, Shape = shaped.Shape, GroundOffset = shaped.GroundOffset, Fixed = original.Fixed, Idle = original.Idle, Animal = original.Animal } : original;
        }
        return cached.Setup;
    }
    Vector3? UpperArmSupport(World world, CharacterId holder)
    {
        if (!world.Characters.TryResolveNext(holder, out int slot)) return null;
        ref var hot = ref world.Characters.Next[slot];
        if (world.Characters.Cold(slot) is not { Appearance: { } look } cold || rigs.For(look) is not { } setup
            || setup.Rig.Handle("Bip01 L UpperArm") is not int shoulder || setup.Rig.Handle("Bip01 L Forearm") is not int elbow)
            return CarryPose.Fallback.World(hot.Position, hot.Yaw).Position;
        setup = ContactSetup(cold, setup);
        var rig = setup.Rig; var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(setup.Shape, CarryLayers(setup, cold, forceCarry: false));
            var placement = CharacterRigs.Placement(setup, hot.Position, hot.Yaw, 1);
            var start = Vector3.Transform(scratch.DerivedPosition[shoulder], placement); var end = Vector3.Transform(scratch.DerivedPosition[elbow], placement);
            var skin = new Matrix4x4[rig.BoneCount];
            for (int h = 0; h < skin.Length; h++)
                skin[h] = Matrix4x4.CreateTranslation(-rig.BindPosition[h]) * Matrix4x4.CreateScale(scratch.DerivedScale[h] / rig.BindScale[h])
                    * Matrix4x4.CreateFromQuaternion(scratch.DerivedRotation[h] * rig.BindRotationInverse[h]) * Matrix4x4.CreateTranslation(scratch.DerivedPosition[h]) * placement;
            var surface = SkinFor(look);
            var material = surface is not null && Definition(look, rig) is { } definition ? surface.FindUpperArmSupport(look, rig, definition, skin, start, end) : null;
            return material is not null ? material.Posed(skin) : CarriedFragmentGrip.UpperArmProbe(start, end);
        }
        finally { rig.Return(scratch); }
    }
    RagdollSkinSurface? SkinFor(CharacterAppearance look)
    {
        if (rigs.FindMesh(look.BodyMesh) is not { } path) return null;
        if (skins.TryGetValue(path, out var surface)) return surface;
        try { surface = RagdollSkinSurface.Read(OgreMeshReader.ReadFile(path)); skins[path] = surface; return surface; }
        catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException) { return null; }
    }
    static (int Body, Vector3 Local) ColliderContact(RagdollDefinition definition, RigidTransform[] target, bool[] pinned, Vector3 point)
    {
        int selected = definition.Root; Vector3 local = default; float nearest = float.MaxValue;
        for (int b = 0; b < target.Length; b++)
        {
            if (!pinned[b]) continue;
            var body = definition.Bodies[b]; var wanted = target[b].Inverse().Apply(point);
            if (body.Type == PhysicsShapeType.Mesh)
            {
                var points = RagdollCut.Points(body);
                if (!RagdollHull.TryTriangles(points, out var triangles)) continue;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = points[triangles[i]]; var c = points[triangles[i + 1]]; var d = points[triangles[i + 2]];
                    var bary = RagdollSkinSurface.ClosestBarycentric(wanted, a, c, d); Candidate(a * bary.X + c * bary.Y + d * bary.Z);
                }
            }
            else if (body.Type == PhysicsShapeType.Box)
            {
                var half = body.Size * .5f; var clamped = Vector3.Clamp(wanted, -half, half);
                if (clamped != wanted) Candidate(clamped);
                else for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2) { var face = clamped; face[axis] = half[axis] * sign; Candidate(face); }
            }
            else
            {
                float length = body.Type == PhysicsShapeType.Capsule ? body.Size.Y * .5f : 0;
                var axis = new Vector3(0, Math.Clamp(wanted.Y, -length, length), 0); var normal = wanted - axis;
                if (normal.LengthSquared() < 1e-8f) normal = Vector3.UnitX;
                Candidate(axis + Vector3.Normalize(normal) * body.Size.X);
            }
            void Candidate(Vector3 candidate)
            {
                float distance = Vector3.DistanceSquared(target[b].Apply(candidate), point);
                if (distance >= nearest) return;
                nearest = distance; selected = b; local = candidate;
            }
        }
        return (selected, local);
    }
    static void HashCarry(Owner owner, ref StateHasher hash)
    {
        hash.Add(owner.Carries.Count);
        foreach (var (key, carry) in owner.Carries.OrderBy(pair => pair.Key))
        {
            hash.Add(key); hash.Add(carry.Side); hash.Add(carry.Holder); hash.Add(carry.GripSelected); hash.Add(carry.Body); hash.Add(carry.Local); hash.Add(carry.Surface is not null);
            if (carry.Surface is not { } surface) continue;
            hash.Add(surface.Barycentric); hash.Add(surface.RootShare);
            foreach (var point in new[] { surface.Triangle.A, surface.Triangle.B, surface.Triangle.C })
            { hash.Add(point.Position); hash.Add((int)point.Bones); hash.Add(point.Weights.X); hash.Add(point.Weights.Y); hash.Add(point.Weights.Z); hash.Add(point.Weights.W); }
        }
    }
}
