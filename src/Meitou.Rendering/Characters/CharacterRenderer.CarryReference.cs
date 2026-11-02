using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Characters;

namespace Meitou.Rendering.Characters;

internal sealed partial class CharacterRenderer
{
    public sealed class FrozenTorsoPose
    {
        internal Matrix4x4[] Bones = [];
    }

    public sealed class TorsoCarryReference
    {
        internal CharacterAsset Asset = null!;
        internal Matrix4x4[] Skin = [];
        internal BonePose Pose = null!;
        internal SurfaceContact? Original, Retained;
        internal bool Preserve;
        public Vector3 ReferencePoint { get; internal set; }
        public Vector3 RetainedPoint { get; internal set; }
        public Vector3 Translation { get; internal set; }
    }

    public FrozenTorsoPose? CaptureFrozenTorsoPose(long key) => states.TryGetValue(key, out var state) && state.Posed >= 0
        ? new FrozenTorsoPose { Bones = [.. state.Bones] } : null;

    public bool RestoreFrozenTorsoPose(long key, FrozenTorsoPose original)
    {
        if (!states.TryGetValue(key, out var state) || state.Asset is not { } asset || original.Bones.Length != asset.Rig.BoneCount) return false;
        state.Bones = [.. original.Bones]; state.Previous = [.. original.Bones];
        state.Attach = new Matrix4x4[asset.AttachCount];
        foreach (var part in asset.Parts)
            if (part.AttachIndex >= 0) state.Attach[part.AttachIndex] = FrozenTag(asset.Rig, state.Bones, part.Bone, part.Offset);
        foreach (var lamp in asset.Lamps) state.Attach[lamp.AttachIndex] = FrozenTag(asset.Rig, state.Bones, lamp.Bone, lamp.Offset);
        state.PreviousAttach = [.. state.Attach];
        state.Frozen = true; state.PosedRagdoll = null; state.Posed = updateStamp;
        return true;
    }

    /// <summary>Samples the ordinary intact carry palette, clips that same pose, and changes only position when its original hand-contact material was removed.</summary>
    public bool PrepareTorsoCarryReference(in CharacterInstance reference, in CharacterTorsoCut cut, Vector3 palm,
        ref TorsoCarryReference? cached, out BonePose? pose, out Vector3 translation)
    {
        pose = null; translation = default;
        if (!assets.TryGetValue(reference.Appearance, out var asset) || asset is not { BodySource: { } source, IsIcon: false }
            || reference.World is not { } world) return false;
        if (reference.Build is { } build) ApplyBuild(asset, build);
        var rig = asset.Rig;
        if (cached is null || !ReferenceEquals(cached.Asset, asset))
            cached = new TorsoCarryReference { Asset = asset, Skin = new Matrix4x4[rig.BoneCount], Pose = new(rig.BoneCount) { AbsolutePositions = true } };
        var context = cached;
        var layers = new List<ActiveLayer>();
        foreach (var item in reference.Poses)
            if (rig.Layer(content.Database, item.Animation, asset.Animal) is { } layer) layers.Add(new(layer, item.Time, item.Weight));
        foreach (var (layer, time) in asset.Fixed) layers.Add(new(layer, time, 1));
        var scratch = rig.Rent();
        try
        {
            scratch.Pose(asset.Shape, CollectionsMarshal.AsSpan(layers), context.Skin, reference.Bones);
            for (int h = 0; h < rig.BoneCount; h++)
            {
                context.Pose.Set[h] = true;
                context.Pose.Position[h] = scratch.DerivedPosition[h];
                context.Pose.Rotation[h] = scratch.DerivedRotation[h];
            }
        }
        finally { rig.Return(scratch); }
        var sampled = new CharState { Asset = asset, Bones = context.Skin, World = world, Posed = updateStamp, InWorld = true };
        if (context.Original is null)
        {
            var uncut = new CharacterTorsoCut(new(0, 1, 0, 100000), 1, true);
            context.Original = ClosestTorsoContact(sampled, uncut, palm, limbCuts: reference.LimbCuts, axialOnly: true)
                ?? ClosestTorsoContact(sampled, uncut, palm, limbCuts: reference.LimbCuts);
            if (context.Original is not { } original) return false;
            context.Preserve = CarryMaterialSurvives(asset, source, original, cut, reference.LimbCuts);
            var target = CarryMaterialPosition(asset, sampled, original);
            context.Retained = context.Preserve ? original : ClosestTorsoContact(sampled, cut, target, limbCuts: reference.LimbCuts, axialOnly: true)
                ?? ClosestTorsoContact(sampled, cut, target, limbCuts: reference.LimbCuts);
            if (context.Retained is null) { context.Original = null; return false; }
        }
        context.ReferencePoint = CarryMaterialPosition(asset, sampled, context.Original!.Value);
        context.RetainedPoint = CarryMaterialPosition(asset, sampled, context.Retained!.Value);
        context.Translation = context.Preserve ? Vector3.Zero : context.ReferencePoint - context.RetainedPoint;
        pose = context.Pose; translation = context.Translation;
        return true;
    }

    Vector3 CarryMaterialPosition(CharacterAsset asset, CharState state, SurfaceContact material)
    {
        var morphs = asset.Morph?.Poses.Deltas(asset.Morph.Weights, asset.Morph.CutHorns);
        var point = Vector3.Zero;
        for (int i = 0; i < 3; i++)
        {
            var vertex = asset.BodySource![material.Triangle + i];
            uint slot = BitConverter.SingleToUInt32Bits(vertex.Colour.X);
            if (morphs is not null && slot > 0 && slot <= morphs.Length)
            {
                var delta = morphs[slot - 1];
                vertex = vertex with { Position = vertex.Position + new Vector3(delta.X, delta.Y, delta.Z) };
            }
            point += PoseCutVertex(vertex, state) * material.Barycentric[i];
        }
        return point;
    }

    bool CarryMaterialSurvives(CharacterAsset asset, Vertex[] source, SurfaceContact material, in CharacterTorsoCut cut, uint limbCuts)
    {
        var groups = woundMasks.BoneGroups(asset.Rig);
        float torso = 0, limb = 0;
        for (int i = 0; i < 3; i++)
        {
            var vertex = source[material.Triangle + i];
            int group = TorsoCutSurface.Group(vertex, groups);
            uint height = group is >= 1 and <= 4 ? limbCuts >> (8 * (group - 1)) & 255 : 0;
            torso += material.Barycentric[i] * TorsoCutSurface.Band(vertex.Position, group, cut);
            limb += material.Barycentric[i] * (height == 0 ? 1000 : vertex.Position.Y - height * 0.1f);
        }
        return torso >= -1e-5f && limb >= -1e-5f;
    }
}
