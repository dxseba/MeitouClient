using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Simulation.Carrying;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    sealed record TorsoGrip(RagdollSkinSurface.Grip? Surface, int Body, Vector3 Local);

    sealed class CarryContactShape
    {
        public BodyBuild Build;
        public CharacterAppearance? Look;
        public CharacterPoseSetup? Original, Setup;
    }
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterCold, CarryContactShape> carryContactShapes = new();

    CharacterPoseSetup ContactSetup(CharacterCold cold, CharacterPoseSetup original)
    {
        if (cold is not { Stats: { } stats, Medical: { } medical, Race.IsRobot: false, Appearance: { Animal: null } look }) return original;
        var build = CharacterShape.BuildFromStats(look.Female, stats, static (state, stat) => state[stat], medical.Hunger);
        var cache = carryContactShapes.GetValue(cold, static _ => new());
        if (cache.Setup is null || !cache.Build.Near(build) || !ReferenceEquals(cache.Look, look) || !ReferenceEquals(cache.Original, original))
        {
            cache.Build = build;
            cache.Look = look;
            cache.Original = original;
            cache.Setup = rigs.Reshape(look, build) is { } reshaped ? new CharacterPoseSetup
            {
                Rig = original.Rig, Shape = reshaped.Shape, GroundOffset = reshaped.GroundOffset,
                Fixed = original.Fixed, Idle = original.Idle, Animal = original.Animal,
            } : original;
        }
        return cache.Setup;
    }

    Matrix4x4[] ContactSkin(Ragdoll piece, CharacterCold cold, ActiveLayer[] layers, BonePose bones, Matrix4x4 publicationFrame)
    {
        var setup = ContactSetup(cold, piece.Setup);
        var scratch = piece.Setup.Rig.Rent();
        try
        {
            var skin = new Matrix4x4[piece.Setup.Rig.BoneCount];
            scratch.Pose(setup.Shape, layers, skin, bones);
            // Absolute physics bones are published in the original setup's frame; rendering lowers that frame by the current build's ground offset.
            var delta = Vector3.TransformNormal(new(0, piece.Setup.GroundOffset - setup.GroundOffset, 0), publicationFrame);
            if (delta != Vector3.Zero)
                for (int h = 0; h < skin.Length; h++) skin[h] *= Matrix4x4.CreateTranslation(delta);
            return skin;
        }
        finally { piece.Setup.Rig.Return(scratch); }
    }

    Vector3? UpperArmOf(CharacterTable table, CharacterId holder, ref RagdollSkinSurface.Grip? material)
    {
        if (!table.TryResolveNext(holder, out int i)) return null;
        ref readonly var hot = ref (table.Phase == TablePhase.Outside ? ref table.Previous[i] : ref table.Next[i]);
        if (table.Cold(i) is not { Appearance: { } look } carrier || SetupOf(look) is not { } setup
            || setup.Rig.Handle("Bip01 L UpperArm") is not int shoulder || setup.Rig.Handle("Bip01 L Forearm") is not int elbow)
            return CarryPose.Fallback.World(hot.Position, hot.Yaw).Position;
        setup = ContactSetup(carrier, setup);
        var scratch = setup.Rig.Rent();
        try
        {
            PoseDerived(scratch, setup, carrier);
            var placement = CharacterRigs.Placement(setup, hot.Position, hot.Yaw, carrier.Animal is { } animal ? animal.Scale(carrier.ShownAge) : 1);
            var start = Vector3.Transform(scratch.DerivedPosition[shoulder], placement);
            var end = Vector3.Transform(scratch.DerivedPosition[elbow], placement);
            var skin = new Matrix4x4[setup.Rig.BoneCount];
            for (int h = 0; h < skin.Length; h++)
                skin[h] = Matrix4x4.CreateTranslation(-setup.Rig.BindPosition[h])
                    * Matrix4x4.CreateScale(scratch.DerivedScale[h] / setup.Rig.BindScale[h])
                    * Matrix4x4.CreateFromQuaternion(scratch.DerivedRotation[h] * setup.Rig.BindRotationInverse[h])
                    * Matrix4x4.CreateTranslation(scratch.DerivedPosition[h]) * placement;
            // The upper face changes around the arm shaft during lift/turn; only the held half's material is fixed.
            material = SkinSurfaceFor(look) is { } surface && DefinitionOf(look, setup.Rig, 1) is { } definition
                ? surface.FindUpperArmSupport(look, setup.Rig, definition, skin, start, end) : null;
            return material is not null ? material.Posed(skin) : CarryPose.BicepsSupport(start, end);
        }
        finally { setup.Rig.Return(scratch); }
    }

    void AlignTorsoGrip(TorsoFragments fragments, Ragdoll piece, CharacterCold cold, int side, RigidTransform[] target,
        bool[] carried, Vector3 support, Matrix4x4 publicationFrame, ActiveLayer[] layers, bool entering)
    {
        var anchors = side > 0 ? fragments.PositiveAnchors : fragments.NegativeAnchors;
        var offsets = side > 0 ? fragments.PositiveOffsets : fragments.NegativeOffsets;
        ref var grip = ref (side > 0 ? ref fragments.PositiveGrip : ref fragments.NegativeGrip);
        var rig = piece.Setup.Rig;
        var scratch = rig.Rent();
        try
        {
            var pinned = carried.Select((root, b) => root && !Simulates(piece, RagdollPart.CarryMode, piece.Definition.Bodies[b])).ToArray();
            var pinnedBones = anchors.Select(b => pinned[b]).ToArray();
            var poseTargets = target.Select((pose, b) => carried[b] && (entering || pinned[b]) ? pose : piece.Pose[b]).ToArray();
            var bones = BuildFragmentPose(piece, anchors, offsets, Matrix4x4.Identity, poseTargets);
            var skin = ContactSkin(piece, cold, layers, bones, publicationFrame);
            if (grip is null)
            {
                var limbCuts = Enumerable.Repeat(-1f, 4).ToArray();
                if (cold.Medical is { } medical)
                    foreach (var part in medical.Parts)
                        if (part.Limb == Bodies.LimbState.Stump && part.Template.LimbSlot is >= 0 and <= 3 and var limb)
                            limbCuts[limb] = Wounds.SeveredLevels.Height(Wounds.SeveredLevels.Of(piece.Id, limb), limb);
                var surface = cold.Appearance is { } look && SkinSurfaceFor(look) is { } bodySkin
                    ? bodySkin.FindGrip(look, rig, fragments.Cut.Plane, side, fragments.Cut.FullBody,
                        bones.Components!, bones.Components![piece.Definition.Bodies[piece.Definition.Root].Bone], piece.Definition,
                        skin, support, limbCuts, query: new(PinnedBones: pinnedBones, AreaCentre: true)) : null;
                if (surface is not null) grip = new(surface, -1, default);
                else
                {
                    float mass = 0;
                    var centre = Vector3.Zero;
                    for (int b = 0; b < target.Length; b++)
                        if (pinned[b]) { centre += target[b].Position * piece.Definition.Bodies[b].Mass; mass += piece.Definition.Bodies[b].Mass; }
                    centre = mass > 0 ? centre / mass : target[piece.Definition.Root].Position;
                    var selected = ColliderGrip(piece.Definition, target, b => pinned[b], centre, axialOnly: false);
                    grip = new(null, selected.Body, selected.Local);
                }
            }
            var contact = grip.Surface is { } saved ? saved.Posed(skin) : target[grip.Body].Apply(grip.Local);
            var shift = (support - contact) / (grip.Surface?.RootShare ?? 1);
            if (!float.IsFinite(shift.LengthSquared())) return;
            for (int b = 0; b < target.Length; b++)
                if (carried[b] && (entering || pinned[b])) target[b] = target[b] with { Position = target[b].Position + shift };
        }
        finally { rig.Return(scratch); }
    }

    /// <summary>The actual cached half material and current upper-arm support in world space, for native contact verification.</summary>
    public bool TryTorsoCarryContact(World world, CharacterId id, int side, out Vector3 contact, out Vector3 support)
    {
        contact = support = default;
        if (!torsoRagdolls.TryGetValue(id, out var fragments) || !world.Characters.TryResolveNext(id, out int slot)
            || world.Characters.Cold(slot) is not { } cold) return false;
        var piece = side > 0 ? fragments.Positive : fragments.Negative;
        var grip = side > 0 ? fragments.PositiveGrip : fragments.NegativeGrip;
        var holder = side > 0 ? cold.TorsoPositiveCarriedBy : cold.CarriedBy;
        RagdollSkinSurface.Grip? arm = null;
        if (grip is null || UpperArmOf(world.Characters, holder, ref arm) is not { } armPoint) return false;
        support = armPoint;
        var frame = Placement(fragments.Negative, cold, world.Characters.Previous[slot]);
        if (grip.Surface is { } material)
        {
            var anchors = side > 0 ? fragments.PositiveAnchors : fragments.NegativeAnchors;
            var offsets = side > 0 ? fragments.PositiveOffsets : fragments.NegativeOffsets;
            var bones = BuildFragmentPose(piece, anchors, offsets, Matrix4x4.Identity);
            contact = material.Posed(ContactSkin(piece, cold, TorsoCarryLayers(piece, cold, side > 0), bones, frame));
        }
        else contact = piece.Pose[grip.Body].Apply(grip.Local);
        return true;
    }

    static (int Body, Vector3 Local) ColliderGrip(RagdollDefinition definition, RigidTransform[] target, Func<int, bool> allowed, Vector3 support, bool axialOnly)
    {
        bool HasAxial(int b) => Axial(definition.Bodies[b].Name) && !definition.Bodies[b].Name.Contains("Head", StringComparison.Ordinal)
            && !definition.Bodies[b].Name.Contains("Jaw", StringComparison.Ordinal) && !definition.Bodies[b].Name.Contains("Neck", StringComparison.Ordinal);
        bool axialExists = axialOnly && Enumerable.Range(0, target.Length).Any(b => allowed(b) && HasAxial(b));
        int best = definition.Root;
        Vector3 local = default;
        float nearest = float.MaxValue;
        for (int b = 0; b < target.Length; b++)
        {
            if (!allowed(b) || axialExists && !HasAxial(b)) continue;
            var body = definition.Bodies[b];
            var wanted = target[b].Inverse().Apply(support);
            if (body.Type == PhysicsShapeType.Mesh)
            {
                var points = RagdollCut.Points(body);
                if (!RagdollHull.TryTriangles(points, out var triangles)) continue;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = points[triangles[i]];
                    var c = points[triangles[i + 1]];
                    var d = points[triangles[i + 2]];
                    var bary = RagdollSkinSurface.ClosestBarycentric(wanted, a, c, d);
                    Candidate(a * bary.X + c * bary.Y + d * bary.Z);
                }
            }
            else if (body.Type == PhysicsShapeType.Box)
            {
                var half = body.Size * 0.5f;
                var point = Vector3.Clamp(wanted, -half, half);
                if (point != wanted) Candidate(point);
                else for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
                { var onFace = point; onFace[axis] = half[axis] * sign; Candidate(onFace); }
            }
            else
            {
                float length = body.Type == PhysicsShapeType.Capsule ? body.Size.Y * 0.5f : 0;
                var axis = new Vector3(0, Math.Clamp(wanted.Y, -length, length), 0);
                var direction = wanted - axis;
                if (direction.LengthSquared() < 1e-8f) direction = Vector3.UnitX;
                Candidate(axis + Vector3.Normalize(direction) * body.Size.X);
            }
            void Candidate(Vector3 point)
            {
                float distance = Vector3.DistanceSquared(target[b].Apply(point), support);
                if (distance >= nearest) return;
                nearest = distance; best = b; local = point;
            }
        }
        return (best, local);
    }

    static void HashTorsoGrip(TorsoGrip? grip, ref StateHasher hash)
    {
        if (grip is null) return;
        hash.Add(0x47726970);
        hash.Add(grip.Body); hash.Add(grip.Local);
        HashTorsoSurface(grip.Surface, ref hash);
    }

    static void HashTorsoSurface(RagdollSkinSurface.Grip? surface, ref StateHasher hash)
    {
        hash.Add(surface is not null);
        if (surface is null) return;
        hash.Add(surface.Barycentric); hash.Add(surface.RootShare);
        foreach (var point in new[] { surface.Triangle.A, surface.Triangle.B, surface.Triangle.C })
        {
            hash.Add(point.Position); hash.Add((int)point.Bones);
            hash.Add(point.Weights.X); hash.Add(point.Weights.Y); hash.Add(point.Weights.Z); hash.Add(point.Weights.W);
        }
    }
}
