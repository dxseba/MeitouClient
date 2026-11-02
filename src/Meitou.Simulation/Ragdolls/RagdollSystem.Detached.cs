using System.Numerics;
using BepuPhysics;
using Meitou.Data.Characters;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Ragdolls;

/// <summary>A severed head (-1) or limb (0..3), fully articulated in its owner's drawn model space.</summary>
public sealed record DetachedRagdollPose(int Part, BonePose Bones, int GroundItemId = 0, int FragmentId = 0);

public sealed partial class RagdollSystem
{
    sealed class DetachedPiece
    {
        public required int Part;
        public int FragmentId;
        public required Ragdoll Body;
        public required int[] Anchors;
        public required RigidTransform[] Offsets;
        public required string ItemBone;
        public int GroundItemId;
        public BonePose? Published;
        public Matrix4x4 PublishedIn;
    }

    sealed class DetachedOwner
    {
        public int Missing, Robotic;
        public HeadCutState? HeadCut;
        public RagdollDefinition? Source, Remaining;
        public readonly List<DetachedPiece> Pieces = [];
        public readonly List<RagdollDefinition> Retired = [];
        public IReadOnlyList<DetachedRagdollPose> Published = [];
        public Ragdoll? RemainingBody;
        public int[]? RemainingAnchors;
        public RigidTransform[]? RemainingOffsets;
    }

    readonly Dictionary<CharacterId, DetachedOwner> detachedOwners = [];
    readonly List<CharacterId> detachedOrder = [];
    readonly HashSet<CharacterId> detachedSeen = [];

    public IReadOnlyList<DetachedRagdollPose> PublishDetached(CharacterId id) => detachedOwners.TryGetValue(id, out var owner) ? owner.Published : [];

    int MovingDetached => detachedOrder.Sum(id => detachedOwners[id].Pieces.Count(p => !p.Body.Frozen));
    int DetachedCount => detachedOrder.Sum(id => detachedOwners[id].Pieces.Count);

    RagdollDefinition RemainingDefinition(CharacterId id, RagdollDefinition source) =>
        detachedOwners.TryGetValue(id, out var owner) && owner.Remaining is { } remaining ? remaining : source;

    BonePose? BuildRemaining(Ragdoll body, CharacterCold cold, Matrix4x4 placement)
    {
        if (options is not { AdvancedRagdolls: true } || (body.Mask & RagdollPart.Whole) == 0 || (body.Mask & RagdollPart.CarryMode) != 0
            || body.BlendLeft > 0 || !detachedOwners.TryGetValue(body.Id, out var owner)
            || owner.Missing == 0 && owner.HeadCut is null) return null;
        if (!ReferenceEquals(owner.RemainingBody, body))
        {
            // A complete pose keeps surviving calves/feet at their actual collision bodies, instead of reconstructing them from a fallen pelvis.
            var world = InitialBoneWorld(body, cold, placement, body.Definition, body.Pose);
            (owner.RemainingAnchors, owner.RemainingOffsets) = FragmentAnchors(body, world);
            owner.RemainingBody = body;
        }
        return BuildFragmentPose(body, owner.RemainingAnchors!, owner.RemainingOffsets!, placement);
    }

    void PruneDetached(World world)
    {
        detachedSeen.RemoveWhere(id => !world.Characters.TryResolveNext(id, out _));
        foreach (var id in detachedOrder.ToArray())
        {
            if (!world.Characters.TryResolveNext(id, out _) || options is not { AdvancedRagdolls: true }) { RemoveDetachedOwner(id); continue; }
            var owner = detachedOwners[id];
            foreach (var retired in owner.Retired.ToArray())
                if (!ragdolls.TryGetValue(id, out var body) || !ReferenceEquals(body.Definition, retired))
                {
                    ReleaseDetachedShapes(retired);
                    owner.Retired.Remove(retired);
                }
        }
    }

