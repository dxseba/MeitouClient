using System.Numerics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public partial class AnatomyBindingTests
{
    static Vector3 Skin(Vertex vertex, Matrix4x4[] palette)
    {
        var result = Vector3.Zero;
        for (int i = 0; i < 4; i++)
        {
            int bone = (int)(vertex.Bones >> (8 * i) & 255);
            result += Vector3.Transform(vertex.Position, palette[bone]) * vertex.Weights[i];
        }
        return result;
    }

    [Fact]
    public void Fitted_internal_tissue_keeps_its_shape_when_neighboring_skin_chains_move_far_apart()
    {
        // The muscle belongs to the forearm, but fitted points may find torso, forearm and hand skin in one bind-space neighborhood.
        Vertex[] fitted =
        [
            new() { Position = new(2, 8, 0), Bones = 0, Weights = Vector4.UnitX, Uv = new(0.1f, 0.2f), Tangent = new(2, 8, 0, 1) },
            new() { Position = new(2.5f, 8, 0), Bones = 1u | 2u << 8, Weights = new(0.5f, 0.5f, 0, 0), Uv = new(0.2f, 0.2f), Tangent = new(2.5f, 8, 0, 1) },
            new() { Position = new(2, 8.6f, 0), Bones = 2, Weights = Vector4.UnitX, Uv = new(0.1f, 0.3f), Tangent = new(2, 8.6f, 0, 1) },
        ];
        Matrix4x4[] palette =
        [
            Matrix4x4.CreateTranslation(500, 0, 0),
            Matrix4x4.CreateTranslation(-2, -8, 0) * Matrix4x4.CreateRotationY(0.9f) * Matrix4x4.CreateTranslation(-20, 4, 10),
            Matrix4x4.CreateTranslation(0, -300, 0),
        ];
        Assert.True(Vector3.Distance(Skin(fitted[0], palette), Skin(fitted[2], palette)) > 100,
            "Independent nearest-skin weights reproduce the original long anatomy strip.");
        var rigid = fitted.Select(vertex => AnatomyTriangles.FollowBone(vertex, 1)).ToArray();
        var posed = rigid.Select(vertex => Skin(vertex, palette)).ToArray();
        for (int i = 0; i < 3; i++)
        {
            int next = (i + 1) % 3;
            Assert.Equal(Vector3.Distance(fitted[i].Position, fitted[next].Position), Vector3.Distance(posed[i], posed[next]), 1e-4f);
            Assert.Equal(fitted[i].Position, rigid[i].Position);
            Assert.Equal(fitted[i].Uv, rigid[i].Uv);
            Assert.Equal(fitted[i].Tangent, rigid[i].Tangent);
            Assert.InRange(Vector3.Distance(posed[i], new(-20, 4, 10)), 0, 1);
        }
    }
}
