using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Clipped_exterior_levels_reduce_triangles_without_reusing_unclipped_indices_or_losing_face_poses()
    {
        var source = DenseExterior();
        var pose = new OgrePose { Name = "head_size", Target = 1, IncludesNormals = true };
        var sourcePositions = source.SubMeshes[0].VertexData!.ReadPositions()!;
        for (uint i = 0; i < sourcePositions.Length; i++) pose.Vertices.Add(new(i, new(sourcePositions[i].X * .2f, 0, 0), Vector3.Zero));
        source.Poses.Add(pose);
        var weights = new Dictionary<string, float> { ["head_size"] = .5f };
        var plane = new Vector4(1, .17f, 0, -.14f);
        var rules = new[] { new CutHalfspace(plane, MorphedDistance: true) };
        var baseline = MeshCut.Clip(source, Rig, rules, weights, buildCaps: false);
        byte[] originalNearBytes = baseline.Skin.SubMeshes[0].VertexData!.Buffers[0].Data.ToArray();
        var cut = GeneratedExteriorLod.Clip(source, Rig, rules, weights);
        var levels = MeshLod.Levels(cut.Skin);
        Assert.True(levels.Count > 1);
        Assert.Equal(originalNearBytes, cut.Skin.SubMeshes[0].VertexData!.Buffers[0].Data.AsSpan(0, originalNearBytes.Length).ToArray());
        Assert.Equal(baseline.Skin.SubMeshes[0].Indices.Indices, levels[0].Indices[0]);
        Assert.True(levels[^1].Indices[0]!.Length < levels[0].Indices[0]!.Length * .85f);
        var vertices = Model.Build(cut.Skin, Rig.BoneCount).Parts[0].Vertices;
        var morph = Assert.Single(cut.Skin.Poses).Vertices.ToDictionary(point => point.Index, point => point.Offset);
        foreach (var level in levels)
        {
            Assert.NotEmpty(level.Indices[0]!);
            foreach (uint index in level.Indices[0]!)
            {
                Assert.InRange(index, 0u, (uint)vertices.Length - 1);
                var vertex = vertices[index];
                var posed = vertex.Position + morph.GetValueOrDefault(index) * .5f;
                Assert.True(Vector4.Dot(plane, new(posed, 1)) >= -.0002f);
                Assert.Equal(vertex.Position.X * .25f + .5f, vertex.Uv.X, .0001f);
                Assert.Equal(vertex.Position.X * .2f, morph.GetValueOrDefault(index).X, .0001f);
                Assert.Equal(1u, vertex.Bones); Assert.Equal(Vector4.UnitX, vertex.Weights);
            }
        }
        Assert.Equal(sourcePositions, source.SubMeshes[0].VertexData!.ReadPositions());
        Assert.Null(source.Lod);
    }

    [Fact]
    public void Different_cut_planes_reuse_the_original_exterior_lod_plan_and_keep_independent_valid_overrides()
    {
        var source = DenseExterior();
        var plan = GeneratedExteriorLod.LevelsFor(source, Rig);
        var left = GeneratedExteriorLod.Clip(source, Rig, [new(new(1, 0, 0, -.2f))]);
        var right = GeneratedExteriorLod.Clip(source, Rig, [new(new(-1, 0, 0, -.3f))]);
        Assert.Same(plan, GeneratedExteriorLod.LevelsFor(source, Rig));
        Assert.NotSame(left.Skin, right.Skin);
        foreach (var (mesh, side, station) in new[] { (left.Skin, 1, .2f), (right.Skin, -1, -.3f) })
        {
            var vertices = Model.Build(mesh, Rig.BoneCount).Parts[0].Vertices;
            Assert.All(MeshLod.Levels(mesh), level =>
                Assert.All(level.Indices[0]!, index => Assert.True((vertices[index].Position.X - station) * side >= -.0001f)));
        }
    }

    static OgreMesh DenseExterior()
    {
        var vertices = new List<Vertex>();
        const int width = 32, height = 20;
        Vector3 At(int x, int y) => new(-3 + x * 6f / width, 11 + y * 4f / height, 0);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            foreach (var point in new[] { At(x, y), At(x + 1, y), At(x + 1, y + 1), At(x, y), At(x + 1, y + 1), At(x, y + 1) })
                vertices.Add(Point(point, 1));
        return MeshCut.FromTriangles(vertices);
    }
}
