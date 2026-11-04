using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.World;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering;

/// <summary>Reuse the native simplification of each original exterior, then clip every level exactly. Never simplify anew for every wound.</summary>
internal static class GeneratedExteriorLod
{
    static readonly FoliageLodBuilder.Settings Settings = new([.5f, .25f, .12f, .05f], 40, .25f, 3.2f, .85f, true);
    static readonly float Projection = TerrainLod.ProjectionScale(1080, 50 * MathF.PI / 180);
    sealed class Plans { public readonly Dictionary<(int Bones, float Scale), IReadOnlyList<MeshLodLevel>> Values = []; }
    static readonly ConditionalWeakTable<OgreMesh, Plans> plans = new();

    internal static IReadOnlyList<MeshLodLevel> LevelsFor(OgreMesh source, SkeletonRig rig, float lodScale = 1)
    {
        var cache = plans.GetValue(source, static _ => new());
        lock (cache)
        {
            var key = (rig.BoneCount, lodScale);
            if (cache.Values.TryGetValue(key, out var ready)) return ready;
            var model = MeshCut.SourceModel(source, rig.BoneCount);
            var parts = model.Parts.Where(part => part.Indices.Length > 0).ToArray();
            var levels = new List<MeshLodLevel> { new(0, source.SubMeshes.Select(sub => (uint[]?)sub.Indices.Indices).ToArray()) };
            float radius = Math.Min(source.Bounds?.Radius ?? model.Radius, Math.Max(model.Radius, 1));
            if (parts.Length > 0 && FoliageLodBuilder.Build(model, radius, Settings) is { } set)
            {
                var distances = ObjectLodGen.Distances(set, ObjectLodGen.TriangleExtent(parts), radius, Projection * lodScale);
                for (int level = 1; level < set.Levels; level++)
                {
                    var indices = new uint[]?[source.SubMeshes.Count];
                    for (int part = 0; part < parts.Length; part++) indices[parts[part].SubMeshIndex] = set.Indices[part][level - 1];
                    levels.Add(new(distances[level], indices));
                }
            }
            return cache.Values[key] = levels.AsReadOnly();
        }
    }

    internal static CutMeshResult Clip(OgreMesh source, SkeletonRig rig, IReadOnlyList<CutHalfspace> cuts,
        IReadOnlyDictionary<string, float>? poseWeights = null, bool cutHorns = false, Vector4? capColour = null,
        int[]? components = null, float lodScale = 1)
    {
        var model = MeshCut.SourceModel(source, rig.BoneCount);
        var full = MeshCut.Clip(source, rig, cuts, poseWeights, cutHorns, capColour, components: components, buildCaps: false, preparedModel: model);
        if (!full.HasTriangles || cuts.Count == 0 || LevelsFor(source, rig, lodScale) is not { Count: > 1 } levels) return full;
        var meshes = new List<OgreMesh> { full.Skin };
        for (int level = 1; level < levels.Count; level++)
        {
            meshes.Add(MeshCut.Clip(source, rig, cuts, poseWeights, cutHorns, capColour, components: components, buildCaps: false,
                preparedModel: model, indexOverrides: levels[level].Indices).Skin);
        }
        return full with { Skin = Merge(meshes, levels) };
    }

    static OgreMesh Merge(IReadOnlyList<OgreMesh> sources, IReadOnlyList<MeshLodLevel> levels)
    {
        var first = sources[0];
        var mesh = new OgreMesh { Version = first.Version, SkeletonName = first.SkeletonName, SkeletallyAnimated = first.SkeletallyAnimated,
            Bounds = first.Bounds, Lod = new() { Strategy = "distance_sphere" } };
        for (int level = 1; level < levels.Count; level++) mesh.Lod.Levels.Add(new() { UserValue = levels[level].Distance });
        for (int part = 0; part < first.SubMeshes.Count; part++)
        {
            var original = first.SubMeshes[part];
            var data = new OgreVertexData(); data.Elements.AddRange(original.VertexData!.Elements);
            var vertices = new List<byte>();
            var sub = new OgreSubMesh { MaterialName = original.MaterialName, VertexData = data,
                Operation = OgreOperationType.TriangleList, Indices = original.Indices };
            var poses = new Dictionary<string, OgrePose>(StringComparer.Ordinal);
            uint vertexBase = 0;
            for (int level = 0; level < sources.Count; level++)
            {
                var current = sources[level].SubMeshes[part]; var currentData = current.VertexData!;
                vertices.AddRange(currentData.Buffers[0].Data);
                foreach (var bone in current.BoneAssignments) sub.BoneAssignments.Add(bone with { VertexIndex = bone.VertexIndex + vertexBase });
                if (level > 0)
                {
                    var indices = current.Indices.Indices.Select(index => index + vertexBase).ToArray();
                    mesh.Lod.Levels[level - 1].Faces.Add(new((uint)indices.Length, 0, null, new(true, indices)));
                }
                foreach (var sourcePose in sources[level].Poses.Where(pose => pose.Target == part + 1))
                {
                    if (!poses.TryGetValue(sourcePose.Name, out var output))
                        poses.Add(sourcePose.Name, output = new() { Name = sourcePose.Name, Target = checked((ushort)(part + 1)), IncludesNormals = sourcePose.IncludesNormals });
                    foreach (var point in sourcePose.Vertices) output.Vertices.Add(point with { Index = point.Index + vertexBase });
                }
                vertexBase += currentData.VertexCount;
            }
            data.VertexCount = vertexBase;
            data.Buffers.Add(0, new(original.VertexData.Buffers[0].VertexSize, vertices.ToArray()));
            foreach (var (key, value) in original.TextureAliases) sub.TextureAliases.Add(key, value);
            mesh.SubMeshes.Add(sub); mesh.Poses.AddRange(poses.Values);
        }
        return mesh;
    }
}