    void PrepareDetached(World world, CharacterId id, CharacterCold cold, ref CharacterHot hot,
        (Vector3 Velocity, Vector3 Push) start, bool advanced)
    {
        if (!advanced || cold.Fighter is { IsAnimal: true } || cold.Race is not { IsRobot: false } race
            || cold.Medical is not { } medical || cold.Appearance is not { } look) return;
        int missing = medical.Decapitated ? 1 : 0, robotic = 0;
        foreach (var part in medical.PartSpan)
        {
            if (part.Template.LimbSlot is not (>= 0 and <= 3 and var limb)) continue;
            if (part.Limb == LimbState.Stump) missing |= 1 << (limb + 1);
            if (part.IsRobotic(race)) robotic |= 1 << (limb + 1);
        }
        bool firstSeen = detachedSeen.Add(id);
        bool torso = cold.TorsoBisection is not null;
        HeadCutState? headCut = cold.HeadCut is { IsValid: true } cut ? cut : null;
        bool first = !detachedOwners.TryGetValue(id, out var owner);
        if (first)
        {
            if (missing == 0 && robotic == 0 && headCut is null) return;
            owner = new DetachedOwner();
            detachedOwners.Add(id, owner);
            detachedOrder.Add(id);
        }
        int previousMissing = owner!.Missing, previousRobotic = owner.Robotic;
        owner.Robotic = robotic;
        if (missing == previousMissing && headCut == owner.HeadCut) return;
        if (SetupOf(look) is not { } setup || DefinitionOf(look, setup.Rig, 1) is not { } source) return;
        owner.Source = source;
        ragdolls.TryGetValue(id, out var before);
        var previousLook = world.Snapshot.Characters.FirstOrDefault(c => c.Id == id)?.Appearance;
        var initialSetup = before?.Setup ?? (previousLook is null ? null : SetupOf(previousLook)) ?? setup;
        if (!ReferenceEquals(initialSetup.Rig, setup.Rig)) initialSetup = setup;
        var prototype = new Ragdoll { Id = id, Definition = source, Setup = initialSetup, Scale = 1 };
        var placement = Placement(prototype, cold, hot);
        var original = Animated(prototype, cold, placement);
        var previousVelocity = new BodyVelocity?[source.Bodies.Length];
        if (before is not null)
            for (int b = 0; b < source.Bodies.Length; b++)
            {
                int old = before.Definition.BodyOfBone[source.Bodies[b].Bone];
                if (old < 0 || old >= before.Pose.Length) continue;
                var bone = before.Definition.Bodies[old].BoneInBody.Then(before.Pose[old]);
                original[b] = source.Bodies[b].BoneInBody.Inverse().Then(bone);
                if (old < before.Bodies.Length && before.Bodies[old] is { } handle && before.Dynamic[old])
                {
                    var velocity = physics.Simulation.Bodies[handle].Velocity;
                    velocity.Linear += Vector3.Cross(velocity.Angular, original[b].Position - before.Pose[old].Position);
                    previousVelocity[b] = velocity;
                }
            }
        var boneWorld = InitialBoneWorld(prototype, cold, placement, source, original);
        RagdollDefinition? remaining = source;
        bool restoredHead = firstSeen && cold.Save is not null;
        var ownerPosition = hot.Position;
        var headPlane = Vector4.Zero;
        bool slicedHead = headCut is { } headState && SkinSurfaceFor(look)?.HeadPlane(setup.Rig, headState.Plane, out headPlane) == true;
        if (slicedHead && headCut is { } state)
        {
            var split = RagdollSeverance.SplitHead(source, headPlane, state.Mode);
            remaining = split.Remaining;
            if (!torso && owner.HeadCut != headCut)
            {
                foreach (var old in owner.Pieces.Where(p => p.Part == -1).ToArray())
                {
                    ReleaseDetachedPiece(old);
                    owner.Pieces.Remove(old);
                }
                var normal = Vector3.TransformNormal(new(headPlane.X, headPlane.Y, headPlane.Z), placement);
                normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitX;
                if (split.Positive is { } positive) Spawn(positive, -1, state.Mode == HeadCutMode.NeckDetach ? 0 : 1, normal);
                if (split.Negative is { } negative) Spawn(negative, -1, 2, -normal);
            }
        }
        int newlyMissing = missing & ~previousMissing & ~previousRobotic;
        for (int part = -1; part <= 3; part++)
        {
            if (part == -1 && slicedHead) continue;
            if ((missing & 1 << (part + 1)) == 0 || remaining is null) continue;
            float height = part < 0 ? 0 : SeveredLevels.Height(SeveredLevels.Of(id, part, world.Tick), part);
            var split = RagdollSeverance.Split(remaining, part, height);
            remaining = split.Remaining;
            if ((newlyMissing & 1 << (part + 1)) == 0 || torso || firstSeen && cold.Save is not null || split.Detached is not { } definition) continue;
            var oldPiece = owner.Pieces.Find(p => p.Part == part);
            if (oldPiece is not null)
            {
                ReleaseDetachedPiece(oldPiece);
                owner.Pieces.Remove(oldPiece);
            }
            Spawn(definition, part, 0, Vector3.Zero);
        }
        owner.Missing = missing;
        owner.HeadCut = headCut;
        var oldDefinition = owner.Remaining;
        owner.Remaining = remaining ?? source; // A humanoid retains its spine/pelvis even after every ordinary limb loss.
        // A torso cut takes the previous body itself; initial loaded stumps must leave saved-pose restoration to the normal path.
        if (torso || firstSeen && cold.Save is not null && before is null)
        {
            if (oldDefinition is not null && !ReferenceEquals(oldDefinition, source)) owner.Retired.Add(oldDefinition);
            return;
        }
        if (before is not null)
        {
            Destroy(before);
            order.Remove(id);
        }
        if (oldDefinition is not null && !ReferenceEquals(oldDefinition, source)) ReleaseDetachedShapes(oldDefinition);
        var wanted = Wanted(cold);
        if (wanted != RagdollPart.None)
        {
            var body = Make(owner.Remaining);
            var initial = FromOriginal(owner.Remaining);
            Rebuild(body, wanted, initial, start, true, world.Seed);
            Inherit(body);
            if ((wanted & RagdollPart.Whole) != 0) SetTone(body, medical.Dead ? Tone.Dead : Tone.Unconscious);
            ragdolls.Add(id, body);
            order.Add(id);
        }
        owner.Published = [];

        void Spawn(RagdollDefinition definition, int part, int fragment, Vector3 separation)
        {
            var piece = Make(definition, initialSetup);
            var initial = FromOriginal(definition);
            bool restored = part == -1 && restoredHead;
            var motion = restored ? default : (start.Velocity + separation * 1.5f, start.Push);
            Rebuild(piece, RagdollPart.Whole, initial, motion, true, world.Seed);
            if (!restored) Inherit(piece, separation * 1.5f);
            if (!restored && piece.Bodies[piece.Definition.Root] is { } root && start.Push.LengthSquared() > 1e-8f)
            {
                var spin = Vector3.Cross(start.Push, Vector3.UnitY) * .1f;
                if (spin.Length() > MaxPushSpin) spin = Vector3.Normalize(spin) * MaxPushSpin;
                physics.Simulation.Bodies[root].Velocity.Angular += spin;
            }
            SetTone(piece, medical.Dead ? Tone.Dead : Tone.Unconscious);
            var (anchors, offsets) = FragmentAnchors(piece, boneWorld, initial);
            if (restored)
            {
                var flat = new Vector3(separation.X, 0, separation.Z);
                flat = flat.LengthSquared() > 1e-8f ? Vector3.Normalize(flat) : fragment == 2 ? -Vector3.UnitX : Vector3.UnitX;
                var at = ownerPosition + flat * 2.5f;
                at.Y = world.Walkability.GroundHeight(at.X, at.Z, ownerPosition.Y) + 1.35f;
                Shift(piece, at - piece.Pose[definition.Root].Position);
                piece.Age = FreezeLatest;
                Freeze(piece);
            }
            string bone = part < 0 ? "" : medical.PartSpan.ToArray().First(p => p.Template.LimbSlot == part).Template.BoneName2;
            owner.Pieces.Add(new DetachedPiece
            {
                Part = part, FragmentId = fragment, Body = piece, Anchors = anchors, Offsets = offsets, ItemBone = bone,
            });
        }

        Ragdoll Make(RagdollDefinition definition, CharacterPoseSetup? poseSetup = null) => new()
        {
            Id = id, Definition = definition, Setup = poseSetup ?? setup, Scale = 1,
            CarryBone = string.IsNullOrEmpty(look.Race.GetString("carry bone")) ? "Bip01" : look.Race.GetString("carry bone"),
            Pose = new RigidTransform[definition.Bodies.Length],
        };
        RigidTransform[] FromOriginal(RagdollDefinition definition) => [.. definition.Bodies.Select(body =>
        {
            int old = source.BodyOfBone[body.Bone];
            return body.BoneInBody.Inverse().Then(source.Bodies[old].BoneInBody.Then(original[old]));
        })];
        void Inherit(Ragdoll piece, Vector3 extra = default)
        {
            for (int b = 0; b < piece.Definition.Bodies.Length; b++)
            {
                int old = source.BodyOfBone[piece.Definition.Bodies[b].Bone];
                if (previousVelocity[old] is not { } velocity || piece.Bodies[b] is not { } handle || !piece.Dynamic[b]) continue;
                velocity.Linear += Vector3.Cross(velocity.Angular, piece.Pose[b].Position - original[old].Position) + start.Push + extra;
                physics.Simulation.Bodies[handle].Velocity = velocity;
            }
        }
    }

