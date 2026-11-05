using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Numerics;
using HarmonyLib;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;
using NativeRagdolls = Meitou.Simulation.Ragdolls.RagdollSystem;

namespace Meitou.Brutality.Physics;

public sealed record FragmentPoseSnapshot(int StableKey, BonePose Bones, Vector3 Position)
{
    internal RagdollDefinition? Definition { get; init; }
}
public sealed record GoreCharacterFrame(CharacterId Character, CharacterAppearance Appearance, BrutalityActorView State,
    BonePose? Retained, IReadOnlyList<FragmentPoseSnapshot> Detached)
{
    public Matrix4x4 Placement { get; init; } = Matrix4x4.Identity;
    internal RagdollDefinition? RetainedDefinition { get; init; }
    public FragmentBirthPresentation? BirthPresentation { get; init; }
}
public sealed record GorePresentationFrame(long Tick, IReadOnlyDictionary<CharacterId, GoreCharacterFrame> Characters);

/// <summary>Publishes independently articulated pieces after the host's animation/ragdoll step.</summary>
public sealed partial class FragmentPhysicsSystem(BrutalityGameplaySystem gameplay, CharacterRigs rigs, IRagdollEnvironment? environment = null)
    : ICombatHitObserver, IStateHashed, IDisposable
{
    sealed class Owner
    {
        public required CharacterPoseSetup Setup;
        public required CharacterAppearance Appearance;
        public required RagdollSkinSurface Skin;
        public required RagdollDefinition Source;
        public required CharacterCold Cold;
        public required FragmentAppearanceCache Presentation;
        public readonly Dictionary<int, (string Stamp, AdvancedFragmentScene.Piece Piece)> Pieces = [];
        public string StateStamp = "";
        public readonly FragmentTopologyCache Topology = new();
        public Matrix4x4 Placement;
        public readonly Dictionary<int, CarryState> Carries = [];
        public bool NegativePoseRestored;
        public bool NativeClaimed;
        public (long Tick, BonePose Pose, Matrix4x4 Placement)? ReturnFrame;
        public bool FootDiagnosed;
        public FragmentBirthPresentation? BirthPresentation;
    }
    static readonly bool TracePhases = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PHASES") == "1";
    readonly AdvancedFragmentScene scene = new();
    readonly Queue<CombatHit> impacts = new();
    readonly HashSet<CharacterId> reactionOwners = [];
    public void HitResolved(World world, in CombatHit hit)
    {
        if (hit.VictimWasAlive && hit.VictimWasIncapacitated) impacts.Enqueue(hit);
    }
    internal bool OwnsLimbDrop(CharacterId id, CharacterCold cold, LimbCutState cut) =>
        owners.TryGetValue(id, out var owner) && ReferenceEquals(cold, owner.Cold)
        && owner.Pieces.TryGetValue(10 + cut.LimbSlot, out var detached) && detached.Stamp == cut.ToString()
        && detached.Piece.Bodies.Any(body => body.HasValue);

    public bool TryPartPosition(CharacterId id, BodyPartTemplate part, out Vector3 position)
    {
        position = default;
        if (!owners.TryGetValue(id, out var owner) || !owner.Pieces.TryGetValue(0, out var retained)) return false;
        int bone = owner.Setup.Rig.Handle(part.BoneName) ?? owner.Setup.Rig.Handle(part.BoneName2) ?? -1;
        int index = bone >= 0 && bone < retained.Piece.Definition.BodyOfBone.Length ? retained.Piece.Definition.BodyOfBone[bone] : -1;
        if (index < 0) return false;
        var at = retained.Piece.Definition.Bodies[index].BoneInBody.Then(retained.Piece.Pose[index]);
        position = at.Position; return true;
    }
    readonly Dictionary<CharacterId, CharacterAppearance> uncut = [];
    readonly Dictionary<CharacterId, Owner> owners = [];
    readonly FragmentAssets assets = FragmentAssets.For(rigs);
    readonly Queue<GorePresentationFrame> history = [];
    volatile GorePresentationFrame[] frames = [];
    IRagdollEnvironment? ground = environment;
    IAnimationSource? animations;
    NativeRagdolls? native;
    bool disposed;
    public GorePresentationFrame? Latest => frames.LastOrDefault();
    public GorePresentationFrame? FrameAt(long tick)
    {
        var available = frames;
        for (int i = available.Length - 1; i >= 0; i--) if (available[i].Tick <= tick) return available[i];
        return null;
    }

    public void Schedule(World world)
    {
        BindSaveWorld(world);
        var cuts = gameplay.ActorViews.Where(v => v.Torso is not null || v.Head is not null || v.Limbs.Count > 0).Select(v => v.Character).ToHashSet();
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
        {
            if (!world.Characters.Next[slot].Alive || world.Characters.Cold(slot)?.Appearance is not { } look) continue;
            var id = world.Characters.IdOf(slot);
            if (!cuts.Contains(id) && !owners.ContainsKey(id)) uncut[id] = look;
        }
    }

    public void SlowWorld(World world)
    {
        if (disposed) return;
        ground ??= new WalkabilityEnvironment(world.Walkability);
        animations ??= world.Systems.OfType<IAnimationSource>().FirstOrDefault();
        native ??= world.Systems.OfType<NativeRagdolls>().FirstOrDefault();
        foreach (var hit in impacts) reactionOwners.Add(hit.Victim);
        int startsLeft = 4, changed = 0;
        long setupTime = 0, definitionsTime = 0, poseTime = 0, createTime = 0;
        foreach (var state in gameplay.ActorViews)
        {
            if (state.Torso is null && state.Head is null && state.Limbs.Count == 0 && state.SevereHead.Kind == SevereHeadWoundKind.None
                && !reactionOwners.Contains(state.Character) && !HasSavedOwner(world, state.Character)) continue;
            if (!world.Characters.TryResolveNext(state.Character, out int slot) || world.Characters.Cold(slot) is not { Appearance: { } current } cold) continue;
            long setupStarted = TracePhases ? Stopwatch.GetTimestamp() : 0;
            if (!owners.TryGetValue(state.Character, out var owner))
            {
                if (startsLeft-- <= 0) continue;
                var original = uncut.GetValueOrDefault(state.Character, current);
                var presentation = new FragmentAppearanceCache();
                var natural = presentation.Get(rigs.Database, current, PresentedParts(current, original, cold), state);
                if (rigs.For(natural) is not { } setup || rigs.FindMesh(natural.BodyMesh) is not { } bodyPath) continue;
                var surface = assets.Skin(bodyPath);
                if (Definition(natural, setup.Rig) is not { } source) continue;
                owner = new Owner { Setup = setup, Appearance = natural, Skin = surface, Source = source, Cold = cold, Presentation = presentation };
                owners.Add(state.Character, owner);
            }
            var presented = owner.Presentation.Get(rigs.Database, current,
                PresentedParts(current, uncut.GetValueOrDefault(state.Character, current), cold), state);
            if (!ReferenceEquals(owner.Appearance, presented))
            {
                if (rigs.For(presented) is not { } setup || Definition(presented, setup.Rig) is not { } source) continue;
                // Definitions are cached by immutable rig, source file and robot weighting. Shape changes are applied
                // by the refreshed setup when posing; no shaped hull is cached under an appearance-independent key.
                owner.Appearance = presented; owner.Setup = setup; owner.Source = source;
            }
            ref var hot = ref world.Characters.Next[slot];
            bool ownBody = state.Torso is not null || cold.Medical is { Dead: true } or { Incapacitated: true };
            var topology = owner.Topology.Get(state);
            string stamp = ownBody ? topology.WithBody : topology.WithoutBody;
            var placement = PlacementFor(owner, cold, hot);
            if (owner.StateStamp != stamp)
            {
                long definitionsStarted = TracePhases ? Stopwatch.GetTimestamp() : 0;
                var specs = Definitions(owner, state, ownBody);
                long poseStarted = TracePhases ? Stopwatch.GetTimestamp() : 0;
                if (TracePhases)
                {
                    changed++;
                    setupTime += definitionsStarted - setupStarted;
                    definitionsTime += poseStarted - definitionsStarted;
                }
                Meitou.Simulation.Ragdolls.RagdollHandoff? handoff = null;
                if (native is not null && !owner.NativeClaimed)
                {
                    if (ownBody)
                    {
                        if (!native.TryClaim(world, state.Character, this, out handoff)) continue;
                        owner.NativeClaimed = true;
                    }
                    else native.TryCapture(world, state.Character, out handoff);
                }
                var posed = BoneWorld(world, state.Character, cold, owner, placement, handoff);
                owner.BirthPresentation = CaptureBirthPresentation(owner.Appearance, posed, placement, restoring: HasSavedOwner(world, state.Character));
                var previousMotion = owner.Pieces.TryGetValue(0, out var priorBody) ? scene.CaptureMotion(priorBody.Piece)
                    : RobotRagdollWeight.CaptureMotion(cold.Race?.IsRobot == true, handoff);
                var pending = handoff?.PendingLaunch;
                var launch = pending is { } initial ? (initial.Velocity, initial.Push) : cold.Blow ?? (hot.Velocity, Vector3.Zero);
                var start = RobotRagdollWeight.Launch(cold.Race?.IsRobot == true, launch);
                var appliedPush = handoff?.AppliedLaunch?.Push ?? start.Push;
                var returnedPose = !ownBody && owner.NativeClaimed && owner.Pieces.TryGetValue(0, out var returning)
                    ? scene.Publish(returning.Piece, owner.Setup.Rig, placement) : null;
                long createStarted = TracePhases ? Stopwatch.GetTimestamp() : 0;
                if (TracePhases) poseTime += createStarted - poseStarted;
                var keep = specs.Select(s => s.Key).ToHashSet();
                foreach (int old in owner.Pieces.Keys.Where(k => !keep.Contains(k)).ToArray()) { scene.Remove(owner.Pieces[old].Piece); owner.Pieces.Remove(old); }
                foreach (var spec in specs)
                {
                    if (owner.Pieces.TryGetValue(spec.Key, out var before) && before.Stamp == spec.Stamp) continue;
                    if (before.Piece is not null) scene.Remove(before.Piece);
                    var piece = scene.Create(spec.Definition, bone => posed[bone], start.Velocity, start.Push,
                        Rng.Mix(world.Seed ^ Rng.Key(state.Character) ^ (ulong)world.Tick ^ (ulong)spec.Key), posed);
                    Vector3 separation = default;
                    if (state.Torso is { } torso && spec.Key is 0 or 1 && appliedPush.LengthSquared() > 1e-8f)
                    {
                        var normal = Vector3.TransformNormal(new(torso.Plane.X, torso.Plane.Y, torso.Plane.Z), placement);
                        if (normal.LengthSquared() > 1e-8f) separation = Vector3.Normalize(normal) * (spec.Key == 0 ? 2 : -2) * RobotRagdollWeight.Response(cold.Race?.IsRobot == true);
                    }
                    scene.InheritMotion(piece, previousMotion, separation);
                    owner.Pieces[spec.Key] = (spec.Stamp, piece);
                }
                if (!ownBody && owner.NativeClaimed)
                {
                    if (returnedPose is not null && native?.ReturnClaim(world, state.Character, this, returnedPose, placement) == true)
                        owner.ReturnFrame = (world.Tick, CopyPose(returnedPose), placement);
                    else native?.ReleaseClaim(state.Character, this);
                    owner.NativeClaimed = false;
                }
                owner.StateStamp = stamp;
                if (TracePhases) createTime += Stopwatch.GetTimestamp() - createStarted;
            }
            owner.Placement = placement;
            RestoreSavedOwner(world, state.Character, owner, state, hot.Position);
        }
        foreach (var id in owners.Keys.Where(id => !world.Characters.IsAlive(id)).ToArray()) Remove(id);
        while (impacts.TryDequeue(out var hit))
        {
            if (!owners.TryGetValue(hit.Victim, out var owner) || !owner.Pieces.TryGetValue(0, out var retained)) continue;
            if ((uint)hit.Part >= (uint)(owner.Cold.Medical?.Parts.Count ?? 0)) continue;
            var part = owner.Cold.Medical!.Parts[hit.Part].Template;
            if (!scene.TryImpactSite(retained.Piece, owner.Setup.Rig, part, out int actor, out var offset)) continue;
            var direction = new Vector2(hit.Travel.X, hit.Travel.Z);
            if (world.Characters.TryResolveNext(hit.Attacker, out int attacker))
            {
                var at = retained.Piece.Pose[actor].Apply(offset); var from = world.Characters.Previous[attacker].Position;
                direction = new(at.X - from.X, at.Z - from.Z);
            }
            var push = HitPush(direction, hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce)
                * RobotRagdollWeight.Response(owner.Cold.Race?.IsRobot == true);
            scene.Impact(retained.Piece, actor, offset, push);
        }
        UpdateCarries(world);
        long stepStarted = changed > 0 ? Stopwatch.GetTimestamp() : 0;
        scene.Step(RagdollPhysics.StepOf(world.TickSeconds, world.PhysicsSpeed), ground, world.Tick, world.TickSeconds, world.Settings.Threads);
        long publishStarted = changed > 0 ? Stopwatch.GetTimestamp() : 0;
        var publish = new Dictionary<CharacterId, GoreCharacterFrame>();
        foreach (var state in gameplay.ActorViews)
        {
            if (!owners.TryGetValue(state.Character, out var owner) || !world.Characters.TryResolveNext(state.Character, out int slot)) continue;
            ref var hot = ref world.Characters.Next[slot];
            if (owner.Pieces.TryGetValue(0, out var remaining))
            {
                var root = remaining.Piece.Pose[remaining.Piece.Definition.Root].Position;
                if (owner.Cold.CarriedBy.IsNone) { hot.Position = root; hot.Velocity = Vector3.Zero; }
            }
            // Animation already published before this system. Keep the final external pose for this return frame;
            // on the next tick its native replacement has begun the get-up blend.
            var recovery = owner.ReturnFrame is { } returned && returned.Tick == world.Tick ? returned : ((long Tick, BonePose Pose, Matrix4x4 Placement)?)null;
            if (recovery is null) owner.ReturnFrame = null;
            owner.Placement = recovery?.Placement ?? PlacementFor(owner, owner.Cold, hot);
            TrackSavedPosition(owner, hot.Position);
            var detached = new List<FragmentPoseSnapshot>(); BonePose? body = recovery?.Pose;
            RagdollDefinition? retainedDefinition = null;
            foreach (var (key, entry) in owner.Pieces)
            {
                var pose = scene.Publish(entry.Piece, owner.Setup.Rig, owner.Placement);
                if (key == 0) { body = pose; retainedDefinition = entry.Piece.Definition; }
                else detached.Add(new(key, pose, entry.Piece.Pose[entry.Piece.Definition.Root].Position) { Definition = entry.Piece.Definition });
            }
            if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") is not null && !owner.FootDiagnosed && world.Tick >= 180
                && owner.Pieces.TryGetValue(1, out var lower))
            {
                owner.FootDiagnosed = true;
                var worldBones = scene.Publish(lower.Piece, owner.Setup.Rig, Matrix4x4.Identity);
                var contactSkin = CarryContactSkin(owner.Cold, owner.Appearance, owner.Setup, [], worldBones);
                if (owner.Skin.FootBounds(owner.Setup.Rig, contactSkin) is { } feet)
                    Console.WriteLine($"Brutality physical foot skin: tick={world.Tick} owner={state.Character} min={feet.Min} max={feet.Max} ground={ground.GroundHeight(feet.Min.X, feet.Min.Z, feet.Min.Y)} frozen={lower.Piece.Frozen}");
            }
            publish.Add(state.Character, new(state.Character, owner.Appearance, state, body, Array.AsReadOnly(detached.ToArray()))
            { Placement = owner.Placement, BirthPresentation = owner.BirthPresentation, RetainedDefinition = retainedDefinition });
        }
        history.Enqueue(new(world.Tick + 1, new ReadOnlyDictionary<CharacterId, GoreCharacterFrame>(publish)));
        while (history.Count > 64) history.Dequeue();
        frames = history.ToArray();
        if (changed > 0)
        {
            double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
            Console.WriteLine($"Brutality fragment phases: tick={world.Tick} changed={changed} setup={Ms(setupTime):F2} definitions={Ms(definitionsTime):F2} pose={Ms(poseTime):F2} create={Ms(createTime):F2} step={Ms(publishStarted - stepStarted):F2} publish={Ms(Stopwatch.GetTimestamp() - publishStarted):F2} ms");
        }
    }

    static BonePose CopyPose(BonePose source)
    {
        var result = new BonePose(source.Set.Length) { Weight = source.Weight, AbsolutePositions = source.AbsolutePositions, Components = source.Components?.ToArray() };
        source.Set.CopyTo(result.Set, 0); source.Position.CopyTo(result.Position, 0); source.Rotation.CopyTo(result.Rotation, 0);
        return result;
    }

    static IEnumerable<CharacterPart> PresentedParts(CharacterAppearance current, CharacterAppearance original, CharacterCold cold)
    {
        var parts = current.Parts.ToList();
        foreach (var part in original.Parts)
        {
            bool intrinsic = part.Slot is AttachSlot.Hair or AttachSlot.Beard;
            bool stillWorn = part.Slot is AttachSlot.Hat or AttachSlot.Eyes
                && cold.Inventory?.Items.Any(item => item.Record == part.Record.StringId) == true;
            if (!intrinsic && !stillWorn || parts.Any(p => p.Record.StringId == part.Record.StringId && p.Field == part.Field && p.Point == part.Point)) continue;
            parts.Add(part);
        }
        return parts;
    }

    sealed record Spec(int Key, string Stamp, RagdollDefinition Definition);
    internal static string Stamp(BrutalityActorView s) => s.Torso + "/" + s.Head + "/" + string.Join(';', s.Limbs);
    static List<Spec> Definitions(Owner owner, BrutalityActorView state, bool ownBody)
    {
        var result = new List<Spec>(); var remaining = owner.Source;
        foreach (var limb in state.Limbs)
        {
            if (!LimbCutGeometry.TryPlane(owner.Setup.Rig, limb, out var plane)) continue;
            var split = RagdollSeverance.Split(remaining, limb.LimbSlot, plane);
            if (split.Detached is { } piece) result.Add(new(10 + limb.LimbSlot, limb.ToString(), piece));
            if (split.Remaining is { } retained) remaining = retained;
        }
        if (state.Head is { } head)
        {
            owner.Skin.HeadPlane(owner.Setup.Rig, head.Plane, out var fittedHeadPlane, owner.Appearance.Race.GetBool("is robot"));
            var split = RagdollSeverance.SplitHead(remaining, fittedHeadPlane, head.Mode);
            if (split.Positive is { } positive) result.Add(new(2, head.ToString(), positive));
            if (split.Negative is { } negative) result.Add(new(3, head.ToString(), negative));
            if (split.Remaining is { } retained) remaining = retained;
        }
        if (state.Torso is { } cut)
        {
            var positive = RagdollCut.Build(remaining, cut.Plane, 1, cut.FullBody, owner.Skin.Bridges(owner.Setup.Rig, remaining, cut.Plane, 1));
            var negative = RagdollCut.Build(remaining, cut.Plane, -1, cut.FullBody, owner.Skin.Bridges(owner.Setup.Rig, remaining, cut.Plane, -1));
            if (negative is not null) result.Add(new(1, cut.ToString(), negative));
            if (positive is not null) remaining = positive;
        }
        if (ownBody) result.Add(new(0, owner.Topology.Get(state).Retained, remaining));
        return result;
    }

    RigidTransform[] BoneWorld(World world, CharacterId id, CharacterCold cold, Owner owner, Matrix4x4 placement, Meitou.Simulation.Ragdolls.RagdollHandoff? handoff = null)
    {
        var rig = owner.Setup.Rig;
        var layers = new List<ActiveLayer>();
        if (cold.Animation is { } animation && animations is not null)
            foreach (var layer in animations.Publish(animation, []))
                if (rig.Layer(rigs.Database, layer.Name, owner.Setup.Animal) is { } def) layers.Add(new(def, MathF.Max(layer.Time, 0), layer.Weight));
        foreach (var (def, time) in owner.Setup.Fixed) layers.Add(new(def, time, 1));
        // A second cut starts from every currently physical AND nonphysical bone. Replacing only the actors
        // would leave fingers, facial bones and hair at their animation positions while their parent lies elsewhere.
        var pose = owner.Pieces.TryGetValue(0, out var existing) ? scene.Publish(existing.Piece, rig, placement) : native?.Publish(id);
        if (pose is null && handoff?.Bones is { } nativeBones)
        {
            var nativeWorld = PoseWorld(owner.Setup, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(layers), nativeBones, handoff.Placement);
            pose = new(rig.BoneCount) { AbsolutePositions = true };
            Matrix4x4.Invert(placement, out var inverse);
            var unturn = Quaternion.Inverse(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 })));
            for (int bone = 0; bone < nativeWorld.Length; bone++)
            {
                pose.Set[bone] = true; pose.Position[bone] = Vector3.Transform(nativeWorld[bone].Position, inverse);
                pose.Rotation[bone] = Quaternion.Normalize(unturn * nativeWorld[bone].Rotation);
            }
        }
        if (pose is null && handoff is { Bodies.Count: > 0 })
        {
            pose = new(rig.BoneCount) { AbsolutePositions = true };
            Matrix4x4.Invert(placement, out var inverse);
            var unturn = Quaternion.Inverse(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 })));
            foreach (var body in handoff.Bodies)
            {
                if ((uint)body.Bone >= (uint)rig.BoneCount) continue;
                pose.Set[body.Bone] = true; pose.Position[body.Bone] = Vector3.Transform(body.BoneWorld.Position, inverse);
                pose.Rotation[body.Bone] = Quaternion.Normalize(unturn * body.BoneWorld.Rotation);
            }
        }
        return PoseWorld(owner.Setup, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(layers), pose, placement);
    }

    internal static RigidTransform[] PoseWorld(CharacterPoseSetup setup, ReadOnlySpan<ActiveLayer> layers, BonePose? pose, Matrix4x4 placement)
    {
        var rig = setup.Rig; var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(setup.Shape, layers, pose);
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            var result = new RigidTransform[rig.BoneCount];
            for (int h = 0; h < result.Length; h++) result[h] = new(Vector3.Transform(scratch.DerivedPosition[h], placement), Quaternion.Normalize(turn * scratch.DerivedRotation[h]));
            return result;
        }
        finally { rig.Return(scratch); }
    }

    RagdollDefinition? Definition(CharacterAppearance look, SkeletonRig rig) => assets.Definition(look, rig);

    internal static Vector3 HitPush(Vector2 direction, float damage)
    {
        if (!float.IsFinite(damage) || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 1e-6f) return Vector3.Zero;
        direction = Vector2.Normalize(direction);
        return new Vector3(direction.X, .2f, direction.Y) * Math.Clamp(damage * .4f, 10, 50);
    }
    void Remove(CharacterId id)
    {
        if (!owners.Remove(id, out var owner)) return;
        RememberRemovedOwner(id, owner);
        foreach (var item in owner.Pieces.Values) scene.Remove(item.Piece);
        if (owner.NativeClaimed) native?.ReleaseClaim(id, this);
        uncut.Remove(id); reactionOwners.Remove(id);
    }
    public void Hash(ref StateHasher hash)
    {
        HashSaved(ref hash);
        hash.Add(owners.Count);
        foreach (var (id, owner) in owners.OrderBy(p => Rng.Key(p.Key)))
        {
            hash.Add(id); hash.Add(Rng.StableHash(owner.StateStamp)); hash.Add(owner.Pieces.Count); hash.Add(owner.NegativePoseRestored); HashCarry(owner, ref hash);
            foreach (var (key, entry) in owner.Pieces.OrderBy(p => p.Key))
            { hash.Add(key); hash.Add(Rng.StableHash(entry.Stamp)); scene.Hash(entry.Piece, ref hash); }
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var (id, owner) in owners) if (owner.NativeClaimed) native?.ReleaseClaim(id, this);
        scene.Dispose(); ClearSaved(); owners.Clear(); uncut.Clear(); history.Clear(); frames = [];
    }
}

