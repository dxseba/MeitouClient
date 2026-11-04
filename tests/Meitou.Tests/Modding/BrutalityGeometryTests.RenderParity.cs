using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Brutality.Rendering.Surface;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Consecutive_interpolated_fragment_poses_publish_distinct_objects_and_keep_sources_and_previous_skin_unchanged()
    {
        var before = PhysicalPose(); var after = PhysicalPose();
        before.Position[3] = new(1, 2, 3); after.Position[3] = new(9, 6, -1);
        before.Rotation[3] = Quaternion.Identity; after.Rotation[3] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .6f);
        var components = Enumerable.Repeat(0, Rig.BoneCount).ToArray(); before.Components = after.Components = components;
        var sourceBefore = before.Position.ToArray(); var sourceAfter = after.Position.ToArray();
        var blend = new FragmentPoseBlend();
        var first = blend.Between(before, after, .25f);
        var firstSkin = new Matrix4x4[Rig.BoneCount]; new PoseScratch(Rig).Pose(null, [], firstSkin, first);
        var second = blend.Between(before, after, .75f);
        Assert.NotSame(first, second); Assert.Same(components, first.Components); Assert.Same(components, second.Components);
        Assert.Equal(new Vector3(3, 3, 2), first.Position[3]); Assert.Equal(new Vector3(7, 5, 0), second.Position[3]);
        var stillFirst = new Matrix4x4[Rig.BoneCount]; new PoseScratch(Rig).Pose(null, [], stillFirst, first);
        Assert.Equal(firstSkin, stillFirst);
        var secondSkin = new Matrix4x4[Rig.BoneCount]; new PoseScratch(Rig).Pose(null, [], secondSkin, second);
        Assert.NotEqual(firstSkin[3], secondSkin[3]);
        var third = blend.Between(before, after, .9f); Assert.Same(first, third);
        Assert.Equal(new Vector3(7, 5, 0), second.Position[3]);
        Assert.Equal(sourceBefore, before.Position); Assert.Equal(sourceAfter, after.Position);
        Assert.Same(after, blend.Between(after, after, .5f));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exterior_and_interior_materials_keep_the_same_robot_classification(bool robot)
    {
        using var scene = new Scene();
        var db = new GameDatabase(); var file = new FcsFile();
        var race = new FcsRecord { StringId = "material-race", Name = "material race", RecordType = FcsRecordType.RACE };
        race.Bools["is robot"] = robot; file.Records.Add(race); db.Apply(file, "material.mod");
        var look = new CharacterAppearance { Race = db.Find("material-race")! };
        var result = scene.Adapter.Build(look, Rig, scene.Body, _ => null, State(torso: new(new(0, 1, 0, -13))));
        uint expected = robot ? NativeSurfaceShaders.Mechanical : 0;
        foreach (var geometry in result.Detached.Select(piece => piece.Geometry).Prepend(result.Retained))
        {
            Assert.Equal(expected, geometry.BodyMaterialFlags);
            Assert.All(geometry.Parts!.Where(part => part.Part.Field is "anatomy" or "wound-section" or "mechanical-section"),
                part => Assert.Equal(expected, part.MaterialFlags));
        }
    }

    [Fact]
    public void Organic_hidden_anatomy_and_head_relief_do_not_cast_shadows_but_the_exposed_cut_cap_does()
    {
        using var scene = new Scene(withAnatomy: true);
        var state = State(torso: new(new(0, 1, 0, -13))) with
        { SevereHead = new(4, SevereHeadWoundKind.Scalp, 3, new(new(0, 18.65f, 0), Vector3.UnitZ, Vector3.UnitX)) };
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state);
        var parts = result.Retained.Parts!.Concat(result.Detached.SelectMany(piece => piece.Geometry.Parts!)).ToArray();
        Assert.Contains(parts, part => part.Part.Field == "head-relief");
        Assert.Contains(parts, part => part.Part.Field == "anatomy");
        Assert.Contains(parts, part => part.Part.Field == "wound-section");
        Assert.All(parts.Where(part => part.Part.Field is "anatomy" or "head-relief"), part => Assert.False(part.CastsShadows));
        Assert.All(parts.Where(part => part.Part.Field == "wound-section"), part => Assert.True(part.CastsShadows));
    }
}
