using System.Numerics;
using Meitou.Data.Physics;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Tests.Physics;

public class BowelAttachmentTests
{
    sealed class Ground : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    static BowelRigGeometry Geometry() => new([.. from row in Enumerable.Range(0, 3)
        from column in Enumerable.Range(0, 3) select new Vector3((column - 1) * 1.2f, (row - 1) * 1.2f, 0)], 0.3f);

    [Fact]
    public void The_pinned_edge_follows_the_cut_while_the_free_tissue_bends_under_gravity()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        Assert.True(scene.Add(1, geometry, new(0, 8, 0), Quaternion.Identity, Vector3.Zero, pinNode: 0));
        var anchor = new Vector3(0, 8, 0) + geometry.RestNodes[0];
        var ground = new Ground();
        for (int i = 1; i <= 150; i++)
        {
            var target = anchor + Vector3.UnitX * (i * 0.005f);
            Assert.True(scene.MoveAttachment(1, target));
            scene.Step(1f / 60, ground, i);
            var pose = Assert.IsType<BowelPose>(scene.PoseOf(1));
            Assert.InRange(Vector3.Distance(target, Node(pose, 0)), 0, 0.0001f);
            AssertFinite(pose);
        }
        var final = Assert.IsType<BowelPose>(scene.PoseOf(1));
        Assert.True(Node(final, 8).Y < Node(final, 0).Y - 0.4f);
        Assert.True(scene.IsAttached(1));
        Assert.Equal(1, scene.Moving);
    }

    [Fact]
    public void Pause_preserves_the_attached_pose_and_release_has_no_position_jump()
    {
        using var scene = new BowelRagdollScene();
        Assert.True(scene.Add(2, Geometry(), new(0, 8, 0), Quaternion.Identity, Vector3.Zero, pinNode: 0));
        var ground = new Ground();
        for (int i = 0; i < 30; i++) scene.Step(1f / 60, ground, i);
        var pose = Assert.IsType<BowelPose>(scene.PoseOf(2));
        var anchor = Node(pose, 0) + new Vector3(0.1f, 0, 0);
        Assert.True(scene.MoveAttachment(2, anchor));
        pose = Assert.IsType<BowelPose>(scene.PoseOf(2));
        var before = pose.Transforms.ToArray();
        var centre = pose.Centre;
        for (int i = 0; i < 20; i++) scene.Step(0, ground, i + 30);
        Assert.Equal(centre, scene.PoseOf(2)!.Centre);
        Assert.Equal(before, scene.PoseOf(2)!.Transforms);
        Assert.True(scene.ReleaseAttachment(2));
        Assert.False(scene.IsAttached(2));
        Assert.Equal(centre, scene.PoseOf(2)!.Centre);
        Assert.Equal(before, scene.PoseOf(2)!.Transforms);
        scene.Step(1f / 60, ground, 50);
        var released = Assert.IsType<BowelPose>(scene.PoseOf(2));
        AssertFinite(released);
        Assert.True(Node(released, 0).X > anchor.X);
        for (int i = 51; i < 80; i++) scene.Step(1f / 60, ground, i);
        Assert.True(Node(scene.PoseOf(2)!, 0).Y < anchor.Y - 0.5f);
    }

    [Fact]
    public void Fast_motion_or_excessive_strain_releases_the_pin_without_teleporting_the_tissue()
    {
        using var scene = new BowelRagdollScene();
        for (int i = 0; i < 2; i++)
        {
            long key = i + 3;
            Assert.True(scene.Add(key, Geometry(), new(i * 20, 8, 0), Quaternion.Identity, Vector3.Zero, pinNode: 0));
            var pose = Assert.IsType<BowelPose>(scene.PoseOf(key));
            var anchor = Node(pose, 0);
            Assert.False(scene.MoveAttachment(key, new(float.NaN)));
            Assert.True(scene.IsAttached(key));
            Assert.False(scene.MoveAttachment(key, anchor + Vector3.UnitX * (i == 0 ? 9 : -7)));
            Assert.False(scene.IsAttached(key));
            Assert.Equal(anchor, Node(scene.PoseOf(key)!, 0));
            AssertFinite(scene.PoseOf(key)!);
        }
    }

    [Fact]
    public void A_full_crowd_of_attached_and_released_tissue_remains_finite_and_bounded()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        for (int i = 0; i < BowelRagdollScene.Capacity; i++)
            Assert.True(scene.Add(i, geometry, new((i % 8) * 6, 8, (i / 8) * 6), Quaternion.Identity, Vector3.Zero, pinNode: 0));
        Assert.False(scene.Add(100, geometry, new(0, 8, 0), Quaternion.Identity, Vector3.Zero, pinNode: 0));
        var ground = new Ground();
        for (int tick = 0; tick < 90; tick++)
        {
            if (tick == 30)
                for (int i = 0; i < BowelRagdollScene.Capacity; i++) Assert.True(scene.ReleaseAttachment(i));
            scene.Step(1f / 60, ground, tick);
            for (int i = 0; i < BowelRagdollScene.Capacity; i++)
            {
                var pose = Assert.IsType<BowelPose>(scene.PoseOf(i));
                AssertFinite(pose);
                for (int node = 0; node < BowelRagdollScene.NodeCount; node++)
                    Assert.InRange(Vector3.Distance(Node(pose, node), new Vector3((i % 8) * 6, 4, (i / 8) * 6)), 0, 12);
            }
        }
        Assert.Equal(BowelRagdollScene.Capacity, scene.Count);
        Assert.False(scene.IsAttached(0));
    }

    static Vector3 Node(BowelPose pose, int node) => Vector3.Transform(pose.RestNodes[node], pose.Transforms[node]) + pose.Centre;

    static void AssertFinite(BowelPose pose)
    {
        for (int i = 0; i < BowelRagdollScene.NodeCount; i++)
        {
            var at = Node(pose, i);
            Assert.True(float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z));
        }
    }
}