/// <summary>The native look can stay unchanged while a second explicit cut takes over another collapsed limb.</summary>
internal sealed class FragmentAppearanceCache
{
    CharacterAppearance? native, presented;
    int restored;
    public CharacterAppearance Get(GameDatabase data, CharacterAppearance current, IEnumerable<CharacterPart> parts, BrutalityActorView state)
    {
        int control = 0;
        foreach (var cut in state.Limbs) if (cut.IsValid) control |= 1 << cut.LimbSlot;
        if (state.Head is { IsValid: true }) control |= 1 << 4;
        if (!current.Headless && state.SevereHead.Kind == SevereHeadWoundKind.JawLost) control |= 1 << 5;
        if (ReferenceEquals(native, current) && restored == control && presented is not null) return presented;
        native = current; restored = control;
        return presented = NaturalAppearance.Make(data, current, parts, state);
    }
}

internal sealed record FragmentTopologyKeys(string Cut, string Retained, string WithBody, string WithoutBody);

/// <summary>Actor views may change for XP or ordinary wounds while immutable cut topology remains identical.</summary>
internal sealed class FragmentTopologyCache
{
    HeadCutState? head;
    TorsoBisectionState? torso;
    IReadOnlyList<LimbCutState>? limbs;
    FragmentTopologyKeys? keys;
    public FragmentTopologyKeys Get(BrutalityActorView state)
    {
        if (keys is not null && head == state.Head && torso == state.Torso && ReferenceEquals(limbs, state.Limbs)) return keys;
        head = state.Head; torso = state.Torso; limbs = state.Limbs;
        string retained = FragmentPhysicsSystem.Stamp(state);
        return keys = new(FragmentPhysicsSystem.CutKey(state), retained, retained + true, retained + false);
    }
}
