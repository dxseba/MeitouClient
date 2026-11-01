using System.Numerics;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Wounds;

public class HeadWoundPlacementPolicyTests
{
    [Fact]
    public void Incoming_sides_select_front_back_and_both_sides_in_victim_space()
    {
        (Vector2 Travel, Vector3 Expected)[] cases = [(new(0, -1), Vector3.UnitZ), (new(0, 1), -Vector3.UnitZ), (new(1, 0), -Vector3.UnitX), (new(-1, 0), Vector3.UnitX)];
        foreach (var (travel, expected) in cases)
        {
            var frame = HeadWoundPlacementPolicy.Estimate(travel, 0, 3)!.Value;
            Assert.True(frame.IsValid);
            Assert.True(Vector3.Dot(frame.Normal, expected) > 0.98f);
            Assert.InRange(MathF.Abs(Vector3.Dot(frame.Normal, frame.Tangent)), 0, 1e-6f);
        }
    }

    [Fact]
    public void Turning_the_victim_and_incoming_travel_together_keeps_the_bind_frame()
    {
        var original = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3)!.Value;
        foreach (float yaw in new[] { 0.3f, MathF.PI / 2, MathF.PI, 5.7f })
        {
            var travel = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
            var turned = HeadWoundPlacementPolicy.Estimate(travel * 40, yaw, 3)!.Value;
            Assert.InRange(Vector3.Distance(original.Normal, turned.Normal), 0, 1e-6f);
            Assert.InRange(Vector3.Distance(original.Tangent, turned.Tangent), 0, 1e-6f);
            Assert.Equal(original.Point, turned.Point);
        }
    }

    [Fact]
    public void Blade_directions_have_distinct_axes_on_the_same_surface()
    {
        var down = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 1)!.Value;
        var left = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 2)!.Value;
        var right = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3)!.Value;
        Assert.True(down.Tangent.Y > 0.98f);
        Assert.Equal(down.Tangent, HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 4)!.Value.Tangent);
        Assert.True(left.Tangent.X < -0.8f);
        Assert.True(right.Tangent.X > 0.8f);
        Assert.Equal(down.Normal, left.Normal);
        Assert.Equal(left.Normal, right.Normal);
    }

    [Fact]
    public void Rear_codes_keep_the_existing_down_and_right_axis_convention()
    {
        Assert.Equal(HeadWoundPlacementPolicy.Estimate(new(0, 1), 0, 1), HeadWoundPlacementPolicy.Estimate(new(0, 1), 0, 7));
        Assert.Equal(HeadWoundPlacementPolicy.Estimate(new(0, 1), 0, 3), HeadWoundPlacementPolicy.Estimate(new(0, 1), 0, 9));
    }

    [Fact]
    public void Unknown_or_invalid_travel_uses_the_legacy_fallback_instead_of_a_bad_frame()
    {
        Assert.Null(HeadWoundPlacementPolicy.Estimate(Vector2.Zero, 0, 3));
        Assert.Null(HeadWoundPlacementPolicy.Estimate(new(float.NaN, 1), 0, 3));
        Assert.Null(HeadWoundPlacementPolicy.Estimate(new(1, 0), float.PositiveInfinity, 3));
        Assert.Null(HeadWoundPlacementPolicy.Estimate(new(float.MaxValue, float.MaxValue), 0, 3));
    }
}
