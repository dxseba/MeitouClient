using System.Numerics;
using BepuPhysics;
using Meitou.Data.Characters;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Ragdolls;

/// <summary>Both cut sides, independently articulated in the logical owner's drawn model space. Immutable once published.</summary>
public sealed record TorsoRagdollPose(BonePose Positive, BonePose Negative);

public sealed partial class RagdollSystem
{
    sealed class TorsoFragments
    {
        public required TorsoBisectionState Cut;
        public required RagdollDefinition Source;
        public required Ragdoll Positive, Negative;
        public required int[] PositiveAnchors, NegativeAnchors;
        public required RigidTransform[] PositiveOffsets, NegativeOffsets;
        public TorsoRagdollPose? Published;
        public Matrix4x4 PublishedIn;
        public bool NegativeCarried, PositiveCarried;
        public bool[]? NegativeCarriedBodies, PositiveCarriedBodies;
        public TorsoGrip? NegativeGrip, PositiveGrip;
        public RagdollSkinSurface.Grip? NegativeSupport, PositiveSupport;
        public CharacterId NegativeGripHolder = CharacterId.None, PositiveGripHolder = CharacterId.None;
    }

    readonly Dictionary<CharacterId, TorsoFragments> torsoRagdolls = [];
    readonly List<CharacterId> torsoOrder = [];

    public TorsoRagdollPose? PublishTorso(CharacterId id) => torsoRagdolls.TryGetValue(id, out var fragments) ? fragments.Published : null;

    int MovingTorsoRagdolls => torsoOrder.Sum(id => (torsoRagdolls[id].Positive.Frozen ? 0 : 1) + (torsoRagdolls[id].Negative.Frozen ? 0 : 1));

    void PruneTorsoRagdolls(World world)
    {
        foreach (var id in torsoOrder.ToArray())
            if (!world.Characters.TryResolveNext(id, out int slot) || options is not { AdvancedRagdolls: true }
                || world.Characters.Cold(slot)?.TorsoBisection is not { IsValid: true }) RemoveTorsoRagdoll(id);
    }

