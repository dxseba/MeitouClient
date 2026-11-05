using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    const float Spine = 6.1649f;   // the test rig's Pelvis to Neck bind distance

    [Fact]
    public void The_hole_matrix_and_header_bit_reach_only_the_retained_body_of_an_uncleaved_torso()
    {
        var hole = ChestHolePolicy.Preview(1);
        var state = State() with { ChestHole = hole };
        var frame = ChestHoleFrame.Resolve(Rig, hole)!.Value;
        Assert.Equal(hole.Across * Spine, frame.Centre.X, 4);
        Assert.Equal(10 + hole.Height * Spine, frame.Centre.Y, 4);
        Assert.Equal(hole.Radius * Spine, frame.Radius, 4);
        Assert.Equal(MathF.Sin(hole.Yaw), frame.Axis.X, 5);
        Assert.Equal(-MathF.Cos(hole.Yaw), frame.Axis.Z, 5);

        var words = new uint[16 * NativeSurfaceShaders.WoundPaletteBones];
        var packed = NativeSurfaceBridge.ChestHole(Rig, state, 0, words);
        Assert.Equal(new Vector4(frame.Centre, frame.Radius), new Vector4(packed.M11, packed.M12, packed.M13, packed.M14));
        Assert.Equal(new Vector4(frame.Axis, hole.Seed), new Vector4(packed.M21, packed.M22, packed.M23, packed.M24));
        Assert.Equal(new Vector4(hole.Aspect, hole.Tilt, hole.Tear, hole.Flare), new Vector4(packed.M31, packed.M32, packed.M33, packed.M34));
        Assert.Equal(hole.Region, packed.M41);
        Assert.Equal(NativeSurfaceShaders.ChestHoleHeaderBit, words[NativeSurfaceBridge.ChestHoleWord]);

        foreach (int piece in new[] { 1, 2, 3, 10, 13 }) AssertNoHole(state, piece);
        AssertNoHole(state with { Torso = new(new(0, 1, 0, -13)) }, 0);
        AssertNoHole(State(), 0);
    }

    static void AssertNoHole(BrutalityActorView state, int piece)
    {
        var words = new uint[16 * NativeSurfaceShaders.WoundPaletteBones];
        Assert.Equal(default, NativeSurfaceBridge.ChestHole(Rig, state, piece, words));
        Assert.All(words, word => Assert.Equal(0u, word));
    }

    [Fact]
    public void The_shaders_read_the_hole_where_the_bridge_writes_it()
    {
        // The matrix precedes the 36-matrix legacy layout; its flag is the header's word 30, column 3 row 2.
        Assert.Equal(NativeSurfaceShaders.WoundPaletteBones + 1, NativeSurfaceShaders.HolePaletteBones);
        Assert.Equal(16 + 3 * 4 + 2, NativeSurfaceBridge.ChestHoleWord);
        string vertex = NativeSurfaceShaders.Vertex();
        Assert.Contains($"skin.bones[aInstanceData.x - {NativeSurfaceShaders.HolePaletteBones}u]", vertex);
        Assert.Contains($"(floatBitsToUint(header[3][2]) & {NativeSurfaceShaders.ChestHoleHeaderBit}u)", vertex);
        string[] fragments = [NativeSurfaceShaders.Fragment(), NativeSurfaceShaders.DepthFragment(), NativeSurfaceShaders.MotionFragment()];
        foreach (string fragment in fragments) Assert.Contains("chestHoleEdge(vSkinPos, vHoleCentre, vHoleAxis, vHoleShape)", fragment);
    }

    [Fact]
    public void The_lining_spans_the_torso_from_front_to_back_skin_with_the_skins_weights()
    {
        using var scene = new Scene();
        var hole = ChestHolePolicy.Preview(0);
        var state = State() with { ChestHole = hole };
        var frame = ChestHoleFrame.Resolve(Rig, hole)!.Value;
        var part = Assert.Single(scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state).Retained.Parts!,
            candidate => candidate.Part.Field == "chest-hole");
        Assert.False(part.CastsShadows);
        Assert.True(part.DoubleSided);
        Assert.False(part.ShowInPortrait);

        var vertices = Model.Build(part.Mesh!, Rig.BoneCount, preserveRawVertexChannels: true).Parts.SelectMany(p => p.Vertices).ToArray();
        Assert.NotEmpty(vertices);
        Assert.All(vertices, vertex =>
        {
            var across = vertex.Position - frame.Centre;
            across -= frame.Axis * Vector3.Dot(across, frame.Axis);
            Assert.Equal(frame.LiningRadius(0), across.Length(), 3);   // a round hole without flare: one radius throughout
            Assert.True(Vector3.Dot(vertex.Normal, across) < 0);   // the wall faces the tunnel
            Assert.Equal(NativeSurfaceShaders.ChestHoleLayer, vertex.Tangent.W);
            Assert.Equal(1u, vertex.Bones & 255);   // Spine1, the torso box's only bone
            Assert.Equal(1, vertex.Weights.X, 4);
        });
        // The torso box's front skin is at z 1 and its back at z -1; each end stays just inside.
        Assert.Equal(.97f, vertices.Max(vertex => vertex.Position.Z), 3);
        Assert.Equal(-.97f, vertices.Min(vertex => vertex.Position.Z), 3);

        var cleaved = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state with { Torso = new(new(0, 1, 0, -13)) });
        Assert.DoesNotContain(cleaved.Retained.Parts!, candidate => candidate.Part.Field == "chest-hole");
    }
}
