using System.Numerics;
using Meitou.Game;

namespace Meitou.Tests.Physics;

public class BowelFallbackTests
{
    [Fact]
    public void The_simple_bowel_fall_keeps_every_rotated_mesh_corner_above_the_floor_and_stops_on_contact()
    {
        var extent = new Vector3(1.2f, 1.6f, 0.65f);
        foreach (var rotation in new[]
        {
            Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
            Quaternion.CreateFromYawPitchRoll(0.3f, 0.7f, 1.1f),
            Quaternion.CreateFromYawPitchRoll(-1.5f, -0.6f, 2.2f),
        })
        {
            var fall = new GameHost.GutFall
            {
                Position = new(2, 12, -3), Velocity = new(1, -2, 0.5f), Rotation = rotation,
                HalfExtent = extent, Floor = 4,
            };
            for (int tick = 0; tick < 360; tick++)
            {
                GameHost.StepGut(fall, 1f / 120);
                foreach (var corner in Corners(extent))
                {
                    var world = Vector3.Transform(corner, fall.Rotation) + fall.Position;
                    Assert.True(world.Y >= fall.Floor - 0.00001f,
                        $"A rotated bowel corner crossed the floor: {world.Y} < {fall.Floor}.");
                }
            }
            Assert.True(fall.Resting);
            var position = fall.Position;
            var turn = fall.Rotation;
            GameHost.StepGut(fall, 0.5f);
            Assert.Equal(position, fall.Position);
            Assert.Equal(turn, fall.Rotation);
            Assert.Equal(Vector3.Zero, fall.Velocity);
        }
    }

    [Fact]
    public void A_paused_simple_bowel_keeps_its_pose_unless_its_initial_bounds_intersect_the_floor()
    {
        var fall = new GameHost.GutFall
        {
            Position = new(0, 5, 0), Velocity = new(1, -2, 3),
            Rotation = Quaternion.CreateFromYawPitchRoll(0.2f, 0.4f, 0.6f),
            HalfExtent = new(1.2f, 1.6f, 0.65f), Floor = 0,
        };
        var before = (fall.Position, fall.Rotation, fall.Velocity, fall.Age);
        GameHost.StepGut(fall, 0);
        Assert.Equal(before, (fall.Position, fall.Rotation, fall.Velocity, fall.Age));
        fall.Position.Y = 0;
        GameHost.StepGut(fall, 0);
        Assert.True(fall.Resting);
        foreach (var corner in Corners(fall.HalfExtent))
            Assert.True((Vector3.Transform(corner, fall.Rotation) + fall.Position).Y >= -0.00001f);
        Assert.Equal(0, fall.Age);
    }

    static IEnumerable<Vector3> Corners(Vector3 extent) =>
        from x in new[] { -1, 1 }
        from y in new[] { -1, 1 }
        from z in new[] { -1, 1 }
        select new Vector3(x * extent.X, y * extent.Y, z * extent.Z);
}
