using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>Exact layered sections from the pinned torso builder, with its single-segment limb anchoring.</summary>
internal static class NativeCapGeometry
{
    internal const int VertexCapacity = 32768;
    public static OgreMesh? Build(Vertex[] source, SkeletonRig rig, NativeMeshMorphs? morphs,
        CharacterAppearance appearance, IReadOnlyList<CutHalfspace> rules, int[]? components, uint blood, bool mechanical, string? skeletonName = null, Vertex[]? brainSource = null, Vertex[]? headSource = null)
    {
        if (rules.Count == 0) return null;
        var groupOfBone = MeshCut.BoneGroups(rig);
        var groups = new uint[(rig.BoneCount + 7) / 8];
        for (int bone = 0; bone < groupOfBone.Length; bone++) groups[bone / 8] |= (uint)groupOfBone[bone] << (bone % 8 * 4);
        var delta = morphs?.Deltas(appearance.PoseWeights, appearance.Character?.GetBool("shaved") == true);
        var result = new List<Vertex>();
        foreach (var rule in rules)
        {
            bool limb = rule.Group is >= 1 and <= 4;
            Vertex[] local = rule.Group == 7 ? headSource ?? SelectHeadSkin(source, rig) : source;
            if (limb)
            {
                var filtered = new List<Vertex>();
                for (int i = 0; i + 2 < source.Length; i += 3)
                    if (NativeSectionGeometry.Group(source[i], groups) == rule.Group
                        && NativeSectionGeometry.Group(source[i + 1], groups) == rule.Group
                        && NativeSectionGeometry.Group(source[i + 2], groups) == rule.Group)
                    { filtered.Add(source[i]); filtered.Add(source[i + 1]); filtered.Add(source[i + 2]); }
                local = filtered.ToArray();
            }
            var cut = new NativeSectionCut(rule.Plane, rule.KeepSide, FullBody: !rule.LegacyTorso, HeadOnly: rule.Group == 7);
            var section = NativeSectionGeometry.Build(local, groups, cut, delta, components, blood, mechanical: mechanical);
            if (!mechanical && !limb) section = WithBrainSection(section, brainSource, groups, cut, components, blood);
            if (section is null) continue;
            if (limb)
            {
                // Exact LimbCutSurface cap ownership: all vertices follow the bone containing the actual cut station.
                int anchor = MeshCut.SectionBone(rig, rule);
                if (section.Length + result.Count > 16384) continue;
                foreach (var vertex in section)
                    result.Add(vertex with { Bones = anchor < 0 ? vertex.Bones : (uint)anchor,
                        Weights = anchor < 0 ? vertex.Weights : Vector4.UnitX,
                        Tangent = new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z, 35) });
            }
            else result.AddRange(section);
        }
        return result.Count == 0 ? null : MeshCut.FromTriangles(result, skeletonName, preserveRawVertexChannels: true);
    }

    /// <summary>The pin's HeadCutSurface.Skin: a triangle belongs to the head when any corner has at least half its weight on Head/Jaw descendants.</summary>
    internal static Vertex[] SelectHeadSkin(Vertex[] source, SkeletonRig rig)
    {
        int head = rig.Handle("Bip01 Head") ?? -1, jaw = rig.Handle("Bip01 Jaw") ?? -1;
        ulong mask = 0;
        for (int bone = 0; bone < Math.Min(rig.BoneCount, 64); bone++)
            for (int parent = bone; parent >= 0 && parent < rig.BoneCount; parent = rig.Parent[parent])
                if (parent == head || parent == jaw) { mask |= 1UL << bone; break; }
        bool HeadVertex(Vertex vertex)
        {
            float weight = 0;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(vertex.Bones >> (8 * k) & 255);
                if (bone < 64 && (mask & (1UL << bone)) != 0) weight += vertex.Weights[k];
            }
            return weight >= .5f;
        }
        var selected = new List<Vertex>();
        for (int i = 0; i + 2 < source.Length; i += 3)
            if (HeadVertex(source[i]) || HeadVertex(source[i + 1]) || HeadVertex(source[i + 2]))
            { selected.Add(source[i]); selected.Add(source[i + 1]); selected.Add(source[i + 2]); }
        return selected.ToArray();
    }

    /// <summary>The pinned cap pool closes only the fitted brain's actual footprint; it never substitutes a skull-sized disc.</summary>
    internal static Vertex[]? WithBrainSection(Vertex[]? flesh, Vertex[]? brain, uint[] groups, in NativeSectionCut cut,
        int[]? components, uint bloodColour)
    {
        if (!cut.FullBody && !cut.HeadOnly || brain is not { Length: > 0 }) return flesh;
        var section = NativeSectionGeometry.Build(brain, groups, cut, components: components, bloodColour: bloodColour, brainSection: true);
        if (section is not { Length: > 0 } || (flesh?.Length ?? 0) + section.Length > VertexCapacity) return flesh;
        return [.. flesh ?? [], .. section];
    }
}
