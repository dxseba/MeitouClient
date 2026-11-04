using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Brutality.Rendering;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Cached_source_skinning_keeps_cut_uvs_weights_and_sparse_pose_bytes_identical_to_a_fresh_decode()
    {
        var source = PosedMixedExterior();
        var rules = new[] { new CutHalfspace(new(1, .17f, 0, -.14f), MorphedDistance: true) };
        var weights = new Dictionary<string, float> { ["head_size"] = .5f };
        var fresh = Model.Build(source, Rig.BoneCount, gameBinormals: true);
        var expected = MeshCut.Clip(source, Rig, rules, weights, preparedModel: fresh);
        var actual = MeshCut.Clip(source, Rig, rules, weights);
        AssertCutPayload(expected, actual);
        Assert.NotEmpty(actual.Skin.Poses);
        var vertices = Model.Build(actual.Skin, Rig.BoneCount).Parts.Single().Vertices;
        Assert.All(vertices, vertex => Assert.InRange(vertex.Weights.X + vertex.Weights.Y + vertex.Weights.Z + vertex.Weights.W, .99999f, 1.00001f));
        Assert.Contains(vertices, vertex => vertex.Weights.Y > 0);
    }

    [Fact]
    public void Lower_levels_share_decoded_vertices_and_preserve_shared_and_private_pose_targets_without_cloning_assignments()
    {
        var original = PosedMixedExterior();
        var part = original.SubMeshes[0];
        var sharedVertices = part.VertexData!;
        original.SharedVertexData = sharedVertices;
        original.BoneAssignments.AddRange(part.BoneAssignments);
        part.UseSharedVertices = true; part.VertexData = null; part.BoneAssignments.Clear();
        original.Poses[0].Target = 0;
        var privatePart = new OgreSubMesh { MaterialName = "private", VertexData = sharedVertices, Indices = part.Indices };
        privatePart.BoneAssignments.AddRange(original.BoneAssignments);
        privatePart.TextureAliases.Add("diffuse", "private-texture");
        original.SubMeshes.Add(privatePart);
        var privatePose = new OgrePose { Name = "head_size", Target = 2, IncludesNormals = true };
        privatePose.Vertices.AddRange(original.Poses[0].Vertices);
        original.Poses.Add(privatePose);
        var indices = part.Indices.Indices.Where((_, index) => index / 3 % 2 == 0).ToArray();
        uint[]?[] overrides = [indices, indices.Reverse().Chunk(3).SelectMany(triangle => triangle.Reverse()).ToArray()];
        var reference = IndexedReference(original, overrides);
        var model = MeshCut.SourceModel(original, Rig.BoneCount);
        var before = model.Parts.Select(decoded => MemoryMarshal.AsBytes(decoded.Vertices.AsSpan()).ToArray()).ToArray();
        var rules = new[] { new CutHalfspace(new(1, .17f, 0, -.14f), MorphedDistance: true) };
        var weights = new Dictionary<string, float> { ["head_size"] = .5f };
        var expected = MeshCut.Clip(reference, Rig, rules, weights, buildCaps: false,
            preparedModel: Model.Build(reference, Rig.BoneCount, gameBinormals: true));
        var actual = MeshCut.Clip(original, Rig, rules, weights, buildCaps: false, preparedModel: model, indexOverrides: overrides);
        AssertCutPayload(expected, actual);
        for (int p = 0; p < model.Parts.Count; p++)
            Assert.Equal(before[p], MemoryMarshal.AsBytes(model.Parts[p].Vertices.AsSpan()).ToArray());
        Assert.Same(model, MeshCut.SourceModel(original, Rig.BoneCount));
        Assert.Equal((uint)before[0].Length / (uint)Vertex.Size, sharedVertices.VertexCount);
        Assert.Equal(2, actual.Skin.Poses.Count);
    }

    [Fact]
    public async Task Concurrent_warm_and_cut_requests_reuse_one_immutable_model_per_original_mesh_and_bone_count()
    {
        var source = DenseExterior();
        var cancellation = TestContext.Current.CancellationToken;
        using var start = new ManualResetEventSlim();
        var requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() => { start.Wait(cancellation); return MeshCut.SourceModel(source, Rig.BoneCount); }, cancellation)).ToArray();
        start.Set();
        var models = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        Assert.All(models, model => Assert.Same(models[0], model));
        var vertices = models[0].Parts.Single().Vertices;
        byte[] snapshot = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray();
        GeneratedExteriorLod.LevelsFor(source, Rig);
        _ = GeneratedExteriorLod.Clip(source, Rig, [new(new(1, 0, 0, -.2f))]);
        _ = GeneratedExteriorLod.Clip(source, Rig, [new(new(-1, 0, 0, -.3f))]);
        Assert.Same(vertices, MeshCut.SourceModel(source, Rig.BoneCount).Parts.Single().Vertices);
        Assert.Equal(snapshot, MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray());
        Assert.NotSame(models[0], MeshCut.SourceModel(source, Rig.BoneCount - 1));
    }

    [Fact]
    public void Repeated_source_model_requests_do_not_allocate_another_vertex_array_or_bone_sorting_pipeline()
    {
        var source = PosedMixedExterior();
        var model = MeshCut.SourceModel(source, Rig.BoneCount);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++) _ = MeshCut.SourceModel(source, Rig.BoneCount);
        long reused = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = Model.Build(source, Rig.BoneCount, gameBinormals: true);
        long fresh = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(decoded);
        Assert.InRange(reused, 0, 4096);
        Assert.True(fresh > reused + model.Parts.Single().Vertices.Length * Vertex.Size,
            $"Repeated shared-model requests allocated {reused:N0} bytes versus {fresh:N0} for one repeated source decode.");
    }

    static OgreMesh PosedMixedExterior()
    {
        var source = DenseExterior();
        var part = source.SubMeshes[0]; part.BoneAssignments.Clear();
        var positions = part.VertexData!.ReadPositions()!;
        var pose = new OgrePose { Name = "head_size", Target = 1, IncludesNormals = true };
        for (uint i = 0; i < positions.Length; i++)
        {
            part.BoneAssignments.Add(new(i, 1, .6f)); part.BoneAssignments.Add(new(i, 2, .4f));
            pose.Vertices.Add(new(i, new(positions[i].X * .2f, 0, 0), new(0, .02f, 0)));
        }
        part.TextureAliases.Add("diffuse", "source-texture");
        source.Poses.Add(pose);
        return source;
    }

    // The former lower-level route: same Ogre vertex data and poses, copied assignment lists, then a fresh skinning decode.
    static OgreMesh IndexedReference(OgreMesh source, IReadOnlyList<uint[]?> indices)
    {
        var mesh = new OgreMesh { Version = source.Version, SkeletonName = source.SkeletonName,
            SkeletallyAnimated = source.SkeletallyAnimated, SharedVertexData = source.SharedVertexData, Bounds = source.Bounds };
        mesh.BoneAssignments.AddRange(source.BoneAssignments); mesh.Poses.AddRange(source.Poses);
        for (int i = 0; i < source.SubMeshes.Count; i++)
        {
            var original = source.SubMeshes[i];
            var part = new OgreSubMesh { MaterialName = original.MaterialName, UseSharedVertices = original.UseSharedVertices,
                VertexData = original.VertexData, Operation = OgreOperationType.TriangleList, Indices = new(true, indices[i] ?? original.Indices.Indices) };
            part.BoneAssignments.AddRange(original.BoneAssignments);
            foreach (var pair in original.TextureAliases) part.TextureAliases.Add(pair.Key, pair.Value);
            mesh.SubMeshes.Add(part);
        }
        return mesh;
    }

    static void AssertCutPayload(CutMeshResult expected, CutMeshResult actual)
    {
        Assert.Equal(expected.HasTriangles, actual.HasTriangles); Assert.Equal(expected.Min, actual.Min); Assert.Equal(expected.Max, actual.Max);
        AssertMeshPayload(expected.Skin, actual.Skin);
        if (expected.Wound is null) Assert.Null(actual.Wound);
        else AssertMeshPayload(expected.Wound, Assert.IsType<OgreMesh>(actual.Wound));
    }
    static void AssertMeshPayload(OgreMesh expected, OgreMesh actual)
    {
        Assert.Equal(expected.SubMeshes.Count, actual.SubMeshes.Count);
        for (int i = 0; i < expected.SubMeshes.Count; i++)
        {
            var a = expected.SubMeshes[i]; var b = actual.SubMeshes[i];
            Assert.Equal(a.MaterialName, b.MaterialName); Assert.Equal(a.Indices.Indices, b.Indices.Indices);
            Assert.Equal(a.VertexData!.Buffers[0].Data, b.VertexData!.Buffers[0].Data);
            Assert.Equal(a.BoneAssignments, b.BoneAssignments); Assert.Equal(a.TextureAliases, b.TextureAliases);
        }
        Assert.Equal(expected.Poses.Count, actual.Poses.Count);
        for (int i = 0; i < expected.Poses.Count; i++)
        {
            var a = expected.Poses[i]; var b = actual.Poses[i];
            Assert.Equal(a.Name, b.Name); Assert.Equal(a.Target, b.Target); Assert.Equal(a.IncludesNormals, b.IncludesNormals);
            Assert.Equal(a.Vertices, b.Vertices);
        }
    }
}
