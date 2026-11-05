using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Held_families_copy_pose_animation_and_prefix_instead_of_following_the_claimed_native_root()
    {
        using var scene = new Scene();
        var bones = PhysicalPose(); bones.Position[3] = new(1, 18, 2);
        CharacterPose[] clips = [new("idle", .25f, 1)];
        var geometry = new RuntimeCharacterGeometry();
        var prefix = new RuntimeCharacterShaderData([Matrix4x4.CreateTranslation(1, 2, 3)]);
        var instance = new CharacterInstance(11, scene.Appearance, new(10, 0, 20), .3f, clips)
        { Bones = bones, Geometry = geometry, RuntimeShaderData = prefix, World = Matrix4x4.CreateTranslation(10, 0, 20) };
        var family = new CutPresentationFamily(); family.Capture(new[] { new PresentedPiece(0, instance) });
        bones.Position[3] = new(300, -100, 500); clips[0] = new("dead", 3, 1);
        Assert.True(family.Hold(scene.Appearance, _ => PhysicalPose()));
        var held = Assert.Single(family.Pieces).Instance;
        Assert.Equal(new Vector3(1, 18, 2), held.Bones!.Position[3]); Assert.Equal("idle", held.Poses[0].Animation);
        Assert.Equal(instance.Position, held.Position); Assert.Equal(instance.World, held.World);
        Assert.Same(geometry, held.Geometry); Assert.Same(prefix, held.RuntimeShaderData);
        family.Capture(new[] { new PresentedPiece(0, instance with { Position = new(1000) }) });
        Assert.Equal(new Vector3(10, 0, 20), Assert.Single(family.Pieces).Instance.Position);
    }

    [Fact]
    public void Retained_head_and_limb_replacements_are_not_ready_as_a_family_until_every_exact_gpu_variant_is_ready()
    {
        using var scene = new Scene();
        var body = new RuntimeCharacterGeometry(); var head = new RuntimeCharacterGeometry(); var limb = new RuntimeCharacterGeometry();
        PresentedPiece[] candidate = [new(0, new(10, scene.Appearance, Vector3.Zero, 0, []) { Geometry = body }),
            new(2, new(11, scene.Appearance, Vector3.Zero, 0, []) { Geometry = head }),
            new(10, new(12, scene.Appearance, Vector3.Zero, 0, []) { Geometry = limb })];
        var uploaded = new HashSet<RuntimeCharacterGeometry> { body };
        bool Ready(CharacterAppearance look, RuntimeCharacterGeometry? geometry) => ReferenceEquals(look, scene.Appearance)
            && geometry is not null && uploaded.Contains(geometry) && !geometry.IsRetired;
        Assert.False(CutPresentationFamily.Ready(candidate, Ready));
        uploaded.Add(head); Assert.False(CutPresentationFamily.Ready(candidate, Ready));
        uploaded.Add(limb); Assert.True(CutPresentationFamily.Ready(candidate, Ready));
        var sameMeshDifferentVariant = new RuntimeCharacterGeometry();
        Assert.False(CutPresentationFamily.Ready(new[] { candidate[0] with { Instance = candidate[0].Instance with { Geometry = sameMeshDifferentVariant } } }, Ready));
        head.Retire(); Assert.False(CutPresentationFamily.Ready(candidate, Ready));
        Assert.False(CutPresentationFamily.Ready(Array.Empty<PresentedPiece>(), Ready));
    }

    [Fact]
    public void Publication_begins_at_complete_held_world_bones_and_blends_all_new_islands_to_physics_in_120ms()
    {
        using var scene = new Scene();
        var family = new CutPresentationFamily();
        var root = new CharacterInstance(1, scene.Appearance, Vector3.Zero, 0, []);
        var arm = root with { Key = 2 };
        family.Capture(new[] { new PresentedPiece(0, root), new PresentedPiece(10, arm) });
        var wholeWorld = WorldPose(new(12, 30, -8)); var armWorld = WorldPose(new(-4, 6, 10));
        Assert.True(family.Hold(scene.Appearance, item => item.Key == 1 ? wholeWorld : armWorld));
        var target = WorldPose(new(1, 2, 3)); var components = new int[Rig.BoneCount]; target.Components = components;
        var placement = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(100, 50, -30);
        var targetWorld = Vector3.Transform(target.Position[3], placement);
        family.Accept(10);
        var first = family.Blend(0, target, placement, 10)!;
        Assert.True(Vector3.Distance(wholeWorld.Position[3], Vector3.Transform(first.Position[3], placement)) < .0001f);
        Assert.Same(components, first.Components); Assert.True(first.AbsolutePositions);
        var newLeg = family.Blend(12, target, placement, 10)!;
        Assert.True(Vector3.Distance(wholeWorld.Position[3], Vector3.Transform(newLeg.Position[3], placement)) < .0001f);
        var oldArm = family.Blend(10, target, placement, 10)!;
        Assert.True(Vector3.Distance(armWorld.Position[3], Vector3.Transform(oldArm.Position[3], placement)) < .0001f);
        var middle = family.Blend(0, target, placement, 10.06)!;
        Assert.True(Vector3.Distance(Vector3.Lerp(wholeWorld.Position[3], targetWorld, .5f), Vector3.Transform(middle.Position[3], placement)) < .001f);
        Assert.Same(target, family.Blend(0, target, placement, 10.121));
        Assert.Equal(new Vector3(1, 2, 3), target.Position[3]); Assert.Equal(new Vector3(12, 30, -8), wholeWorld.Position[3]);
    }

    [Fact]
    public void A_second_cut_starts_from_the_current_visible_blend_and_never_reuses_a_different_race_family()
    {
        using var scene = new Scene(); using var other = new Scene();
        var family = new CutPresentationFamily();
        var source = new CharacterInstance(1, scene.Appearance, Vector3.Zero, 0, []);
        family.Capture(new[] { new PresentedPiece(0, source) });
        family.Hold(scene.Appearance, _ => WorldPose(new(0, 20, 0))); family.Accept(1);
        var firstTarget = WorldPose(new(10, 0, 0));
        var visible = family.Blend(0, firstTarget, Matrix4x4.Identity, 1.06)!;
        family.Capture(new[] { new PresentedPiece(0, source with { Bones = visible }) });
        Assert.True(family.Hold(scene.Appearance, item => item.Bones!));
        var start = visible.Position[3];
        family.Accept(2);
        var second = family.Blend(12, WorldPose(new(-20, 2, 5)), Matrix4x4.Identity, 2)!;
        Assert.True(Vector3.Distance(start, second.Position[3]) < .0001f);
        Assert.False(family.Hold(other.Appearance, _ => throw new InvalidOperationException("Different races must stay hidden until ready.")));
        var empty = new CutPresentationFamily(); Assert.False(empty.Hold(scene.Appearance, _ => WorldPose(Vector3.Zero)));
    }

    [Fact]
    public void A_held_or_currently_displayed_stain_wrapper_survives_superseded_never_displayed_candidates()
    {
        var held = new RuntimeCharacterGeometry(); var unused = new RuntimeCharacterGeometry(); var next = new RuntimeCharacterGeometry();
        var wanted = new HashSet<RuntimeCharacterGeometry> { next };
        CutPresentationFamily.RetireUnused(new[] { held, unused, next }, wanted, geometry => ReferenceEquals(geometry, held));
        Assert.False(held.IsRetired); Assert.True(unused.IsRetired); Assert.False(next.IsRetired);
        CutPresentationFamily.RetireUnused(new[] { held }, wanted, _ => false); Assert.True(held.IsRetired);
    }

    [Fact]
    public void Held_complete_world_bones_keep_the_full_rotated_placement_independent_of_later_root_reanchoring()
    {
        using var scene = new Scene();
        var setup = new CharacterPoseSetup { Rig = Rig, GroundOffset = 2 };
        var rigs = new CharacterRigs(new GameDatabase(), _ => null, _ => null);
        var bones = WorldPose(new(1, 2, 3));
        var instance = new CharacterInstance(1, scene.Appearance, new(20, 5, -10), 0, [])
        { Bones = bones, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f), Scale = 1.2f };
        var expectedPlacement = CharacterRigs.Placement(setup, instance.Position, instance.Rotation.Value, instance.Scale);
        var world = CutPresentationFamily.WorldBones(instance, setup, rigs);
        Assert.True(Vector3.Distance(Vector3.Transform(bones.Position[3], expectedPlacement), world.Position[3]) < .0001f);
        Assert.All(world.Set, set => Assert.True(set)); Assert.Null(world.Components);
        bones.Position[3] = new(500); Assert.NotEqual(bones.Position[3], world.Position[3]);
    }

    static BonePose WorldPose(Vector3 position)
    {
        var pose = PhysicalPose();
        for (int bone = 0; bone < pose.Set.Length; bone++) pose.Position[bone] = position;
        return pose;
    }
}
