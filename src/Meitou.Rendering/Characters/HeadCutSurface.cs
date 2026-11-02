using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meitou.Rendering.Characters;

/// <summary>Head-only skin, bounds and bone masks; ordinary torso/limb wound groups stay unchanged.</summary>
static class HeadCutSurface
{
    sealed record Source(Vertex[] Skin, Vector3 Centre, Vector3 Half, ulong Mask, uint[] Groups);
    static readonly ConditionalWeakTable<CharacterAsset, Source> sources = new();

    static Source Of(CharacterAsset asset) => sources.GetValue(asset, BuildSource);
    public static Vertex[] Skin(CharacterAsset asset) => Of(asset).Skin;
    public static uint[] Groups(CharacterAsset asset) => Of(asset).Groups;

    public static int Group(CharacterAsset asset, int bone)
    {
        var source = Of(asset);
        return bone >= 0 && bone < 64 && (source.Mask & (1UL << bone)) != 0 ? 7 : 0;
    }

    /// <summary>The saved plane uses the head's own box, so person races and face proportions share no fixed cut height.</summary>
    public static CharacterTorsoCut Resolve(CharacterAsset asset, in CharacterTorsoCut cut)
    {
        var source = Of(asset);
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z) / source.Half;
        float length = normal.Length();
        return cut with { Plane = new Vector4(normal, cut.Plane.W - Vector3.Dot(normal, source.Centre)) / length };
    }

    public static Matrix4x4 Pack(CharacterAsset asset, in CharacterTorsoCut cut)
    {
        var frame = TorsoCutSurface.Packed(cut);
        ulong mask = Of(asset).Mask;
        frame.M31 = BitConverter.UInt32BitsToSingle((uint)mask);
        frame.M32 = BitConverter.UInt32BitsToSingle((uint)(mask >> 32));
        return frame;
    }

    static Source BuildSource(CharacterAsset asset)
    {
        var rig = asset.Rig;
        int head = rig.Handle("Bip01 Head") ?? -1, jaw = rig.Handle("Bip01 Jaw") ?? -1;
        ulong mask = 0;
        var groups = new uint[8];
        for (int bone = 0; bone < Math.Min(rig.BoneCount, 64); bone++)
        {
            bool inHead = false;
            for (int parent = bone; parent >= 0 && parent < rig.BoneCount; parent = rig.Parent[parent])
                if (parent == head || parent == jaw) { inHead = true; break; }
            if (inHead) mask |= 1UL << bone;
            groups[bone / 8] |= (uint)(inHead ? bone == jaw ? 6 : 7 : 0) << (bone % 8 * 4);
        }

        var raw = asset.HeadSource ?? [];
        var skin = new List<Vertex>();
        bool HeadVertex(Vertex vertex)
        {
            float weight = 0;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(vertex.Bones >> (8 * k) & 255);
                if (bone < 64 && (mask & (1UL << bone)) != 0) weight += vertex.Weights[k];
            }
            return weight >= 0.5f;
        }
        for (int i = 0; i + 2 < raw.Length; i += 3)
            if (HeadVertex(raw[i]) || HeadVertex(raw[i + 1]) || HeadVertex(raw[i + 2]))
                skin.AddRange([raw[i], raw[i + 1], raw[i + 2]]);

        if (!HeadWoundSurface.TryAnatomyBounds(raw, head, jaw, asset.Morph?.Poses, out var min, out var max))
        {
            var centre = head >= 0 ? rig.BindPosition[head] : new Vector3(0, asset.Height * 0.9f, 0);
            min = centre - new Vector3(1, 1.5f, 1);
            max = centre + new Vector3(1, 1.5f, 1);
        }
        return new(skin.ToArray(), (min + max) * 0.5f, Vector3.Max((max - min) * 0.5f, new Vector3(0.01f)), mask, groups);
    }
}