    bool PrepareTorsoRagdoll(World world, CharacterId id, CharacterCold cold, ref CharacterHot hot, TorsoBisectionState cut,
        (Vector3 Velocity, Vector3 Push) start, IRagdollEnvironment env)
    {
        if (torsoRagdolls.TryGetValue(id, out var existing))
        {
            if (existing.Cut == cut)
            {
                UpdateTorsoCarry(world, existing, cold, hot);
                CoverTorso(existing, env, world.Tick);
                return true;
            }
            RemoveTorsoRagdoll(id);
        }
        if (cold.Appearance is not { } look || SetupOf(look) is not { } setup || DefinitionOf(look, setup.Rig, 1) is not { } originalDefinition) return false;
        var source = RemainingDefinition(id, originalDefinition);
        var positiveBridge = cut.FullBody ? SkinBridgeFor(look, setup.Rig, source, cut.Plane, 1) : null;
        var negativeBridge = cut.FullBody ? SkinBridgeFor(look, setup.Rig, source, cut.Plane, -1) : null;
        if (RagdollCut.Build(source, cut.Plane, 1, cut.FullBody, positiveBridge) is not { } positiveDefinition
            || RagdollCut.Build(source, cut.Plane, -1, cut.FullBody, negativeBridge) is not { } negativeDefinition) return false;

        ragdolls.TryGetValue(id, out var before);
        var prototype = new Ragdoll { Id = id, Definition = source, Setup = setup, Scale = 1 };
        var placement = Placement(prototype, cold, hot);
        var original = Animated(prototype, cold, placement);
        var previousVelocity = new BodyVelocity?[source.Bodies.Length];
        if (before is not null)
        {
            for (int b = 0; b < source.Bodies.Length; b++)
            {
                int old = before.Definition.BodyOfBone[source.Bodies[b].Bone];
                if (old < 0 || old >= before.Pose.Length) continue;
                var bone = before.Definition.Bodies[old].BoneInBody.Then(before.Pose[old]);
                original[b] = source.Bodies[b].BoneInBody.Inverse().Then(bone);
                if (old < before.Bodies.Length && before.Bodies[old] is { } handle && before.Dynamic[old])
                    previousVelocity[b] = physics.Simulation.Bodies[handle].Velocity;
            }
            Destroy(before);
            order.Remove(id);
        }
        var saved = cold.Save?.Ragdoll;
        if (cold.Save is { } save) save.Ragdoll = null;
        bool restore = saved?.Torso is { } stored && stored.Cut == cut
            && stored.Positive.Pose.Length == positiveDefinition.Bodies.Length && stored.Negative.Pose.Length == negativeDefinition.Bodies.Length
            && SavedFragmentRootNear(stored.Negative, negativeDefinition.Root, hot.Position) && FiniteFragment(stored.Positive);
        if (!restore && saved is { Torso: null } legacy && legacy.Pose.Length == source.Bodies.Length && SavedNear(legacy, hot.Position))
            original = legacy.Pose;

        RigidTransform[] FromOriginal(RagdollDefinition definition) => [.. definition.Bodies.Select(body =>
        {
            int old = source.BodyOfBone[body.Bone];
            return body.BoneInBody.Inverse().Then(source.Bodies[old].BoneInBody.Then(original[old]));
        })];
        var positive = new Ragdoll { Id = id, Definition = positiveDefinition, Setup = setup, Scale = 1, Pose = new RigidTransform[positiveDefinition.Bodies.Length] };
        var negative = new Ragdoll { Id = id, Definition = negativeDefinition, Setup = setup, Scale = 1, Pose = new RigidTransform[negativeDefinition.Bodies.Length] };
        var normal = Vector3.TransformNormal(new(cut.Plane.X, cut.Plane.Y, cut.Plane.Z), placement);
        normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitX;
        // A small separation speed exposes the cut; each loose joint chain then falls and collides by itself.
        float separation = start.Push.LengthSquared() > 1e-8f ? 2 : 0;
        var positiveReference = FromOriginal(positiveDefinition);
        var negativeReference = FromOriginal(negativeDefinition);
        var positiveStart = restore ? saved!.Torso!.Positive.Pose : positiveReference;
        var negativeStart = restore ? saved!.Torso!.Negative.Pose : negativeReference;
        Rebuild(positive, RagdollPart.Whole, positiveStart,
            restore ? default : (start.Velocity + normal * separation, start.Push), true, world.Seed);
        Rebuild(negative, RagdollPart.Whole, negativeStart,
            restore ? default : (start.Velocity - normal * separation, start.Push), true, world.Seed);
        if (!restore)
        {
            Inherit(positive, normal * separation);
            Inherit(negative, -normal * separation);
        }
        else
        {
            Restore(positive, saved!.Torso!.Positive);
            Restore(negative, saved.Torso.Negative);
        }
        if (!restore && cold.TorsoPositiveCarriedBy.IsNone && cold.TorsoPositivePosition is { } retained)
            Shift(positive, retained - positive.Pose[positive.Definition.Root].Position);
        if (!positive.Frozen) SetTone(positive, Tone.Dead);
        if (!negative.Frozen) SetTone(negative, Tone.Dead);
        var boneWorld = InitialBoneWorld(prototype, cold, placement, source, original);
        var (positiveAnchors, positiveOffsets) = FragmentAnchors(positive, boneWorld, positiveReference);
        var (negativeAnchors, negativeOffsets) = FragmentAnchors(negative, boneWorld, negativeReference);
        if (restore)
        {
            RestoreFragmentOffsets(saved!.Torso!.PositiveAnchors, positive, positiveAnchors, positiveOffsets);
            RestoreFragmentOffsets(saved.Torso.NegativeAnchors, negative, negativeAnchors, negativeOffsets);
        }
        var fragments = new TorsoFragments
        {
            Cut = cut, Source = source, Positive = positive, Negative = negative,
            PositiveAnchors = positiveAnchors, PositiveOffsets = positiveOffsets,
            NegativeAnchors = negativeAnchors, NegativeOffsets = negativeOffsets,
        };
        torsoRagdolls[id] = fragments;
        torsoOrder.Add(id);
        UpdateTorsoCarry(world, fragments, cold, hot);
        CoverTorso(fragments, env, world.Tick);
        return true;

        void Inherit(Ragdoll piece, Vector3 outward)
        {
            for (int b = 0; b < piece.Definition.Bodies.Length; b++)
            {
                int old = source.BodyOfBone[piece.Definition.Bodies[b].Bone];
                if (previousVelocity[old] is not { } velocity || piece.Bodies[b] is not { } handle) continue;
                velocity.Linear += Vector3.Cross(velocity.Angular, piece.Pose[b].Position - original[old].Position) + outward;
                physics.Simulation.Bodies[handle].Velocity = velocity;
            }
        }
        void Restore(Ragdoll piece, SavedRagdoll stored)
        {
            piece.Age = stored.Age;
            if (stored.Frozen) Freeze(piece);
        }
    }

