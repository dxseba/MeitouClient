using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>The pin's rigid-batch head/body triangle selection, independent of cut planes. Compact views adapt its shared-buffer regions to Ogre overrides.</summary>
internal static class NativeAnatomyRegions
{
    sealed record Views(OgreMesh? Body, OgreMesh? Head);
    sealed class Rigs { public readonly ConditionalWeakTable<SkeletonRig, Views> Items = new(); }
    static readonly ConditionalWeakTable<OgreMesh, Rigs> regions = new();

    public static OgreMesh? Select(OgreMesh source, SkeletonRig rig, bool headOnly) =>
        headOnly ? ViewsFor(source, rig).Head : ViewsFor(source, rig).Body;

    static Views ViewsFor(OgreMesh source, SkeletonRig rig) => regions.GetValue(source, static _ => new()).Items.GetValue(rig, r => Build(source, r));
    static Views Build(OgreMesh source, SkeletonRig rig)
    {
        int head = rig.Handle("Bip01 Head") ?? -1, jaw = rig.Handle("Bip01 Jaw") ?? -1;
        var model = Model.Build(source, rig.BoneCount, preserveRawVertexChannels: true);
        // Fitted anatomy follows one rigid bone per triangle, exactly as AnatomyMeshRegions in the pinned renderer.
        bool Head(Vertex vertex) => (int)(vertex.Bones & 255) is var bone && (bone == head || bone == jaw);
        OgreMesh? View(bool onlyHead)
        {
            var vertices = new List<Vertex>();
            foreach (var part in model.Parts)
                for (int i = 0; i + 2 < part.Indices.Length; i += 3)
                    if (Head(part.Vertices[part.Indices[i]]) == onlyHead)
                        for (int k = 0; k < 3; k++) vertices.Add(part.Vertices[part.Indices[i + k]]);
            if (vertices.Count == 0) return null;
            var mesh = MeshCut.FromTriangles(vertices, source.SkeletonName, preserveRawVertexChannels: true);
            return AnatomyDetail.WithLevels(mesh, rig, [], preserveRawVertexChannels: true);
        }
        return new(View(false), View(true));
    }
}
