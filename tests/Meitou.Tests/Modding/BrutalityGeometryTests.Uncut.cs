using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Theory]
    [InlineData(.2f)]
    [InlineData(.8f)]
    public void Uncut_shader_wounds_keep_original_vertex_and_pose_data_with_the_exact_current_face_morph_bounds(float weight)
    {
        var source = PosedMixedExterior();
        var originalData = source.SubMeshes[0].VertexData!;
        byte[] originalBytes = originalData.Buffers[0].Data.ToArray();
        var originalAssignments = source.SubMeshes[0].BoneAssignments.ToArray();
        var originalPose = source.Poses[0].Vertices.ToArray();
        var weights = new Dictionary<string, float> { ["head_size"] = weight };
        var reference = MeshCut.Clip(source, Rig, [], weights, buildCaps: false);
        var result = GeneratedExteriorLod.Clip(source, Rig, [], weights);
        Assert.Same(source, result.Skin);
        Assert.Same(originalData, result.Skin.SubMeshes[0].VertexData);
        Assert.Equal(reference.Min, result.Min); Assert.Equal(reference.Max, result.Max);
        Assert.Equal(reference.HasTriangles, result.HasTriangles); Assert.Null(result.Wound);
        Assert.Equal(-3 * (1 + .2f * weight), result.Min.X, .00001f);
        Assert.Equal(3 * (1 + .2f * weight), result.Max.X, .00001f);
        Assert.Equal(originalBytes, originalData.Buffers[0].Data);
        Assert.Equal(originalAssignments, source.SubMeshes[0].BoneAssignments);
        Assert.Equal(originalPose, source.Poses[0].Vertices);
        Assert.Null(source.Lod);
    }

    [Fact]
    public void Ordinary_mark_changes_keep_native_body_and_clothing_meshes_and_reuse_the_same_presentation_geometry()
    {
        using var scene = new Scene();
        var cloth = PosedMixedExterior();
        var part = new CharacterPart { Record = scene.Appearance.Race, Mesh = "shirt.mesh", Field = "shirt", Mode = AttachMode.SharedSkeleton };
        scene.Appearance.Parts.Add(part);
        var mark = new Wound(9, .5f, Vector3.UnitZ, new(1, 0, .3f, .5f, WoundKind.Slash), 1, 1, 1, 1, 0);
        var state = State() with { Wounds = new([new BodyWoundEntry(-1, mark)]) };
        var first = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, path => path == part.Mesh ? cloth : null, state);
        Assert.Null(first.Retained.BodyMesh);
        Assert.Null(Assert.Single(first.Retained.Parts!, supplied => ReferenceEquals(supplied.Part, part)).Mesh);
        Assert.Empty(first.Detached);
        var changed = state with { Wounds = new([new BodyWoundEntry(-1, mark with { Dose = 2, Hits = 2, Age = 2 })]) };
        Assert.Same(first, scene.Adapter.Build(scene.Appearance, Rig, scene.Body,
            _ => throw new InvalidOperationException("A shader mark update must not rebuild its source meshes."), changed));
    }

    [Fact]
    public void Uncut_bounds_do_not_allocate_per_triangle_samples_or_an_encoded_replacement_mesh()
    {
        var source = PosedMixedExterior();
        var weights = new Dictionary<string, float> { ["head_size"] = .5f };
        _ = GeneratedExteriorLod.Clip(source, Rig, [], weights);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var original = GeneratedExteriorLod.Clip(source, Rig, [], weights);
        long uncut = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var copied = MeshCut.Clip(source, Rig, [], weights, buildCaps: false);
        long encoded = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(source, original.Skin); Assert.NotSame(source, copied.Skin);
        int vertices = MeshCut.SourceModel(source, Rig.BoneCount).Parts.Single().Vertices.Length;
        Assert.InRange(uncut, 0, vertices * 16L + 8192);
        Assert.True(encoded > uncut * 4, $"Uncut bounds allocated {uncut:N0} bytes versus {encoded:N0} for the unused encoded copy.");
        Assert.Equal(copied.Min, original.Min); Assert.Equal(copied.Max, original.Max);
    }
}
