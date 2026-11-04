using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Coincident_cut_contours_on_separate_physical_arms_keep_two_independent_caps()
    {
        var source = MeshCut.FromTriangles(Box(new(-1), new(1), 5).Concat(Box(new(-1), new(1), 8)).ToArray());
        int[] components = Components((5, 1), (8, 2));
        var result = MeshCut.Clip(source, Rig, [new(new(0, 1, 0, 0))], components: components);
        var caps = Vertices(Assert.IsType<OgreMesh>(result.Wound));
        Assert.Contains(caps, vertex => vertex.Bones == 5);
        Assert.Contains(caps, vertex => vertex.Bones == 8);
        foreach (var triangle in caps.Chunk(3))
        {
            Assert.Single(triangle.Select(vertex => components[vertex.Bones & 255]).Distinct());
            var posed = triangle.Select(vertex => Posed(vertex, bone => Matrix4x4.CreateTranslation(bone == 5 ? -50 : 50, 0, 0))).ToArray();
            Assert.True(Vector3.Distance(posed[0], posed[1]) < 4);
            Assert.True(Vector3.Distance(posed[0], posed[2]) < 4);
        }
    }

    [Fact]
    public void A_section_drops_cross_island_skin_weights_before_the_other_island_moves()
    {
        var source = MeshCut.FromTriangles(Box(new(-1), new(1), 1).Select(vertex => vertex with
        {
            Bones = 1u | 8u << 8,
            Weights = vertex.Position.X > 0 ? new(.9f, .1f, 0, 0) : new(.55f, .45f, 0, 0),
        }).ToArray());
        var cut = MeshCut.Clip(source, Rig, [new(new(0, 1, 0, 0))], components: Components((1, 1), (8, 2)));
        var caps = Vertices(Assert.IsType<OgreMesh>(cut.Wound));
        Assert.NotEmpty(caps);
        Assert.All(caps, vertex =>
        {
            for (int k = 0; k < 4; k++)
                if ((vertex.Bones >> (8 * k) & 255) == 8) Assert.Equal(0, vertex.Weights[k]);
            var posed = Posed(vertex, bone => Matrix4x4.CreateTranslation(bone == 8 ? 1000 : 0, 0, 0));
            Assert.InRange(posed.X, -1.1f, 1.1f);
        });
    }

    [Fact]
    public void A_forearm_section_follows_one_cut_segment_even_when_upper_arm_and_hand_are_weighted()
    {
        var source = MeshCut.FromTriangles(Box(new(-3, 10, -.5f), new(-2, 16.2f, .5f), 5)
            .Select(vertex => vertex with { Bones = 5u | 7u << 8, Weights = new(.5f, .5f, 0, 0) }).ToArray());
        var cut = MeshCut.Clip(source, Rig, [new(new(0, 1, 0, -12), Group: 1)]);
        var caps = Vertices(Assert.IsType<OgreMesh>(cut.Wound));
        Assert.NotEmpty(caps);
        Assert.All(caps, vertex => { Assert.Equal(6u, vertex.Bones); Assert.Equal(Vector4.UnitX, vertex.Weights); });
    }

    [Fact]
    public void Relief_grid_triangles_cannot_bridge_two_falling_physical_islands()
    {
        Vertex[] triangles = [Point(new(-1, 0, 0), 5), Point(new(1, 0, 0), 8), Point(new(-1, 1, 0), 5),
            Point(new(-3, 0, 0), 5), Point(new(-2, 0, 0), 5), Point(new(-3, 1, 0), 5)];
        var source = MeshCut.FromTriangles(triangles);
        var cut = MeshCut.Clip(source, Rig, [], components: Components((5, 1), (8, 2)), ownedSurface: true);
        var kept = Vertices(cut.Skin);
        Assert.Equal(3, kept.Length);
        Assert.All(kept, vertex => Assert.Equal(5u, vertex.Bones));
        Assert.All(kept, vertex => Assert.True(vertex.Position.X <= -2));
        Assert.Equal(6, Vertices(source).Length);
    }

    [Fact]
    public void Geometry_cache_reuses_one_topology_but_rebuilds_when_physical_ownership_changes()
    {
        using var scene = new Scene();
        var state = State(torso: new(new(0, 1, 0, -13)));
        var firstMap = new CharacterGeometryComponents(Components((5, 1), (8, 1)));
        var secondMap = new CharacterGeometryComponents(Components((5, 1), (8, 2)));
        var first = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state, firstMap);
        Assert.True(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, state, out var same, firstMap));
        Assert.Same(first, same);
        Assert.False(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, state, out _, secondMap));
        var second = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state, secondMap);
        Assert.NotSame(first.Retained, second.Retained);
        Assert.True(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, state, out var published));
        Assert.Same(second, published);
    }

    static int[] Components(params (int Bone, int Component)[] values)
    {
        var result = new int[Rig.BoneCount];
        foreach (var value in values) result[value.Bone] = value.Component;
        return result;
    }
    static Vector3 Posed(Vertex vertex, Func<int, Matrix4x4> skin)
    {
        var result = Vector3.Zero;
        for (int k = 0; k < 4; k++)
            result += Vector3.Transform(vertex.Position, skin((int)(vertex.Bones >> (8 * k) & 255))) * vertex.Weights[k];
        return result;
    }
}
