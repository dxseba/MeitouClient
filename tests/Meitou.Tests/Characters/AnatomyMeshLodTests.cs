using System.Numerics;

using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class AnatomyMeshLodTests
{
    [Fact]
    public void Close_up_level_keeps_every_original_triangle_and_distant_levels_reduce_dense_interiors()
    {
        var source = Grid(1, 0, Vector4.One);
        var original = source.ToArray();
        var levels = AnatomyMeshLod.Build(source);
        Assert.Equal(4, levels.Length);
        Assert.Equal(0, AnatomyMeshLod.LevelForPixels(900, levels.Length));
        Assert.Equal(Enumerable.Range(0, source.Length).Select(i => (uint)i), levels[0]);
        Assert.Equal(original, source);
        Assert.True(levels[^1].Length < levels[0].Length / 4);
        Assert.True(AnatomyMeshLod.LevelForPixels(200, levels.Length) > 0);
        Assert.Equal(0, AnatomyMeshLod.LevelForPixels(200, 1));
    }

    [Fact]
    public void Reduced_interiors_keep_layers_organs_and_skinning_separate_and_preserve_winding()
    {
        Vertex[] source = [.. Grid(1, 0, Vector4.One), .. Grid(2, 1, new Vector4(0.5f, 0.1f, 0.2f, 1))];
        var levels = AnatomyMeshLod.Build(source);
        foreach (var indices in levels)
        {
            Assert.Equal(0, indices.Length % 3);
            Assert.Contains(indices, i => source[i].Bones == 1);
            Assert.Contains(indices, i => source[i].Bones == 2);
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = source[indices[i]];
                var b = source[indices[i + 1]];
                var c = source[indices[i + 2]];
                Assert.Equal(a.Bones, b.Bones);
                Assert.Equal(a.Bones, c.Bones);
                Assert.Equal(a.Tangent.W, b.Tangent.W);
                Assert.Equal(a.Tangent.W, c.Tangent.W);
                Assert.Equal(a.Colour, b.Colour);
                Assert.Equal(a.Colour, c.Colour);
                Assert.True(Vector3.Cross(b.Position - a.Position, c.Position - a.Position).Y < 0);
            }
        }
    }

    static Vertex[] Grid(uint bone, float layer, Vector4 colour)
    {
        var vertices = new List<Vertex>();
        for (int x = 0; x < 50; x++)
            for (int z = 0; z < 50; z++)
            {
                Add(x, z); Add(x + 1, z); Add(x + 1, z + 1);
                Add(x, z); Add(x + 1, z + 1); Add(x, z + 1);
            }
        return vertices.ToArray();
        void Add(int x, int z) => vertices.Add(new Vertex
        {
            Position = new Vector3(x * 0.01f, 0, z * 0.01f),
            Normal = -Vector3.UnitY,
            Bones = bone,
            Weights = Vector4.UnitX,
            Colour = colour,
            Tangent = new Vector4(0, 0, 0, layer)
        });
    }
}