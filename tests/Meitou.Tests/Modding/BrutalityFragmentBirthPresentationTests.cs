using System.Numerics;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using IRagdollEnvironment = Meitou.Simulation.Ragdolls.IRagdollEnvironment;

namespace Meitou.Tests.Modding;

public sealed class BrutalityFragmentBirthPresentationTests
{
    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.17f)]
    public void Pending_whole_body_keeps_complete_birth_pose_and_placement_after_its_physical_root_moves(float yaw)
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01", Position = new(0, 9, 0) });
        skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 L UpperArm", Position = new(3, 1, 0) });
        skeleton.Bones.Add(new() { Handle = 2, Parent = 1, Name = "Bip01 L Forearm", Position = new(0, -3, 0) });
        skeleton.Bones.Add(new() { Handle = 3, Parent = 2, Name = "Bip01 L Hand", Position = new(0, -2, 0) });
        var rig = new SkeletonRig(skeleton);
        var setup = new CharacterPoseSetup { Rig = rig };
        var placement = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(120, 50, -90);
        var world = FragmentPhysicsSystem.PoseWorld(setup, [], null, placement);
        var expected = world.ToArray();
        var appearance = new CharacterAppearance { Race = null! };
        var birth = Assert.IsType<FragmentBirthPresentation>(FragmentPhysicsSystem.CaptureBirthPresentation(appearance, world, placement));
        var definition = new RagdollDefinition
        {
            Root = 0, BodyOfBone = [0, -1, -1, -1], Joined = [0], Joints = [],
            Bodies = [new()
            {
                Name = "Bip01", Bone = 0, Type = PhysicsShapeType.Box, Size = new(2), Mass = 10,
                Bind = new(rig.BindPosition[0], Quaternion.Identity), BoneInBody = RigidTransform.Identity,
            }],
        };
        using var scene = new AdvancedFragmentScene();
        var retained = scene.Create(definition, bone => world[bone], new(30, 0, 0), Vector3.Zero, 1, world);
        for (int tick = 0; tick < 3; tick++) scene.Step(1f / 30, new Flat(), tick);
        Assert.True(Vector3.Distance(expected[0].Position, retained.Pose[0].Position) > .1f);
        _ = scene.Publish(retained, rig, Matrix4x4.CreateTranslation(retained.Pose[0].Position));
        world[3] = new(new(900, 900, 900), Quaternion.Identity);

        Assert.Same(appearance, birth.Appearance);
        Assert.Equal(placement, birth.Placement);
        Assert.True(birth.Bones.AbsolutePositions);
        Assert.Null(birth.Bones.Components);
        Assert.All(birth.Bones.Set, set => Assert.True(set));
        var shown = FragmentPhysicsSystem.PoseWorld(setup, [], birth.Bones, birth.Placement);
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            Assert.InRange(Vector3.Distance(expected[bone].Position, shown[bone].Position), 0, .0001f);
            Assert.InRange(MathF.Abs(1 - MathF.Abs(Quaternion.Dot(expected[bone].Rotation, shown[bone].Rotation))), 0, .0001f);
        }
    }

    [Fact]
    public void Restored_fragments_do_not_publish_a_fabricated_unsplit_birth_body()
    {
        var pose = new[] { new RigidTransform(new(0, 30, 0), Quaternion.Identity) };
        Assert.Null(FragmentPhysicsSystem.CaptureBirthPresentation(new CharacterAppearance { Race = null! }, pose,
            Matrix4x4.CreateTranslation(100, 0, 100), restoring: true));
    }

}
