using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Simulation.Carrying;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    sealed record TorsoGrip(RagdollSkinSurface.Grip? Surface, int Body, Vector3 Local,
        RagdollSkinSurface.Grip? ReferenceSurface, int ReferenceBody, Vector3 ReferenceLocal, bool OriginalSeat);

    Vector3? PalmOf(CharacterTable table, CharacterId holder)
    {
        if (!table.TryResolveNext(holder, out int i)) return null;
        ref readonly var hot = ref table.Next[i];
        if (table.Cold(i) is not { Appearance: { } look } carrier || SetupOf(look) is not { } setup
            || setup.Rig.Handle("Bip01 L Hand") is not int hand)
            return CarryPose.Fallback.World(hot.Position, hot.Yaw).Position;
        var scratch = setup.Rig.Rent();
        try
        {
            PoseDerived(scratch, setup, carrier);
            var placement = CharacterRigs.Placement(setup, hot.Position, hot.Yaw, carrier.Medical?.ScaleK ?? 1);
            Vector3? Point(string name) => setup.Rig.Handle(name) is int bone ? Vector3.Transform(scratch.DerivedPosition[bone], placement) : null;
            return CarryPose.PalmPoint(Vector3.Transform(scratch.DerivedPosition[hand], placement),
                Point("Bip01 L Finger0") ?? Point("Bip01 L Finger1"), Point("Bip01 L Forearm"));
        }
        finally { setup.Rig.Return(scratch); }
    }

    void AlignTorsoGrip(TorsoFragments fragments, Ragdoll piece, CharacterCold cold, int side, RigidTransform[] target,
        bool[] carried, Vector3 palm, Matrix4x4 placement, ActiveLayer[] layers, bool entering)
    {
        var anchors = side > 0 ? fragments.PositiveAnchors : fragments.NegativeAnchors;
        var offsets = side > 0 ? fragments.PositiveOffsets : fragments.NegativeOffsets;
        ref var grip = ref (side > 0 ? ref fragments.PositiveGrip : ref fragments.NegativeGrip);
        var rig = piece.Setup.Rig;
        var scratch = rig.Rent();
        try
        {
            // The reference is the original carry animation and CarryAttach, before any cut-half correction.
            var referenceSkin = new Matrix4x4[rig.BoneCount];
            scratch.Pose(piece.Setup.Shape, layers, referenceSkin);
            for (int h = 0; h < referenceSkin.Length; h++) referenceSkin[h] *= placement;
            scratch.PoseDerived(piece.Setup.Shape, layers);
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            var referenceActors = fragments.Source.Bodies.Select(body => body.BoneInBody.Inverse().Then(new RigidTransform(
                Vector3.Transform(scratch.DerivedPosition[body.Bone], placement), Quaternion.Normalize(turn * scratch.DerivedRotation[body.Bone])))).ToArray();
            if (grip?.OriginalSeat == true) return;

            var pinned = carried.Select((root, b) => root && !Simulates(piece, RagdollPart.CarryMode, piece.Definition.Bodies[b])).ToArray();
            var pinnedBones = anchors.Select(b => pinned[b]).ToArray();
            var poseTargets = target.Select((pose, b) => carried[b] && (entering || pinned[b]) ? pose : piece.Pose[b]).ToArray();
            var bones = BuildFragmentPose(piece, anchors, offsets, Matrix4x4.Identity, poseTargets);
            var skin = new Matrix4x4[rig.BoneCount];
            scratch.Pose(piece.Setup.Shape, layers, skin, bones);
            if (grip is null)
            {
                var limbCuts = Enumerable.Repeat(-1f, 4).ToArray();
                if (cold.Medical is { } medical)
                    foreach (var part in medical.Parts)
                        if (part.Limb == Bodies.LimbState.Stump && part.Template.LimbSlot is >= 0 and <= 3 and var limb)
                            limbCuts[limb] = Wounds.SeveredLevels.Height(Wounds.SeveredLevels.Of(piece.Id, limb), limb);
                RagdollSkinSurface.Grip? reference = null, surface = null;
                if (cold.Appearance is { } look && SkinSurfaceFor(look) is { } bodySkin)
                {
                    reference = bodySkin.FindGrip(look, rig, default, 1, true, new int[rig.BoneCount], 0,
                        fragments.Source, referenceSkin, palm, limbCuts, query: new(NearestToPoint: true, AxialOnly: true, Uncut: true));
                    if (reference is not null)
                    {
                        var originalContact = reference.Posed(referenceSkin);
                        var query = new RagdollSkinSurface.GripQuery(NearestToPoint: true, AxialOnly: true, Retained: reference, PinnedBones: pinnedBones);
                        int root = bones.Components![piece.Definition.Bodies[piece.Definition.Root].Bone];
                        surface = bodySkin.FindGrip(look, rig, fragments.Cut.Plane, side, fragments.Cut.FullBody,
                            bones.Components, root, piece.Definition, skin, originalContact, limbCuts, query: query)
                            ?? bodySkin.FindGrip(look, rig, fragments.Cut.Plane, side, fragments.Cut.FullBody,
                                bones.Components, root, piece.Definition, skin, originalContact, limbCuts, query: query with { AxialOnly = false });
                    }
                }
                if (reference is not null && surface is not null)
                    grip = new(surface, -1, default, reference, -1, default,
                        surface.Triangle == reference.Triangle && surface.Barycentric == reference.Barycentric);
                else
                {
                    var original = ColliderGrip(fragments.Source, referenceActors, _ => true, palm, axialOnly: true);
                    var originalContact = reference is null ? referenceActors[original.Body].Apply(original.Local) : reference.Posed(referenceSkin);
                    var selected = ColliderGrip(piece.Definition, target, b => pinned[b], originalContact, axialOnly: true);
                    bool retained = Vector3.DistanceSquared(target[selected.Body].Apply(selected.Local), originalContact) <= 1e-10f;
                    grip = new(null, selected.Body, selected.Local, reference, original.Body, original.Local, retained);
                }
            }
            // Retaining the ordinary hip/trunk material means exactly the intact placement: no extra translation.
            if (grip.OriginalSeat) return;
            var support = grip.ReferenceSurface is { } originalSurface ? originalSurface.Posed(referenceSkin)
                : referenceActors[grip.ReferenceBody].Apply(grip.ReferenceLocal);
            var contact = grip.Surface is { } saved ? saved.Posed(skin) : target[grip.Body].Apply(grip.Local);
            var shift = (support - contact) / (grip.Surface?.RootShare ?? 1);
            if (!float.IsFinite(shift.LengthSquared())) return;
            for (int b = 0; b < target.Length; b++)
                if (carried[b] && (entering || pinned[b])) target[b] = target[b] with { Position = target[b].Position + shift };
        }
        finally { rig.Return(scratch); }
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
                var pool = new BepuUtilities.Memory.BufferPool();
                try
                {
                    BepuPhysics.Collidables.ConvexHullHelper.ComputeHull(points, pool, out var hull);
                    try
                    {
                        for (int f = 0; f < hull.FaceStartIndices.Length; f++)
                        {
                            hull.GetFace(f, out var face);
                            for (int i = 1; i + 1 < face.VertexCount; i++)
                            {
                                var a = points[face[0]]; var c = points[face[i]]; var d = points[face[i + 1]];
                                var bary = RagdollSkinSurface.ClosestBarycentric(wanted, a, c, d);
                                Candidate(a * bary.X + c * bary.Y + d * bary.Z);
                            }
                        }
                    }
                    finally { hull.Dispose(pool); }
                }
                finally { pool.Clear(); }
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
        hash.Add(grip.ReferenceBody); hash.Add(grip.ReferenceLocal); hash.Add(grip.OriginalSeat);
        HashSurface(grip.Surface, ref hash);
        HashSurface(grip.ReferenceSurface, ref hash);
        static void HashSurface(RagdollSkinSurface.Grip? surface, ref StateHasher hash)
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
}
