using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class LimbCutRenderingTests
{
    [Fact]
    public void Rendering_packs_all_four_shared_race_fitted_planes_without_mutating_the_cut_states()
    {
        var rig = Rig();
        LimbCutState[] cuts = [new(0, .2f, new(.35f, -.2f)), new(1, .7f, new(-.4f, .25f)),
            new(2, .45f, new(.2f, .5f)), new(3, .85f, new(-.2f, -.35f))];
        var original = cuts.ToArray();
        var planes = LimbCutPlanes.Resolve(rig, cuts);
        Assert.Equal(15u, planes.Mask);
        for (int slot = 0; slot < 4; slot++)
        {
            Assert.True(LimbCutGeometry.TryPlane(rig, cuts[slot], out var expected));
            Assert.Equal(expected, planes.Plane(slot));
            Assert.Equal(1, new Vector3(expected.X, expected.Y, expected.Z).Length(), 1e-5f);
        }
        Assert.Equal(original, cuts);
        Assert.Equal(default, LimbCutPlanes.Resolve(rig, [new(-1, .5f, default), new(0, float.NaN, default)]));
    }

    [Fact]
    public void Detached_255_sentinel_uses_the_opposite_actual_plane_and_never_hides_a_different_limb()
    {
        var plane = Vector4.Normalize(new(.4f, 1, .2f, 0));
        var planes = Planes(plane);
        var proximal = new Vector3(0, 2, 0); var distal = -proximal;
        Assert.True(planes.Band(proximal, 1) > 0);
        Assert.True(planes.Band(distal, 1) < 0);
        const uint only = 1u << 8 | 255u;
        Assert.Equal(-planes.Band(proximal, 1), planes.Band(proximal, 1, partOnly: only));
        Assert.True(planes.Band(distal, 1, partOnly: only) > 0);
        Assert.Equal(1000, planes.Band(distal, 2));
        Assert.True(planes.Band(distal, 2, partOnly: only) < 0);
        Assert.Equal(1, default(LimbCutPlanes).Band(new(0, 13, 0), 1, legacy: 120), 1e-5f);
    }

    [Fact]
    public void Picking_intersects_the_torso_and_limb_intervals_instead_of_taking_either_entry_point()
    {
        var planes = Planes(new(0, 1, 0, -1));
        var torso = new CharacterTorsoCut(new(-1, 0, 0, 0), 1, true);
        var min = new Vector3(-5); var max = new Vector3(5);
        Assert.Null(planes.RayBox(new(-2, -2, 0), new(1, 1, 0), min, max, 1, torso: torso));
        var valid = torso with { Plane = new(-1, 0, 0, 2) };
        Assert.Equal(3, planes.RayBox(new(-2, -2, 0), new(1, 1, 0), min, max, 1, torso: valid)!.Value, 1e-5f);
        Assert.Null(planes.RayBox(new(-2, -2, 0), new(1, 1, 0), min, max, 2, partOnly: 1u << 8 | 255u));
    }

    [Fact]
    public void Oblique_caps_close_both_sides_and_exclude_an_unrelated_limb_at_the_same_height()
    {
        Vertex[] skin = [.. Box(new(-3, -1, -1), new(-1, 3, 1), 0), .. Box(new(3, -1, -1), new(5, 3, 1), 1)];
        uint[] groups = [1u | 3u << 4];
        var normal = Vector3.Normalize(new(.35f, 1, .25f));
        var planes = Planes(new(normal, 0));
        foreach (bool mechanical in new[] { false, true })
            foreach (uint only in new[] { 0u, 1u << 8 | 255u })
            {
                var cap = LimbCutSurface.Build(skin, groups, planes, only, mechanical: mechanical);
                Assert.NotEmpty(cap);
                Assert.InRange(cap.Length, 3, LimbCutSurface.VertexLimit);
                Assert.All(cap, vertex =>
                {
                    Assert.InRange(vertex.Position.X, -3.02f, -.98f);
                    Assert.Equal(1, TorsoCutSurface.Group(vertex, groups));
                    Assert.Equal(LimbCutSurface.Layer, vertex.Tangent.W);
                    Assert.True(float.IsFinite(vertex.Position.LengthSquared()));
                    Assert.True(float.IsFinite(vertex.Normal.LengthSquared()));
                    Assert.Equal(1, vertex.Weights.X + vertex.Weights.Y + vertex.Weights.Z + vertex.Weights.W, 1e-5f);
                });
                var rim = cap.Where(v => v.Uv.X < .5f).ToArray();
                Assert.NotEmpty(rim);
                Assert.All(rim, v => Assert.InRange(MathF.Abs(Vector3.Dot(normal, v.Position)), 0, .05f));
            }
    }

    [Fact]
    public void Several_limb_caps_share_one_bounded_mesh_and_detached_selection_keeps_only_its_own_section()
    {
        var skin = Enumerable.Range(0, 4).SelectMany(i => Box(new(i * 4, -1, -1), new(i * 4 + 2, 3, 1), (uint)i)).ToArray();
        uint[] groups = [0x4321];
        var planes = new LimbCutPlanes(new(0, 1, 0, 0, 0, 1, 0, -.2f, 0, 1, 0, -.4f, 0, 1, 0, -.6f), 15);
        var owner = LimbCutSurface.Build(skin, groups, planes);
        Assert.InRange(owner.Length, 12, LimbCutSurface.VertexLimit);
        foreach (int group in Enumerable.Range(1, 4)) Assert.Contains(owner, v => TorsoCutSurface.Group(v, groups) == group);
        var detached = LimbCutSurface.Build(skin, groups, planes, 3u << 8 | 255u);
        Assert.NotEmpty(detached);
        Assert.All(detached, v => Assert.Equal(3, TorsoCutSurface.Group(v, groups)));
        Assert.Equal(36, CharacterShaders.WoundPaletteBones);
        Assert.Equal(35, CharacterShaders.WoundHeaderBones);
    }

    static LimbCutPlanes Planes(Vector4 plane) => new(new(plane.X, plane.Y, plane.Z, plane.W, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 1);

    [Theory]
    [InlineData(19f, 0)]
    [InlineData(17f, 1)]
    public void Cut_caps_cannot_stretch_between_articulated_actors_sharing_one_component(float height, int cutBone)
    {
        var rig = Rig();
        var skin = Box(new(-3, 16, -1), new(-1, 21, 1), 0)
            .Select(vertex => vertex with { Bones = 1u << 8,
                Weights = new(.1f + .8f * (vertex.Position.X + 3) * .5f, .9f - .8f * (vertex.Position.X + 3) * .5f, 0, 0) }).ToArray();
        uint[] groups = [0x11];
        int[] components = [0, 0, 0];
        Matrix4x4[] matrices = [Matrix4x4.CreateRotationZ(.2f),
            Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(75, 20, -35)];
        foreach (uint only in new[] { 0u, 1u << 8 | 255u })
            foreach (bool mechanical in new[] { false, true })
            {
                var cap = LimbCutSurface.Build(skin, groups, Planes(new(0, 1, 0, -height)), only,
                    components: components, mechanical: mechanical, rig: rig);
                Assert.NotEmpty(cap);
                Assert.All(cap, vertex =>
                {
                    Assert.Equal((uint)cutBone, vertex.Bones);
                    Assert.Equal(Vector4.UnitX, vertex.Weights);
                });
                var posed = cap.Select(vertex => Vector3.Transform(vertex.Position, matrices[(int)vertex.Bones])).ToArray();
                for (int triangle = 0; triangle + 2 < cap.Length; triangle += 3)
                    for (int edge = 0; edge < 3; edge++)
                    {
                        int first = triangle + edge, second = triangle + (edge + 1) % 3;
                        float before = Vector3.Distance(cap[first].Position, cap[second].Position);
                        float after = Vector3.Distance(posed[first], posed[second]);
                        Assert.InRange(MathF.Abs(after - before), 0, 1e-4f);
                    }
            }
    }

    static SkeletonRig Rig()
    {
        var skeleton = new OgreSkeleton();
        ushort handle = 0;
        foreach (string side in new[] { "L", "R" })
            foreach (string[] chain in new[] { new[] { "UpperArm", "Forearm", "Hand" }, new[] { "Thigh", "Calf", "Foot" } })
                foreach (string bone in chain)
                {
                    skeleton.Bones.Add(new() { Name = $"Bip01 {side} {bone}", Handle = handle,
                        Position = new(side == "L" ? -2 : 2, 20 - handle * 2, 0) });
                    handle++;
                }
        return new(skeleton);
    }

    static Vertex[] Box(Vector3 min, Vector3 max, uint bone)
    {
        Vertex V(int x, int y, int z) => new() { Position = new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y,
            z == 0 ? min.Z : max.Z), Bones = bone, Weights = Vector4.UnitX };
        var result = new List<Vertex>();
        void Quad(Vertex a, Vertex b, Vertex c, Vertex d) => result.AddRange([a, b, c, b, d, c]);
        Quad(V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(1, 1, 0));
        Quad(V(0, 0, 1), V(0, 1, 1), V(1, 0, 1), V(1, 1, 1));
        Quad(V(0, 0, 0), V(0, 1, 0), V(0, 0, 1), V(0, 1, 1));
        Quad(V(1, 0, 0), V(1, 0, 1), V(1, 1, 0), V(1, 1, 1));
        Quad(V(0, 0, 0), V(0, 0, 1), V(1, 0, 0), V(1, 0, 1));
        Quad(V(0, 1, 0), V(1, 1, 0), V(0, 1, 1), V(1, 1, 1));
        return result.ToArray();
    }
}
