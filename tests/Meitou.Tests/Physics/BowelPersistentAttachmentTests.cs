using System.Numerics;
using Meitou.Data.Physics;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Tests.Physics;

public class BowelPersistentAttachmentTests
{
    sealed class Ground : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    static BowelRigGeometry Geometry() => new([.. from row in Enumerable.Range(0, 3)
        from column in Enumerable.Range(0, 3) select new Vector3((column - 1) * 1.2f, (row - 1) * 1.2f, 0)], 0.3f);

    [Fact]
    public void Pickup_motion_keeps_retained_tissue_attached_without_a_long_tether()
    {
        using var scene = new BowelRagdollScene();
        Assert.True(scene.Add(1, Geometry(), new(0, 8, 0), Quaternion.Identity, Vector3.Zero,
            pinNode: 0, preserveAttachment: true));
        var pose = Assert.IsType<BowelPose>(scene.PoseOf(1));
        var points = Enumerable.Range(0, BowelRagdollScene.NodeCount).Select(i => Node(pose, i)).ToArray();
        var shift = new Vector3(12, 4, -2);
        Assert.True(scene.MoveAttachment(1, points[0] + shift));
        Assert.True(scene.IsAttached(1));
        var moved = Assert.IsType<BowelPose>(scene.PoseOf(1));
        for (int i = 0; i < points.Length; i++)
            Assert.InRange(Vector3.Distance(points[i] + shift, Node(moved, i)), 0, 0.00001f);
        var anchor = Node(moved, 0);
        var ground = new Ground();
        for (int tick = 0; tick < 60 * 13; tick++)
        {
            anchor.X += 0.002f;
            Assert.True(scene.MoveAttachment(1, anchor));
            scene.Step(1f / 60, ground, tick);
            Assert.Equal(1, scene.Moving);
            Assert.True(scene.IsAttached(1));
        }
        moved = Assert.IsType<BowelPose>(scene.PoseOf(1));
        var beforeBreak = Enumerable.Range(0, BowelRagdollScene.NodeCount).Select(i => Node(moved, i)).ToArray();
        Assert.False(scene.MoveAttachment(1, Node(moved, 0) + Vector3.UnitX * 50));
        Assert.False(scene.IsAttached(1));
        for (int i = 0; i < points.Length; i++) Assert.Equal(beforeBreak[i], Node(scene.PoseOf(1)!, i));
        scene.Step(1f / 30, new Ground(), 1);
        AssertFinite(scene.PoseOf(1)!);
    }

    [Fact]
    public void Settled_retained_tissue_stays_attached_without_updates_then_wakes_on_motion_and_release()
    {
        using var scene = new BowelRagdollScene();
        Assert.True(scene.Add(2, Geometry(), new(0, 8, 0), Quaternion.Identity, Vector3.Zero,
            pinNode: 0, preserveAttachment: true));
        var ground = new Ground();
        for (int tick = 0; tick < 60 * 13; tick++) scene.Step(1f / 60, ground, tick);
        Assert.True(scene.IsAttached(2));
        Assert.Equal(0, scene.Moving);
        var held = Assert.IsType<BowelPose>(scene.PoseOf(2));
        var anchor = Node(held, 0);
        var transforms = held.Transforms.ToArray();
        for (int tick = 0; tick < 30; tick++)
        {
            Assert.True(scene.MoveAttachment(2, anchor));
            scene.Step(0, ground, 800 + tick);
            Assert.Same(held, scene.PoseOf(2));
        }
        Assert.Equal(transforms, held.Transforms);
        Assert.True(scene.MoveAttachment(2, anchor + new Vector3(0.2f, 0.1f, 0)));
        Assert.Equal(1, scene.Moving);
        scene.Step(1f / 60, ground, 831);
        Assert.InRange(Vector3.Distance(anchor + new Vector3(0.2f, 0.1f, 0), Node(scene.PoseOf(2)!, 0)), 0, 0.0001f);
        for (int tick = 832; tick < 832 + 60 * 13; tick++) scene.Step(1f / 60, ground, tick);
        Assert.True(scene.IsAttached(2));
        Assert.Equal(0, scene.Moving);
        var beforeRelease = Assert.IsType<BowelPose>(scene.PoseOf(2));
        var releasePoint = Node(beforeRelease, 0);
        Assert.True(scene.ReleaseAttachment(2));
        Assert.Equal(1, scene.Moving);
        Assert.False(scene.IsAttached(2));
        Assert.Same(beforeRelease, scene.PoseOf(2));
        for (int tick = 0; tick < 30; tick++) scene.Step(1f / 60, ground, 1700 + tick);
        AssertFinite(scene.PoseOf(2)!);
        Assert.True(Node(scene.PoseOf(2)!, 0).Y < releasePoint.Y - 0.5f);
    }

    [Fact]
    public void A_full_crowd_settles_with_retained_attachments_and_only_changed_rigs_resume_physics()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        for (int i = 0; i < BowelRagdollScene.Capacity; i++)
            Assert.True(scene.Add(i, geometry, new((i % 8) * 6, 1.5f, (i / 8) * 6), Quaternion.Identity, Vector3.Zero,
                pinNode: 0, preserveAttachment: true));
        var ground = new Ground();
        for (int tick = 0; tick < 60 * 13; tick++) scene.Step(1f / 60, ground, tick);
        Assert.Equal(0, scene.Moving);
        var held = Enumerable.Range(0, BowelRagdollScene.Capacity).Select(i => scene.PoseOf(i)!).ToArray();
        for (int i = 0; i < held.Length; i++)
        {
            Assert.True(scene.IsAttached(i));
            AssertFinite(held[i]);
            Assert.True(scene.MoveAttachment(i, Node(held[i], 0)));
        }
        scene.Step(0.2f, ground, 800);
        for (int i = 0; i < held.Length; i++) Assert.Same(held[i], scene.PoseOf(i));
        Assert.Equal(0, scene.Moving);
        Assert.True(scene.ReleaseAttachment(0));
        Assert.True(scene.MoveAttachment(1, Node(held[1], 0) + new Vector3(0.2f, 0.1f, 0)));
        Assert.Equal(2, scene.Moving);
        scene.Step(1f / 60, ground, 801);
        for (int i = 0; i < held.Length; i++) AssertFinite(scene.PoseOf(i)!);
        for (int i = 2; i < held.Length; i++) Assert.Same(held[i], scene.PoseOf(i));
        Assert.Equal(BowelRagdollScene.Capacity, scene.Count);
    }

    static Vector3 Node(BowelPose pose, int node) => Vector3.Transform(pose.RestNodes[node], pose.Transforms[node]) + pose.Centre;

    static void AssertFinite(BowelPose pose)
    {
        for (int i = 0; i < BowelRagdollScene.NodeCount; i++)
        {
            var at = Node(pose, i);
            Assert.True(float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z));
            Assert.InRange(Vector3.Distance(at, pose.Centre), 0, 12);
        }
    }
}