    void CoverDetached(IRagdollEnvironment env, long tick)
    {
        foreach (var id in detachedOrder)
            foreach (var part in detachedOwners[id].Pieces)
                if (!part.Body.Frozen)
                    foreach (var pose in part.Body.Pose) physics.Cover(pose.Position, 4 * part.Body.Scale, env, tick);
    }

    void BuoyDetached(World world, float dt)
    {
        foreach (var id in detachedOrder)
            foreach (var part in detachedOwners[id].Pieces)
                if (!part.Body.Frozen) Buoy(world, part.Body, dt);
    }

    void FinishDetached(World world, IRagdollEnvironment env, float dt)
    {
        foreach (var id in detachedOrder)
        {
            var owner = detachedOwners[id];
            if (owner.Pieces.Count == 0 || !world.Characters.TryResolveNext(id, out int slot)) continue;
            var cold = world.Characters.Cold(slot)!;
            var hot = world.Characters.Next[slot];
            bool changed = false;
            foreach (var part in owner.Pieces.ToArray())
            {
                if (part.GroundItemId != 0 && world.GroundItems.Find(part.GroundItemId) is null)
                {
                    ReleaseDetachedPiece(part);
                    owner.Pieces.Remove(part);
                    changed = true;
                    continue;
                }
                if (part.GroundItemId == 0 && part.ItemBone.Length > 0 && world.GroundItems.All.LastOrDefault(item => item.Fall is { } fall && fall.Character == id && fall.Bone == part.ItemBone) is { } linked)
                {
                    part.GroundItemId = linked.Id;
                    changed = true;
                }
                var piece = part.Body;
                bool moving = !piece.Frozen;
                if (moving)
                {
                    Surface(piece, env);
                    ReadBack(piece);
                    piece.Age += Afloat(world, piece) ? dt * 0.5f : dt;
                    if (piece.Age >= FreezeLatest || piece.Age >= FreezeEarliest && StillShare(piece) >= 0.99f) Freeze(piece);
                    changed = true;
                }
                if (part.GroundItemId != 0)
                {
                    int root = piece.Definition.Root;
                    var bone = piece.Definition.Bodies[root].BoneInBody.Then(piece.Pose[root]);
                    world.GroundItems.Move(part.GroundItemId, bone.Position, bone.Rotation);
                }
                var placement = Placement(piece, cold, hot);
                if (part.Published is null || moving || placement != part.PublishedIn)
                {
                    part.Published = BuildFragmentPose(piece, part.Anchors, part.Offsets, placement);
                    part.PublishedIn = placement;
                    changed = true;
                }
            }
            if (changed) owner.Published = owner.Pieces.Select(part =>
                new DetachedRagdollPose(part.Part, part.Published!, part.GroundItemId, part.FragmentId)).ToArray();
        }
    }

