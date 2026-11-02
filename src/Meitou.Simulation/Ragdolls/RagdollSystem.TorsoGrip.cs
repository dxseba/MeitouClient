using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Simulation.Carrying;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    sealed record TorsoGrip(RagdollSkinSurface.Grip? Surface, int Body, Vector3 Local);

    Vector3? PalmOf(CharacterTable table, CharacterId holder)
    {
        if (!table.TryResolveNext(holder, out int i) || table.Cold(i) is not { Appearance: { } look } carrier
            || SetupOf(look) is not { } setup || setup.Rig.Handle("Bip01 L Hand") is not int hand) return null;
        var rig = setup.Rig;
        var scratch = rig.Rent();
        try
        {
            PoseDerived(scratch, setup, carrier);
            Vector3? Bone(string name) => rig.Handle(name) is { } h ? scratch.DerivedPosition[h] : null;
            var palm = CarryPose.PalmPoint(scratch.DerivedPosition[hand], Bone("Bip01 L Finger0") ?? Bone("Bip01 L Finger1"), Bone("Bip01 L Forearm"));
            ref readonly var hot = ref table.Next[i];
            return Vector3.Transform(palm, CharacterRigs.Placement(setup, hot.Position, hot.Yaw, carrier.Medical?.ScaleK ?? 1));
        }
        finally { rig.Return(scratch); }
    }

    void AlignTorsoGrip(TorsoFragments fragments, Ragdoll piece, CharacterCold cold, int side, RigidTransform[] target, bool[] carried, Vector3 palm)
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
                    bones.Components!, bones.Components![piece.Definition.Bodies[piece.Definition.Root].Bone], piece.Definition, skin, palm, limbCuts) : null;
                grip = surface is not null ? new(surface, -1, default) : ColliderGrip(piece, target, carried, palm);
            }
            var contact = grip.Surface is { } saved ? saved.Posed(skin) : target[grip.Body].Apply(grip.Local);
            var shift = (palm - contact) / (grip.Surface?.RootShare ?? 1);
            if (!float.IsFinite(shift.LengthSquared())) return;
            for (int b = 0; b < target.Length; b++)
                if (carried[b]) target[b] = target[b] with { Position = target[b].Position + shift };
        }
        finally { rig.Return(scratch); }
    }

    static TorsoGrip ColliderGrip(Ragdoll piece, RigidTransform[] target, bool[] carried, Vector3 palm)
    {
        var candidates = new List<(int Body, Vector3 Local, Vector3 World)>();
        for (int b = 0; b < target.Length; b++)
            if (carried[b])
                foreach (var point in RagdollCut.Points(piece.Definition.Bodies[b])) candidates.Add((b, point, target[b].Apply(point)));
        if (candidates.Count == 0) return new(null, piece.Definition.Root, Vector3.Zero);
        float upper = CarryPose.UpperGripBand(candidates.Min(p => p.World.Y), candidates.Max(p => p.World.Y));
        var selected = candidates.Where(p => p.World.Y >= upper - 1e-5f).MinBy(p => Vector3.DistanceSquared(p.World, palm));
        return new(null, selected.Body, selected.Local);
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