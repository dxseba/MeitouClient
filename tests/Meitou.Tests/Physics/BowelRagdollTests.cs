using System.Numerics;
using Meitou.Data.Physics;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Tests.Physics;

public class BowelRagdollTests
{
    sealed class Ground(bool wall = false) : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;

        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles)
        {
            if (wall && min.X <= 0 && max.X > 0)
                triangles.AddRange([new(0, -10, -10), new(0, 10, -10), new(0, 10, 10),
                    new(0, -10, -10), new(0, 10, 10), new(0, -10, 10)]);
            return true;
        }
    }

    static BowelRigGeometry Geometry() =>
        new([.. from row in Enumerable.Range(0, 3) from column in Enumerable.Range(0, 3)
            select new Vector3((column - 1) * 1.2f, (row - 1) * 1.2f, 0)], 0.3f);

    [Fact]
    public void Rest_skin_coordinates_follow_the_initial_world_rotation_without_double_translation()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        var at = new Vector3(17, 8, -31);
        var rotation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0.3f);
        Assert.True(scene.Add(7, geometry, at, rotation, Vector3.Zero));
        var pose = Assert.IsType<BowelPose>(scene.PoseOf(7));
        Assert.Equal(9, pose.Transforms.Length);
        Assert.InRange(Vector3.Distance(at, pose.Centre), 0, 0.0001f);
        for (int i = 0; i < geometry.RestNodes.Length; i++)
            Assert.InRange(Vector3.Distance(at + Vector3.Transform(geometry.RestNodes[i], rotation), Node(pose, i)), 0, 0.0001f);
        geometry.RestNodes[0] = new(float.NaN);
        AssertFinite(pose);
    }

    [Fact]
    public void Jointed_bowel_falls_deforms_on_contact_and_settles_above_the_ground()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        var rotation = Quaternion.CreateFromYawPitchRoll(0.2f, 0.3f, 0.4f);
        Assert.True(scene.Add(1, geometry, new(0, 8, 0), rotation, new(3, 0, 1)));
        float greatestEdgeChange = 0;
        var ground = new Ground();
        for (int tick = 0; tick < 60 * 13; tick++)
        {
            scene.Step(1f / 60, ground, tick);
            var pose = Assert.IsType<BowelPose>(scene.PoseOf(1));
            AssertFinite(pose);
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                {
                    int a = row * 3 + column;
                    foreach (int b in new[] { column < 2 ? a + 1 : a, row < 2 ? a + 3 : a })
                        if (a != b)
                            greatestEdgeChange = MathF.Max(greatestEdgeChange,
                                MathF.Abs(Vector3.Distance(Node(pose, a), Node(pose, b)) - 1.2f));
                }
        }
        var settled = Assert.IsType<BowelPose>(scene.PoseOf(1));
        Assert.True(settled.Centre.Y < 3, $"The bowel stayed at height {settled.Centre.Y}.");
        Assert.True(greatestEdgeChange > 0.015f, $"The nodes stayed rigid: greatest edge change {greatestEdgeChange}.");
        Assert.Equal(0, scene.Moving);
        for (int i = 0; i < settled.RestNodes.Length; i++)
            Assert.True(Node(settled, i).Y >= geometry.Radius * 0.45f, $"Node {i} passed through the floor.");
        var before = settled.Transforms.ToArray();
        var centre = settled.Centre;
        scene.Step(0.2f, ground, 1000);
        var after = Assert.IsType<BowelPose>(scene.PoseOf(1));
        Assert.Equal(centre, after.Centre);
        Assert.Equal(before, after.Transforms);
    }

    [Fact]
    public void Building_triangles_stop_a_thrown_bowel_before_it_crosses_a_wall()
    {
        using var scene = new BowelRagdollScene();
        Assert.True(scene.Add(2, Geometry(), new(-4, 5, 0), Quaternion.Identity, new(25, 0, 0)));
        float closestToWall = float.MinValue;
        var environment = new Ground(wall: true);
        for (int tick = 0; tick < 90; tick++)
        {
            scene.Step(1f / 120, environment, tick);
            var pose = Assert.IsType<BowelPose>(scene.PoseOf(2));
            AssertFinite(pose);
            for (int i = 0; i < pose.RestNodes.Length; i++)
            {
                float x = Node(pose, i).X;
                Assert.True(x <= 0.05f, $"Node {i} crossed the wall at x={x}.");
                closestToWall = MathF.Max(closestToWall, x);
            }
        }
        Assert.True(closestToWall > -1, $"The thrown bowel never reached the wall: x={closestToWall}.");
    }

    [Fact]
    public void Paused_or_invalid_elapsed_time_does_not_move_the_physical_pose()
    {
        using var scene = new BowelRagdollScene();
        Assert.True(scene.Add(3, Geometry(), new(0, 8, 0), Quaternion.Identity, Vector3.Zero));
        var pose = Assert.IsType<BowelPose>(scene.PoseOf(3));
        var before = pose.Transforms.ToArray();
        var centre = pose.Centre;
        var ground = new Ground();
        foreach (float dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            scene.Step(dt, ground, 1);
        var after = Assert.IsType<BowelPose>(scene.PoseOf(3));
        Assert.Equal(centre, after.Centre);
        Assert.Equal(before, after.Transforms);
        scene.Step(1f / 30, ground, 2);
        Assert.True(scene.PoseOf(3)!.Centre.Y < centre.Y);
    }

    [Fact]
    public void Capacity_removal_clear_and_disposal_release_the_slots_for_later_props()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        for (int i = 0; i < BowelRagdollScene.Capacity; i++)
            Assert.True(scene.Add(i, geometry, new(i * 10, 5, 0), Quaternion.Identity, Vector3.Zero));
        Assert.False(scene.Add(0, geometry, new(0, 5, 0), Quaternion.Identity, Vector3.Zero));
        Assert.False(scene.Add(1000, geometry, new(0, 5, 0), Quaternion.Identity, Vector3.Zero));
        Assert.Equal(BowelRagdollScene.Capacity, scene.Count);
        scene.Remove(0);
        Assert.Null(scene.PoseOf(0));
        Assert.True(scene.Add(1000, geometry, new(0, 5, 0), Quaternion.Identity, Vector3.Zero));
        scene.Clear();
        Assert.Equal(0, scene.Count);
        Assert.Equal(0, scene.Moving);
        Assert.True(scene.Add(0, geometry, new(0, 5, 0), Quaternion.Identity, Vector3.Zero));
        scene.Dispose();
        scene.Dispose();
        Assert.Equal(0, scene.Count);
        Assert.False(scene.Add(1, geometry, Vector3.Zero, Quaternion.Identity, Vector3.Zero));
    }

    [Fact]
    public void Invalid_geometry_and_nonfinite_launches_do_not_create_partial_ragdolls()
    {
        using var scene = new BowelRagdollScene();
        var valid = Geometry();
        BowelRigGeometry[] invalid =
        [
            new([], 0.3f), new(valid.RestNodes, 0), new(valid.RestNodes, -1), new(valid.RestNodes, float.NaN),
            new(valid.RestNodes, float.PositiveInfinity), new([.. valid.RestNodes.Take(8), new(float.NaN, 0, 0)], 0.3f),
        ];
        foreach (var geometry in invalid)
            Assert.False(scene.Add(1, geometry, new(0, 5, 0), Quaternion.Identity, Vector3.Zero));
        Assert.False(scene.Add(1, valid, new(float.NaN, 5, 0), Quaternion.Identity, Vector3.Zero));
        Assert.False(scene.Add(1, valid, new(0, 5, 0), Quaternion.Identity, new(float.PositiveInfinity, 0, 0)));
        Assert.False(scene.Add(1, valid, new(0, 5, 0), default, Vector3.Zero));
        Assert.Equal(0, scene.Count);
        Assert.Null(scene.PoseOf(1));
        Assert.True(scene.Add(1, valid, new(0, 5, 0), Quaternion.Identity, Vector3.Zero));
    }

    [Fact]
    public void A_restored_flat_bowel_starts_at_rest_and_ignores_the_old_launch_impulse()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        var flat = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        Assert.True(scene.Add(4, geometry, new(2, geometry.Radius, -3), flat, new(25, 50, -13), resting: true));
        Assert.Equal(0, scene.Moving);
        var ground = new Ground();
        scene.Step(0, ground, 0);
        var initial = Assert.IsType<BowelPose>(scene.PoseOf(4));
        AssertFinite(initial);
        for (int i = 0; i < initial.RestNodes.Length; i++)
            Assert.InRange(MathF.Abs(Node(initial, i).Y - geometry.Radius), 0, 0.00001f);
        var transforms = initial.Transforms.ToArray();
        var centre = initial.Centre;
        for (int tick = 1; tick <= 20; tick++) scene.Step(0.2f, ground, tick);
        var after = Assert.IsType<BowelPose>(scene.PoseOf(4));
        Assert.Equal(centre, after.Centre);
        Assert.Equal(transforms, after.Transforms);
        Assert.Equal(0, scene.Moving);
    }

    [Fact]
    public void Initial_below_floor_placement_is_corrected_even_while_other_props_remain_paused()
    {
        using var scene = new BowelRagdollScene();
        var geometry = Geometry();
        var flat = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        Assert.True(scene.Add(5, geometry, new(-4, -2, 0), flat, Vector3.Zero));
        Assert.True(scene.Add(6, geometry, new(5, 8, 0), Quaternion.Identity, new(9, -3, 4)));
        var paused = Assert.IsType<BowelPose>(scene.PoseOf(6));
        var pausedTransforms = paused.Transforms.ToArray();
        var pausedCentre = paused.Centre;
        Assert.True(scene.PoseOf(5)!.Centre.Y < 0);
        scene.Step(0, new Ground(), 0);
        var corrected = Assert.IsType<BowelPose>(scene.PoseOf(5));
        AssertFinite(corrected);
        for (int i = 0; i < corrected.RestNodes.Length; i++)
        {
            var point = Node(corrected, i);
            var expected = new Vector3(-4, -2, 0) + Vector3.Transform(geometry.RestNodes[i], flat);
            Assert.True(point.Y >= geometry.Radius - 0.00001f);
            Assert.InRange(MathF.Abs(point.X - expected.X) + MathF.Abs(point.Z - expected.Z), 0, 0.00001f);
        }
        var unchanged = Assert.IsType<BowelPose>(scene.PoseOf(6));
        Assert.Equal(pausedCentre, unchanged.Centre);
        Assert.Equal(pausedTransforms, unchanged.Transforms);
        Assert.Equal(2, scene.Moving);
    }

    static Vector3 Node(BowelPose pose, int i) => Vector3.Transform(pose.RestNodes[i], pose.Transforms[i]) + pose.Centre;

    static void AssertFinite(BowelPose pose)
    {
        Assert.True(Finite(pose.Centre));
        Assert.All(pose.RestNodes, point => Assert.True(Finite(point)));
        foreach (var matrix in pose.Transforms)
            Assert.All(new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44 },
                value => Assert.True(float.IsFinite(value)));
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