    void ReleaseDetachedShapes(RagdollDefinition definition)
    {
        if (shapes.Remove(definition, out var owned))
            foreach (var shape in owned) physics.Simulation.Shapes.RemoveAndDispose(shape.Shape, physics.Pool);
    }

    void ReleaseDetachedPiece(DetachedPiece piece)
    {
        Clear(piece.Body);
        ReleaseDetachedShapes(piece.Body.Definition);
    }

    void RemoveDetachedOwner(CharacterId id)
    {
        if (!detachedOwners.Remove(id, out var owner)) return;
        foreach (var piece in owner.Pieces) ReleaseDetachedPiece(piece);
        if (owner.Remaining is { } remaining && !ReferenceEquals(remaining, owner.Source))
        {
            if (ragdolls.TryGetValue(id, out var body) && ReferenceEquals(body.Definition, remaining))
            {
                Destroy(body);
                order.Remove(id);
            }
            ReleaseDetachedShapes(remaining);
        }
        foreach (var retired in owner.Retired) ReleaseDetachedShapes(retired);
        detachedOrder.Remove(id);
    }

    void HashDetached(ref StateHasher hasher)
    {
        foreach (var id in detachedOrder)
        {
            var owner = detachedOwners[id];
            if (owner.Missing == 0 && owner.HeadCut is null && owner.Pieces.Count == 0) continue;
            hasher.Add(0x44455441);
            hasher.Add(id);
            hasher.Add(owner.Missing);
            if (owner.HeadCut is { } headCut) headCut.Hash(ref hasher);
            foreach (var part in owner.Pieces)
            {
                hasher.Add(part.Part);
                if (part.FragmentId != 0) hasher.Add(part.FragmentId);
                hasher.Add(part.GroundItemId);
                hasher.Add(part.Body.Frozen);
                hasher.Add(part.Body.Age);
                foreach (var pose in part.Body.Pose)
                {
                    hasher.Add(pose.Position);
                    hasher.Add(pose.Rotation.X); hasher.Add(pose.Rotation.Y); hasher.Add(pose.Rotation.Z); hasher.Add(pose.Rotation.W);
                }
            }
        }
    }

    void DisposeDetached()
    {
        foreach (var id in detachedOrder.ToArray()) RemoveDetachedOwner(id);
        detachedSeen.Clear();
    }
}