    static bool SavedNear(SavedRagdoll saved, Vector3 owner) => saved.Pose.Length > 0
        && saved.Pose.All(p => float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z)
            && Vector3.DistanceSquared(p.Position, owner) < 500 * 500);

    RigidTransform[] InitialBoneWorld(Ragdoll prototype, CharacterCold cold, Matrix4x4 placement, RagdollDefinition source, RigidTransform[] original)
    {
        var rig = prototype.Setup.Rig;
        var scratch = rig.Rent();
        try
        {
            PoseDerived(scratch, prototype.Setup, cold);
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            var bones = new RigidTransform[rig.BoneCount];
            for (int h = 0; h < bones.Length; h++)
            {
                var animated = new RigidTransform(Vector3.Transform(scratch.DerivedPosition[h], placement), Quaternion.Normalize(turn * scratch.DerivedRotation[h]));
                int anchor = h;
                while (anchor >= 0 && source.BodyOfBone[anchor] < 0) anchor = rig.Parent[anchor];
                if (anchor >= 0)
                {
                    int body = source.BodyOfBone[anchor];
                    var animatedAnchor = new RigidTransform(Vector3.Transform(scratch.DerivedPosition[anchor], placement),
                        Quaternion.Normalize(turn * scratch.DerivedRotation[anchor]));
                    var animatedBody = source.Bodies[body].BoneInBody.Inverse().Then(animatedAnchor);
                    // Unphysical children retain their shaped, animated offset rather than the neutral human rig's proportions.
                    bones[h] = animated.Then(animatedBody.Inverse()).Then(original[body]);
                }
                else bones[h] = animated;
            }
            // Physical bones must preserve their exact shaped/animated initial transform.
            for (int b = 0; b < source.Bodies.Length; b++) bones[source.Bodies[b].Bone] = source.Bodies[b].BoneInBody.Then(original[b]);
            return bones;
        }
        finally { rig.Return(scratch); }
    }

    static (int[] Anchors, RigidTransform[] Offsets) FragmentAnchors(Ragdoll piece, RigidTransform[] world, RigidTransform[]? initial = null)
    {
        var rig = piece.Setup.Rig;
        var anchors = new int[rig.BoneCount];
        var offsets = new RigidTransform[rig.BoneCount];
        for (int h = 0; h < rig.BoneCount; h++)
        {
            int at = h;
            while (at >= 0 && piece.Definition.BodyOfBone[at] < 0) at = rig.Parent[at];
            int body = at >= 0 ? piece.Definition.BodyOfBone[at] : -1;
            int family = LimbFamily(rig.ByHandle[h]?.Name ?? "");
            // A retained forearm/calf owns the missing upper limb's cut-edge influences, not the torso across its severed joint.
            if (piece.Definition.BodyOfBone[h] < 0 && family != 0 && (body < 0 || BodyFamily(body) != family))
            {
                int limb = Nearest(b => BodyFamily(b) == family);
                if (limb >= 0) body = limb;
            }
            if (body < 0)
            {
                // A missing pelvis/spine/root must follow the retained axial body even when a loose arm is geometrically nearer.
                body = Nearest(b => Axial(Name(b)));
                if (body < 0) body = Nearest(_ => true);
            }
            anchors[h] = body;
            string Name(int b) => rig.ByHandle[piece.Definition.Bodies[b].Bone]?.Name ?? piece.Definition.Bodies[b].Name;
            int BodyFamily(int b) => LimbFamily(Name(b));
            int Nearest(Func<int, bool> allowed)
            {
                int best = -1;
                float nearest = float.PositiveInfinity;
                for (int b = 0; b < piece.Definition.Bodies.Length; b++)
                {
                    if (!allowed(b)) continue;
                    float distance = Vector3.DistanceSquared(rig.BindPosition[h], piece.Definition.Bodies[b].Bind.Position);
                    if (distance < nearest) { nearest = distance; best = b; }
                }
                return best;
            }
            offsets[h] = world[h].Then((initial ?? piece.Pose)[body].Inverse());
            if (piece.Definition.BodyOfBone[h] is int own and >= 0) offsets[h] = piece.Definition.Bodies[own].BoneInBody;
        }
        return (anchors, offsets);
    }

    static void RestoreFragmentOffsets(SavedFragmentAnchors? saved, Ragdoll piece, int[] anchors, RigidTransform[] offsets)
    {
        if (saved is null || saved.Anchors.Length != anchors.Length || saved.Offsets.Length != offsets.Length
            || !saved.Anchors.SequenceEqual(anchors) || saved.Offsets.Any(p => !float.IsFinite(p.Position.LengthSquared())
                || !float.IsFinite(p.Rotation.LengthSquared()) || p.Rotation.LengthSquared() < 1e-12f)) return;
        for (int h = 0; h < offsets.Length; h++)
            if (piece.Definition.BodyOfBone[h] < 0) offsets[h] = saved.Offsets[h] with { Rotation = Quaternion.Normalize(saved.Offsets[h].Rotation) };
    }

    static int LimbFamily(string name)
    {
        int side = name.Contains(" L ", StringComparison.Ordinal) ? 1 : name.Contains(" R ", StringComparison.Ordinal) ? 2 : 0;
        if (side == 0 || name.Contains("Clavicle", StringComparison.Ordinal)) return 0;
        if (name.Contains("Thigh", StringComparison.Ordinal) || name.Contains("Calf", StringComparison.Ordinal)
            || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal)) return side + 2;
        return name.Contains("Arm", StringComparison.OrdinalIgnoreCase) || name.Contains("Hand", StringComparison.Ordinal) || name.Contains("Finger", StringComparison.Ordinal) ? side : 0;
    }

    static bool Axial(string name) => name == "Bip01" || name.Contains("Pelvis", StringComparison.Ordinal) || name.Contains("Spine", StringComparison.Ordinal)
        || name.Contains("Neck", StringComparison.Ordinal) || name.Contains("Head", StringComparison.Ordinal) || name.Contains("Jaw", StringComparison.Ordinal);

    // Carrying owns each cut side's root component independently; disconnected shoulder chains remain free.
    static bool[] RootComponent(RagdollDefinition definition)
    {
        var component = new bool[definition.Bodies.Length];
        component[definition.Root] = true;
        bool changed;
        do
        {
            changed = false;
            foreach (var joint in definition.Joints)
                if (component[joint.A] != component[joint.B])
                {
                    component[joint.A] = component[joint.B] = true;
                    changed = true;
                }
        } while (changed);
        return component;
    }

    void CoverTorso(TorsoFragments fragments, IRagdollEnvironment env, long tick)
    {
        Cover(fragments.Positive);
        Cover(fragments.Negative);
        void Cover(Ragdoll piece)
        {
            if (piece.Frozen) return;
            foreach (var pose in piece.Pose) physics.Cover(pose.Position, 4 * piece.Scale, env, tick);
        }
    }

    void BuoyTorsoRagdolls(World world, float dt)
    {
        foreach (var id in torsoOrder)
        {
            var f = torsoRagdolls[id];
            if (!f.Positive.Frozen) Buoy(world, f.Positive, dt);
            if (!f.Negative.Frozen) Buoy(world, f.Negative, dt);
        }
    }

    void FinishTorsoRagdolls(World world, IRagdollEnvironment env, float dt)
    {
        foreach (var id in torsoOrder)
        {
            var f = torsoRagdolls[id];
            if (!world.Characters.TryResolveNext(id, out int slot)) continue;
            var cold = world.Characters.Cold(slot)!;
            bool moving = !f.Positive.Frozen || !f.Negative.Frozen;
            Finish(f.Positive, f.PositiveCarried);
            Finish(f.Negative, f.NegativeCarried);
            ref var hot = ref world.Characters.Next[slot];
            if (!f.NegativeCarried) hot.Position = f.Negative.Pose[f.Negative.Definition.Root].Position;
            cold.TorsoPositivePosition = f.Positive.Pose[f.Positive.Definition.Root].Position;
            hot.Velocity = Vector3.Zero;
            var placement = Placement(f.Negative, cold, hot);
            if (moving || f.Published is null || f.PublishedIn != placement)
            {
                f.Published = new(BuildFragmentPose(f.Positive, f.PositiveAnchors, f.PositiveOffsets, placement),
                    BuildFragmentPose(f.Negative, f.NegativeAnchors, f.NegativeOffsets, placement));
                f.PublishedIn = placement;
            }
        }
        void Finish(Ragdoll piece, bool carried)
        {
            if (piece.Frozen) return;
            Surface(piece, env);
            ReadBack(piece);
            piece.Age += Afloat(world, piece) ? dt * 0.5f : dt;
            // Every disconnected component must settle; a resting torso cannot freeze an airborne arm.
            if (!carried && (piece.Age >= FreezeLatest || piece.Age >= FreezeEarliest && StillShare(piece) >= 0.99f)) Freeze(piece);
        }
    }

    static BonePose BuildFragmentPose(Ragdoll piece, int[] anchors, RigidTransform[] offsets, Matrix4x4 placement, RigidTransform[]? worldPoses = null)
    {
        Matrix4x4.Invert(placement, out var toModel);
        var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
        var unturn = Quaternion.Inverse(turn);
        var pose = new BonePose(piece.Setup.Rig.BoneCount)
        {
            AbsolutePositions = true,
            Components = piece.Components ??= BoneComponents(piece.Definition, anchors),
        };
        for (int h = 0; h < pose.Set.Length; h++)
        {
            var bone = offsets[h].Then((worldPoses ?? piece.Pose)[anchors[h]]);
            pose.Set[h] = true;
            pose.Position[h] = Vector3.Transform(bone.Position, toModel);
            pose.Rotation[h] = Quaternion.Normalize(unturn * bone.Rotation);
        }
        return pose;
    }

    static int[] BoneComponents(RagdollDefinition definition, int[] anchors)
    {
        var bodyComponents = new int[definition.Bodies.Length];
        Array.Fill(bodyComponents, -1);
        var pending = new Stack<int>();
        int component = 0;
        for (int b = 0; b < bodyComponents.Length; b++)
        {
            if (bodyComponents[b] >= 0) continue;
            bodyComponents[b] = component;
            pending.Push(b);
            while (pending.TryPop(out int body))
                foreach (var joint in definition.Joints)
                {
                    int other = joint.A == body ? joint.B : joint.B == body ? joint.A : -1;
                    if (other < 0 || bodyComponents[other] >= 0) continue;
                    bodyComponents[other] = component;
                    pending.Push(other);
                }
            component++;
        }
        return [.. anchors.Select(body => bodyComponents[body])];
    }

    SavedRagdoll? SaveTorsoRagdoll(CharacterId id)
    {
        if (!torsoRagdolls.TryGetValue(id, out var f)) return null;
        SavedRagdoll Save(Ragdoll piece) => new(piece.Frozen, piece.Age, (RigidTransform[])piece.Pose.Clone());
        var negative = Save(f.Negative);
        return negative with { Torso = new SavedTorsoRagdoll(f.Cut, Save(f.Positive), negative)
        {
            PositiveAnchors = new((int[])f.PositiveAnchors.Clone(), (RigidTransform[])f.PositiveOffsets.Clone()),
            NegativeAnchors = new((int[])f.NegativeAnchors.Clone(), (RigidTransform[])f.NegativeOffsets.Clone()),
        } };
    }

    void RemoveTorsoRagdoll(CharacterId id)
    {
        if (!torsoRagdolls.Remove(id, out var f)) return;
        Release(f.Positive);
        Release(f.Negative);
        torsoOrder.Remove(id);
        void Release(Ragdoll piece)
        {
            Clear(piece);
            if (shapes.Remove(piece.Definition, out var owned))
                foreach (var shape in owned) physics.Simulation.Shapes.RemoveAndDispose(shape.Shape, physics.Pool);
        }
    }

    void HashTorsoRagdolls(ref StateHasher hasher)
    {
        if (torsoOrder.Count == 0) return;
        hasher.Add(0x43555452);
        foreach (var id in torsoOrder)
        {
            var f = torsoRagdolls[id];
            hasher.Add(id);
            hasher.Add(f.Cut.Plane.X); hasher.Add(f.Cut.Plane.Y); hasher.Add(f.Cut.Plane.Z); hasher.Add(f.Cut.Plane.W);
            hasher.Add(f.Cut.FullBody);
            HashPiece(f.Positive, ref hasher);
            HashPiece(f.Negative, ref hasher);
            HashTorsoGrip(f.PositiveGrip, ref hasher);
            HashTorsoGrip(f.NegativeGrip, ref hasher);
            HashTorsoSurface(f.PositiveSupport, ref hasher);
            HashTorsoSurface(f.NegativeSupport, ref hasher);
            HashOffsets(f.PositiveAnchors, f.PositiveOffsets, ref hasher);
            HashOffsets(f.NegativeAnchors, f.NegativeOffsets, ref hasher);
            if (!f.PositiveGripHolder.IsNone || !f.NegativeGripHolder.IsNone)
            {
                hasher.Add(0x47724864); hasher.Add(f.PositiveGripHolder); hasher.Add(f.NegativeGripHolder);
            }
        }
        static void HashOffsets(int[] anchors, RigidTransform[] offsets, ref StateHasher hash)
        {
            for (int h = 0; h < anchors.Length; h++)
            {
                hash.Add(anchors[h]); hash.Add(offsets[h].Position);
                hash.Add(offsets[h].Rotation.X); hash.Add(offsets[h].Rotation.Y); hash.Add(offsets[h].Rotation.Z); hash.Add(offsets[h].Rotation.W);
            }
        }
        static void HashPiece(Ragdoll piece, ref StateHasher hash)
        {
            hash.Add(piece.Frozen); hash.Add(piece.Age);
            foreach (var pose in piece.Pose)
            {
                hash.Add(pose.Position);
                hash.Add(pose.Rotation.X); hash.Add(pose.Rotation.Y); hash.Add(pose.Rotation.Z); hash.Add(pose.Rotation.W);
            }
        }
    }
}
