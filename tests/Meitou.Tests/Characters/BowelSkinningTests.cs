using System.Numerics;
using Meitou.Rendering;

namespace Meitou.Tests.Characters;

public class BowelSkinningTests
{
    static Vertex[] Source() =>
        [.. from x in new[] { -2f, 2f } from y in new[] { -1f, 1f } from z in new[] { -0.3f, 0.3f }
            select new Vertex
            {
                Position = new(x, y, z), Normal = new(2, 3, 4), Uv = new(x + 10, y + 20),
                Tangent = new(1, 0, 0, -1), Colour = new(0.2f, 0.3f, 0.4f, 0.7f),
                Bones = 0x03020100, Weights = new(0.4f, 0.3f, 0.2f, 0.1f),
            }];

    [Fact]
    public void The_rest_lattice_preserves_mesh_shape_uvs_and_vertex_metadata()
    {
        var source = Source();
        var skin = new BowelSkinning(source);
        Assert.Equal(9, skin.Geometry.RestNodes.Length);
        Assert.True(float.IsFinite(skin.Geometry.Radius) && skin.Geometry.Radius > 0);
        var output = new Vertex[source.Length];
        Assert.True(skin.Deform(Enumerable.Repeat(Matrix4x4.Identity, 9).ToArray(), output));
        for (int i = 0; i < source.Length; i++)
        {
            Assert.InRange(Vector3.Distance(source[i].Position, output[i].Position), 0, 0.00001f);
            Assert.InRange(Vector3.Distance(Vector3.Normalize(source[i].Normal), output[i].Normal), 0, 0.00001f);
            Assert.Equal(source[i].Uv, output[i].Uv);
            Assert.Equal(source[i].Tangent, output[i].Tangent);
            Assert.Equal(source[i].Colour, output[i].Colour);
            Assert.Equal(source[i].Bones, output[i].Bones);
            Assert.Equal(source[i].Weights, output[i].Weights);
        }
    }

    [Fact]
    public void Different_node_motion_flexes_the_mesh_within_the_physical_displacement_envelope()
    {
        var source = Source();
        var skin = new BowelSkinning(source);
        var translations = skin.Geometry.RestNodes.Select(node => new Vector3(0, node.X * 0.4f, 0)).ToArray();
        var transforms = translations.Select(Matrix4x4.CreateTranslation).ToArray();
        var output = new Vertex[source.Length];
        Assert.True(skin.Deform(transforms, output));
        float min = translations.Min(t => t.Y), max = translations.Max(t => t.Y);
        for (int i = 0; i < source.Length; i++)
        {
            var movement = output[i].Position - source[i].Position;
            Assert.True(float.IsFinite(movement.X) && float.IsFinite(movement.Y) && float.IsFinite(movement.Z));
            Assert.InRange(MathF.Abs(movement.X) + MathF.Abs(movement.Z), 0, 0.00001f);
            Assert.InRange(movement.Y, min - 0.00001f, max + 0.00001f);
            Assert.InRange(MathF.Abs(output[i].Normal.Length() - 1), 0, 0.00001f);
        }
        float originalDistance = Vector3.Distance(source[0].Position, source[4].Position);
        float deformedDistance = Vector3.Distance(output[0].Position, output[4].Position);
        Assert.True(MathF.Abs(deformedDistance - originalDistance) > 0.02f);
        Assert.False(skin.Deform(transforms.AsSpan(0, 8), output));
        Assert.False(skin.Deform(transforms, output.AsSpan(0, output.Length - 1)));
        var invalid = Enumerable.Repeat(Matrix4x4.CreateScale(float.NaN), 9).ToArray();
        Assert.False(skin.Deform(invalid, output));
    }
}
