using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed class BrutalityAnatomyExposureTests
{
    static readonly SkeletonRig Rig = MakeRig();

    [Fact]
    public void A_torso_opening_never_exposes_far_skinny_calf_head_or_shoulder_anatomy()
    {
        var source = MeshCut.FromTriangles(Triangle(new(0, 13, 0), 0, 10)
            .Concat(Triangle(new(3, 16, 0), 0, 20))
            .Concat(Triangle(new(-3, 13, 0), 3, 30))
            .Concat(Triangle(new(0, 18, 0), 2, 40))
            .Concat(Triangle(new(-1, 4, 0), 5, 50)).ToArray());
        CutHalfspace[] cut = [new(new(0, 1, 0, -13), LegacyTorso: true)];
        var exposed = AnatomyExposure.Filter(source, Rig, cut, []);
        Assert.Equal(new[] { 10f }, Vertices(exposed).Select(v => v.Uv.X).Distinct().ToArray());
        Assert.True(MeshCut.Clip(exposed, Rig, cut).HasTriangles);
        Assert.Empty(Vertices(AnatomyExposure.Filter(source, Rig, [], [])));
    }

    [Fact]
    public void A_full_body_vertical_cut_keeps_leg_internals_at_its_opening_instead_of_hiding_all_leg_groups()
    {
        var source = MeshCut.FromTriangles(Triangle(new(.1f, 4, 0), 5, 1)
            .Concat(Triangle(new(4, 13, 0), 3, 2)).ToArray());
        var exposed = AnatomyExposure.Filter(source, Rig, [new(new(1, 0, 0, 0))], []);
        Assert.Equal(new[] { 1f }, Vertices(exposed).Select(v => v.Uv.X).Distinct().ToArray());
    }

    [Fact]
    public void A_local_head_pocket_keeps_bone_and_brain_behind_it_without_exposing_the_opposite_scalp_or_other_body_groups()
    {
        var source = MeshCut.FromTriangles(Triangle(new(0, 18, .15f), 2, 1)
            .Concat(Triangle(new(0, 18, -3), 2, 2))
            .Concat(Triangle(new(-2, 18, .15f), 2, 3))
            .Concat(Triangle(new(0, 18, .15f), 0, 4)).ToArray());
        var volume = new CutVolume([new(new(1, 0, 0, .4f)), new(new(-1, 0, 0, .4f)),
            new(new(0, 1, 0, -17.6f)), new(new(0, -1, 0, 18.4f)),
            new(new(0, 0, 1, -.6f)), new(new(0, 0, -1, 1.2f))], Vector3.UnitZ);
        var exposed = AnatomyExposure.Filter(source, Rig, [], [volume]);
        Assert.Equal(new[] { 1f }, Vertices(exposed).Select(v => v.Uv.X).Distinct().ToArray());
        Assert.Empty(Vertices(AnatomyExposure.Filter(source, Rig, [new(new(0, 1, 0, -1_000_000), Group: 7)], [])));
    }

    static Vertex[] Triangle(Vector3 centre, int bone, float tag) => [
        Point(centre + new Vector3(-.1f, -.1f, 0), bone, tag),
        Point(centre + new Vector3(.1f, -.1f, 0), bone, tag),
        Point(centre + new Vector3(0, .1f, 0), bone, tag)];
    static Vertex Point(Vector3 position, int bone, float tag) => new() { Position = position, Normal = Vector3.UnitZ,
        Bones = (uint)bone, Weights = Vector4.UnitX, Uv = new(tag, 0), Colour = Vector4.One, Tangent = new(1, 0, 0, 1) };
    static Vertex[] Vertices(OgreMesh mesh) => Model.Build(mesh, Rig.BoneCount).Parts.SelectMany(part => part.Vertices).ToArray();
    static SkeletonRig MakeRig()
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01 Pelvis", Position = new(0, 10, 0) });
        skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 Neck", Position = new(0, 6.1649f, 0) });
        skeleton.Bones.Add(new() { Handle = 2, Parent = 1, Name = "Bip01 Head", Position = new(0, 2, 0) });
        skeleton.Bones.Add(new() { Handle = 3, Parent = 1, Name = "Bip01 L UpperArm", Position = new(-2, 0, 0) });
        skeleton.Bones.Add(new() { Handle = 4, Parent = 0, Name = "Bip01 L Thigh", Position = new(-1, -1, 0) });
        skeleton.Bones.Add(new() { Handle = 5, Parent = 4, Name = "Bip01 L Calf", Position = new(0, -4, 0) });
        return new(skeleton);
    }
}
