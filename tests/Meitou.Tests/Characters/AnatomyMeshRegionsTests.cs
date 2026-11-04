using System.Numerics;

using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class AnatomyMeshRegionsTests
{
    [Fact]
    public void Every_level_partitions_head_and_jaw_from_body_without_duplicating_indices_or_reversing_triangles()
    {
        Vertex[] vertices = [.. Triangle(4, 18), .. Triangle(1, 8), .. Triangle(5, 17), .. Triangle(2, 12)];
        uint[][] levels = [[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], [6, 7, 8, 9, 10, 11]];
        var regions = AnatomyMeshRegions.Build(vertices, levels, headBone: 4, jawBone: 5);
        Assert.Equal(levels.Sum(level => level.Length), regions.Indices.Length);
        for (int level = 0; level < levels.Length; level++)
        {
            uint[] whole = Slice(regions, regions.Whole, level), head = Slice(regions, regions.Head, level), body = Slice(regions, regions.Body, level);
            Assert.Equal(levels[level].Order(), whole.Order());
            Assert.Equal(whole, body.Concat(head));
            Assert.All(head, index => Assert.True(vertices[index].Bones is 4 or 5));
            Assert.All(body, index => Assert.True(vertices[index].Bones is 1 or 2));
            for (int i = 0; i < whole.Length; i += 3)
            {
                uint start = whole[i];
                Assert.Equal(start + 1, whole[i + 1]);
                Assert.Equal(start + 2, whole[i + 2]);
            }
        }
    }

    [Fact]
    public void Detached_head_bounds_cover_only_the_retained_head_so_body_size_cannot_force_full_detail()
    {
        Vertex[] vertices = [.. Triangle(1, 0), .. Triangle(4, 18), .. Triangle(5, 17)];
        uint[][] levels = [[0, 1, 2, 3, 4, 5, 6, 7, 8]];
        var regions = AnatomyMeshRegions.Build(vertices, levels, headBone: 4, jawBone: 5);
        Assert.InRange(regions.Head.Centre.Y, 17, 19);
        Assert.True(regions.Head.Radius < 2);
        Assert.True(regions.Whole.Radius > 9);
        foreach (uint index in Slice(regions, regions.Head, 0))
            Assert.True(Vector3.Distance(vertices[index].Position, regions.Head.Centre) <= regions.Head.Radius + 0.0001f);
    }

    [Fact]
    public void A_rig_without_head_anatomy_has_an_empty_finite_head_view_and_keeps_its_body()
    {
        var vertices = Triangle(1, 8);
        uint[][] levels = [[0, 1, 2]];
        var regions = AnatomyMeshRegions.Build(vertices, levels, headBone: -1, jawBone: -1);
        Assert.Equal([0], regions.Head.Counts);
        Assert.Equal(Vector3.Zero, regions.Head.Centre);
        Assert.Equal(0, regions.Head.Radius);
        Assert.Equal(levels[0], Slice(regions, regions.Body, 0));
    }

    static uint[] Slice(AnatomyMeshRegions regions, AnatomyMeshRegion region, int level) =>
        regions.Indices.AsSpan(region.Offsets[level], region.Counts[level]).ToArray();

    static Vertex[] Triangle(uint bone, float height) =>
    [
        new() { Position = new Vector3(0, height, 0), Bones = bone, Weights = Vector4.UnitX },
        new() { Position = new Vector3(1, height, 0), Bones = bone, Weights = Vector4.UnitX },
        new() { Position = new Vector3(0, height + 1, 0), Bones = bone, Weights = Vector4.UnitX }
    ];
}
