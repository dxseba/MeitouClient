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
        internal SurfaceContact? Retained;
        public Vector3 SupportPoint { get; internal set; }
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

    /// <summary>Preserves the ordinary carry palette and rotation, translating the retained half's central material onto the real upper arm.</summary>
    public bool PrepareTorsoCarryReference(in CharacterInstance reference, in CharacterTorsoCut cut, Vector3 support,
        ref TorsoCarryReference? cached, out BonePose? pose, out Vector3 translation)
    {
        pose = null; translation = default;
        if (!assets.TryGetValue(reference.Appearance, out var asset) || asset is not { BodySource: not null, IsIcon: false }
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
        var sampled = new CharState { Asset = asset, Bones = context.Skin, World = world, Posed = updateStamp, InWorld = true,
            LimbPlanes = LimbCutPlanes.Resolve(rig, reference.LimbSeverances), LimbCuts = reference.LimbCuts, PartOnly = reference.PartOnly };
        context.Retained ??= CentralCarryContact(sampled, cut, support, reference.LimbCuts, null, out _);
        if (context.Retained is null) return false;
        context.SupportPoint = support;
        context.RetainedPoint = CarryMaterialPosition(asset, sampled, context.Retained!.Value);
        context.Translation = support - context.RetainedPoint;
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

}
