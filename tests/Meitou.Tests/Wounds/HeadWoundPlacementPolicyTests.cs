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
    [Fact]
    public void Footprint_scales_grow_smoothly_with_force_and_preserve_the_surface_frame()
    {
        var approved = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3)!.Value;
        Assert.Equal(1, approved.LengthScale);
        Assert.Equal(1, approved.WidthScale);
        float length = 0, width = 0;
        foreach (float force in new[] { 15f, 20, 30, 34.99f, 35, 35.01f, 45, 60, 100 })
        {
            var frame = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3, force)!.Value;
            Assert.True(frame.IsValid);
            Assert.Equal(approved.Point, frame.Point);
            Assert.Equal(approved.Normal, frame.Normal);
            Assert.Equal(approved.Tangent, frame.Tangent);
            Assert.InRange(frame.LengthScale, 0.9f, 1.2f);
            Assert.InRange(frame.WidthScale, 0.85f, 1.3f);
            Assert.True(frame.LengthScale >= length && frame.WidthScale >= width);
            (length, width) = (frame.LengthScale, frame.WidthScale);
        }
        var before = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3, 34.99f)!.Value;
        var after = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3, 35.01f)!.Value;
        Assert.InRange(after.LengthScale - before.LengthScale, 0, 0.001f);
        Assert.InRange(after.WidthScale - before.WidthScale, 0, 0.001f);
        Assert.Null(HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3, float.NaN));
    }

    [Fact]
    public void Later_impact_enlarges_the_footprint_without_moving_or_rotating_the_opening()
    {
        var opening = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3, 20, 1234)!.Value;
        var larger = HeadWoundPlacementPolicy.Enlarge(opening, 60);
        Assert.Equal(opening.Point, larger.Point);
        Assert.Equal(opening.Normal, larger.Normal);
        Assert.Equal(opening.Tangent, larger.Tangent);
        Assert.True(larger.LengthScale > opening.LengthScale && larger.WidthScale > opening.WidthScale);
        Assert.Equal(larger, HeadWoundPlacementPolicy.Enlarge(larger, 15));
        Assert.Equal(larger, HeadWoundPlacementPolicy.Enlarge(larger, float.PositiveInfinity));
    }

    [Fact]
    public void Opening_variation_is_repeatable_bounded_and_fixed_in_victim_space()
    {
        var baseline = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3)!.Value;
        foreach (ulong variation in new[] { 1UL, 987654321UL, ulong.MaxValue })
        {
            var frame = HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3, 30, variation)!.Value;
            Assert.Equal(frame, HeadWoundPlacementPolicy.Estimate(new(0, -1), 0, 3, 30, variation));
            Assert.True(frame.IsValid);
            Assert.InRange(MathF.Abs(frame.Point.Y - baseline.Point.Y), 0, 0.06001f);
            Assert.True(Vector3.Dot(frame.Tangent, baseline.Tangent) >= MathF.Cos(MathF.PI / 18) - 1e-6f);
            Assert.InRange(MathF.Abs(Vector3.Dot(frame.Normal, frame.Tangent)), 0, 1e-6f);
            var turned = HeadWoundPlacementPolicy.Estimate(new(-1, 0), MathF.PI / 2, 3, 30, variation)!.Value;
            Assert.InRange(Vector3.Distance(frame.Normal, turned.Normal), 0, 1e-6f);
            Assert.InRange(Vector3.Distance(frame.Tangent, turned.Tangent), 0, 1e-6f);
            Assert.Equal(frame.Point, turned.Point);
        }
    }

}
