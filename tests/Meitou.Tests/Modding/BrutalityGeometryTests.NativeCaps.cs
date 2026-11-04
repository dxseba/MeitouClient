using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Ogre;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Exact_native_limb_section_keeps_its_layered_flesh_bone_marrow_and_single_segment_owner()
    {
        using var scene = new Scene();
        var source = Box(new(-3, 10, -.5f), new(-2, 16.2f, .5f), 5);
        var cap = Assert.IsType<OgreMesh>(NativeCapGeometry.Build(source, Rig, null, scene.Appearance,
            [new(new(0, 1, 0, -12), Group: 1)], null, 0x800000, false));
        var vertices = NativeVertices(cap);
        Assert.NotEmpty(vertices);
        Assert.All(vertices, vertex =>
        {
            Assert.Equal(35, vertex.Tangent.W);
            Assert.Equal(6u, vertex.Bones);
            Assert.Equal(Vector4.UnitX, vertex.Weights);
        });
        Assert.Contains(vertices, vertex => vertex.Colour.X > .6f && vertex.Colour.Y > .5f && vertex.Colour.Z > .35f);
        Assert.Contains(vertices, vertex => vertex.Colour.X > vertex.Colour.Y * 3 && vertex.Colour.Y < .1f);
        Assert.True(vertices.Select(vertex => vertex.Uv.X).Distinct().Count() > 2, "The native rim must retain its separate tissue bands");
    }

    [Fact]
    public void Exact_native_layered_caps_do_not_weld_coincident_independently_falling_arms()
    {
        using var scene = new Scene();
        var source = Box(new(-1), new(1), 5).Concat(Box(new(-1), new(1), 8)).ToArray();
        int[] components = Components((5, 1), (8, 2));
        var cap = Assert.IsType<OgreMesh>(NativeCapGeometry.Build(source, Rig, null, scene.Appearance,
            [new(new(0, 1, 0, 0))], components, 0x800000, false));
        var vertices = NativeVertices(cap);
        Assert.Contains(vertices, vertex => vertex.Bones == 5);
        Assert.Contains(vertices, vertex => vertex.Bones == 8);
        foreach (var triangle in vertices.Chunk(3))
        {
            Assert.Single(triangle.Select(vertex => components[vertex.Bones & 255]).Distinct());
            var posed = triangle.Select(vertex => Posed(vertex, bone => Matrix4x4.CreateTranslation(bone == 5 ? -50 : 50, 0, 0))).ToArray();
            Assert.True(Vector3.Distance(posed[0], posed[1]) < 4);
            Assert.True(Vector3.Distance(posed[0], posed[2]) < 4);
        }
    }
}
