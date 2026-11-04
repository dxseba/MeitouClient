using System.Numerics;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Moved_and_carried_fragment_skin_uses_its_published_frame_instead_of_the_new_native_root(bool carried)
    {
        using var scene = new Scene();
        var placement = Matrix4x4.CreateRotationZ(carried ? 1.1f : 0) * Matrix4x4.CreateTranslation(30, carried ? 516 : 500, -20);
        Matrix4x4.Invert(placement, out var inverse);
        var worldBone = new Vector3(32, carried ? 517 : 502, -19);
        var pose = PhysicalPose(); pose.Position[1] = Vector3.Transform(worldBone, inverse);
        pose.Rotation[1] = Quaternion.Inverse(Quaternion.CreateFromRotationMatrix(placement));
        var frame = new GoreCharacterFrame(default, scene.Appearance, State(), pose, []) { Placement = placement };
        var native = new CharacterInstance(1, scene.Appearance, new(-50, 540, 70), -.7f, [])
        { World = Matrix4x4.CreateRotationY(-.7f) * Matrix4x4.CreateTranslation(-50, 540, 70) };
        var shown = RenderBridge.WithPhysicalPlacement(native, null, frame, 1);
        var skin = new Matrix4x4[Rig.BoneCount]; new PoseScratch(Rig).Pose(null, [], skin, pose);
        var point = Rig.BindPosition[1] + new Vector3(.3f, -.4f, .2f);
        var drawn = Vector3.Transform(Vector3.Transform(point, skin[1]), shown.World!.Value);
        Assert.True(Vector3.Distance(drawn, worldBone + new Vector3(.3f, -.4f, .2f)) < .0001f);
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.Transform(point, skin[1]), native.World!.Value), drawn) > 20);
        // Detached non-proxy pieces use the very same physical frame, even when the retained body is animated.
        var detached = RenderBridge.WithPhysicalPlacement(native, null, frame with { Retained = null }, 1, detached: true);
        Assert.Equal(shown.World, detached.World);
    }

    [Fact]
    public void Recovery_return_frame_keeps_old_physical_placement_until_the_pose_returns_to_native_animation()
    {
        using var scene = new Scene();
        var oldFrame = Matrix4x4.CreateTranslation(20, 503, -10);
        var native = new CharacterInstance(1, scene.Appearance, new(40, 500, 20), .5f, []) { World = null };
        var pose = PhysicalPose(); pose.Position[1] = new(0, 1, 0);
        var physical = new GoreCharacterFrame(default, scene.Appearance, State(), pose, []) { Placement = oldFrame };
        var shown = RenderBridge.WithPhysicalPlacement(native, null, physical, 1);
        Assert.True(Vector3.Distance(Vector3.Transform(pose.Position[1], shown.World!.Value), new(20, 504, -10)) < .0001f);
        var animated = RenderBridge.WithPhysicalPlacement(native, physical, physical with { Retained = null }, 1);
        Assert.Null(animated.World); Assert.Equal(native.Position, animated.Position);
    }
}
