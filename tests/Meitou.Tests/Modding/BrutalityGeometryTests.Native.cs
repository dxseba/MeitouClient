using System.Numerics;
using System.Text;
using Meitou.Brutality;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Native_vertex_round_trip_keeps_layer_ids_bind_coordinates_and_opaque_colour_bits()
    {
        Vertex[] source = [Point(new(0, 0, 0), 1), Point(new(1, 0, 0), 1), Point(new(0, 1, 0), 1)];
        for (int i = 0; i < source.Length; i++)
            source[i] = source[i] with { Tangent = new(new Vector3(2 + i, -3, 7), 33 + i),
                Colour = new(BitConverter.UInt32BitsToSingle(17), -.25f, 1.4f, 5.8f) };
        var mesh = MeshCut.FromTriangles(source, preserveRawVertexChannels: true);
        var vertices = NativeVertices(mesh);
        Assert.Equal(source.Length, vertices.Length);
        for (int i = 0; i < source.Length; i++)
        {
            Assert.Equal(source[i].Tangent, vertices[i].Tangent);
            Assert.Equal(source[i].Colour, vertices[i].Colour);
            Assert.Equal(17u, BitConverter.SingleToUInt32Bits(vertices[i].Colour.X));
        }
        Assert.Equal(OgreVertexElementType.Float4, mesh.SubMeshes[0].VertexData!.Find(OgreVertexSemantic.Diffuse)!.Type);
    }

    [Fact]
    public void Native_mouth_keeps_upper_gums_and_throat_as_complete_rigid_head_triangles()
    {
        using var scene = new Scene(withAnatomy: true);
        string bodyPath = Assert.IsType<string>(scene.Adapter.Anatomy.PathFor(AnatomyKind.Body));
        using (var writer = new BinaryWriter(File.Create(Path.Combine(Path.GetDirectoryName(bodyPath)!, "Mouth.anatomy"))))
        {
            writer.Write(Encoding.ASCII.GetBytes("ECR1")); writer.Write(2);
            Batch("Gingiva"); Batch("superior_constrictor");
            void Batch(string name)
            {
                byte[] encoded = Encoding.ASCII.GetBytes(name);
                writer.Write(encoded.Length); writer.Write(5); writer.Write(6); writer.Write(encoded);
                foreach (var p in new[] { new Vector3(-1, 1, -1), new(1, 1, -1), new(0, 1, 1),
                    new(-1, -1, -1), new(0, -1, 1), new(1, -1, -1) })
                {
                    writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z);
                    writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(0f); writer.Write(0f);
                }
            }
        }
        var state = State() with { SevereHead = new(4, SevereHeadWoundKind.JawLost) };
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state);
        var inside = Assert.Single(result.Retained.Parts!, part => part.Part.Field == "anatomy");
        var mouth = NativeVertices(inside.Mesh!).Where(vertex => vertex.Tangent.W == 5).ToArray();
        Assert.Equal(6, mouth.Length);
        Assert.All(mouth, vertex =>
        {
            Assert.Equal(3u, vertex.Bones); Assert.Equal(Vector4.UnitX, vertex.Weights);
            Assert.True(vertex.Position.Y > 17.36f);
        });
        Assert.True(inside.PreserveRawVertexChannels);
        Assert.False(inside.ShowInPortrait);
        Assert.Equal(NativeSurfaceShaders.WoundFlag | NativeSurfaceShaders.AnatomyFlag, inside.DrawFlags);
    }

    static Vertex[] NativeVertices(OgreMesh mesh) => Model.Build(mesh, Rig.BoneCount, preserveRawVertexChannels: true)
        .Parts.SelectMany(part => part.Vertices).ToArray();
}
