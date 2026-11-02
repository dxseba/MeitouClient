using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Simulation.Carrying;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    sealed record TorsoGrip(RagdollSkinSurface.Grip? Surface, int Body, Vector3 Local);

    Vector3? BicepsOf(CharacterTable table, CharacterId holder)
    {
        if (!table.TryResolveNext(holder, out int i)) return null;
        ref readonly var hot = ref table.Next[i];
        if (table.Cold(i) is not { Appearance: { } look } carrier || SetupOf(look) is not { } setup
            || setup.Rig.Handle("Bip01 L UpperArm") is not int shoulder || setup.Rig.Handle("Bip01 L Forearm") is not int elbow)
            return CarryPose.Fallback.World(hot.Position, hot.Yaw).Position;
        var scratch = setup.Rig.Rent();
        try
        {
            PoseDerived(scratch, setup, carrier);
            var placement = CharacterRigs.Placement(setup, hot.Position, hot.Yaw, carrier.Medical?.ScaleK ?? 1);
            var upperArm = Vector3.Transform(scratch.DerivedPosition[shoulder], placement);
            var forearm = Vector3.Transform(scratch.DerivedPosition[elbow], placement);
            return CarryPose.BicepsSupport(upperArm, forearm);
        }
        finally { setup.Rig.Return(scratch); }
    }
    void AlignTorsoGrip(TorsoFragments fragments, Ragdoll piece, CharacterCold cold, int side, RigidTransform[] target, bool[] carried, Vector3 palm, Matrix4x4 placement)
    {
        var anchors = side > 0 ? fragments.PositiveAnchors : fragments.NegativeAnchors;
        var offsets = side > 0 ? fragments.PositiveOffsets : fragments.NegativeOffsets;
        ref var grip = ref (side > 0 ? ref fragments.PositiveGrip : ref fragments.NegativeGrip);
        var rig = piece.Setup.Rig;
        var scratch = rig.Rent();
        try
        {
            var poseTargets = new RigidTransform[target.Length];
            for (int b = 0; b < target.Length; b++) poseTargets[b] = carried[b] ? target[b] : piece.Pose[b];
            var bones = BuildFragmentPose(piece, anchors, offsets, Matrix4x4.Identity, poseTargets);
            var layers = new List<ActiveLayer>();
            if (rig.Layer(rigs.Database, CarrySystem.CarriedClip, piece.Setup.Animal) is { } carry) layers.Add(new(carry, 0, 1));
            foreach (var (def, time) in piece.Setup.Fixed) layers.Add(new(def, time, 1));
            var skin = new Matrix4x4[rig.BoneCount];
            scratch.Pose(piece.Setup.Shape, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(layers), skin, bones);
            if (grip is null)
            {
                var limbCuts = Enumerable.Repeat(-1f, 4).ToArray();
                if (cold.Medical is { } medical)
                    foreach (var part in medical.Parts)
                        if (part.Limb == Bodies.LimbState.Stump && part.Template.LimbSlot is >= 0 and <= 3 and var limb)
                            limbCuts[limb] = Wounds.SeveredLevels.Height(Wounds.SeveredLevels.Of(piece.Id, limb), limb);
                var surface = cold.Appearance is { } look ? SkinSurfaceFor(look)?.FindGrip(look, rig, fragments.Cut.Plane, side, fragments.Cut.FullBody,
                    bones.Components!, bones.Components![piece.Definition.Bodies[piece.Definition.Root].Bone], piece.Definition, skin, palm, limbCuts, placement) : null;
                grip = surface is not null ? new(surface, -1, default) : ColliderGrip(piece, target, carried, palm, placement);
            }
            var contact = grip.Surface is { } saved ? saved.Posed(skin) : target[grip.Body].Apply(grip.Local);
            var shift = (palm - contact) / (grip.Surface?.RootShare ?? 1);
            if (!float.IsFinite(shift.LengthSquared())) return;
            for (int b = 0; b < target.Length; b++)
                if (carried[b]) target[b] = target[b] with { Position = target[b].Position + shift };
        }
        finally { rig.Return(scratch); }
    }

    static TorsoGrip ColliderGrip(Ragdoll piece, RigidTransform[] target, bool[] carried, Vector3 support, Matrix4x4 boundsFrame)
    {
        if (!Matrix4x4.Invert(boundsFrame, out var toBounds)) { boundsFrame = Matrix4x4.Identity; toBounds = Matrix4x4.Identity; }
        var bounds = new List<Vector3>();
        for (int b = 0; b < target.Length; b++)
            if (carried[b]) foreach (var p in RagdollCut.Points(piece.Definition.Bodies[b])) bounds.Add(Vector3.Transform(target[b].Apply(p), toBounds));
        if (bounds.Count == 0) return new(null, piece.Definition.Root, Vector3.Zero);
        var centre = Vector3.Transform((bounds.Aggregate(new Vector3(float.MaxValue), Vector3.Min) + bounds.Aggregate(new Vector3(float.MinValue), Vector3.Max)) * 0.5f, boundsFrame);
        int best = piece.Definition.Root;
        Vector3 local = default;
        float nearest = float.MaxValue, armDistance = float.MaxValue;
        for (int b = 0; b < target.Length; b++)
        {
            if (!carried[b]) continue;
            var body = piece.Definition.Bodies[b];
            var wanted = target[b].Inverse().Apply(centre);
            var arm = target[b].Inverse().Apply(support);
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
                if (direction.LengthSquared() < 1e-8f) direction = new(arm.X, 0, arm.Z);
                if (direction.LengthSquared() < 1e-8f) direction = Vector3.UnitX;
                Candidate(axis + Vector3.Normalize(direction) * body.Size.X);
            }
            void Candidate(Vector3 point)
            {
                var world = target[b].Apply(point);
                float distance = Vector3.DistanceSquared(world, centre), toArm = Vector3.DistanceSquared(world, support);
                if (distance > nearest + 1e-5f || MathF.Abs(distance - nearest) <= 1e-5f && toArm >= armDistance) return;
                nearest = distance; armDistance = toArm; best = b; local = point;
            }
        }
        return new(null, best, local);
    }
    static void HashTorsoGrip(TorsoGrip? grip, ref StateHasher hash)
    {
        if (grip is null) return;
        hash.Add(0x47726970);
        hash.Add(grip.Body);
        hash.Add(grip.Local);
        if (grip.Surface is not { } surface) return;
        hash.Add(surface.Barycentric);
        hash.Add(surface.RootShare);
        foreach (var point in new[] { surface.Triangle.A, surface.Triangle.B, surface.Triangle.C })
        {
            hash.Add(point.Position); hash.Add((int)point.Bones);
            hash.Add(point.Weights.X); hash.Add(point.Weights.Y); hash.Add(point.Weights.Z); hash.Add(point.Weights.W);
        }
    }}