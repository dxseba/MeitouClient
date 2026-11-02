using System.Numerics;
using Meitou.Rendering;

namespace Meitou.Tests.Characters;

public class RobotDischargeGeometryTests
{
    static RobotDischargeAnchor Source => new(new(3, 100, 5), 12.3f, 25, 123, 1.5f);

    [Fact]
    public void Electrical_arcs_and_warm_sparks_stay_finite_and_follow_independently_moved_fragment_anchors()
    {
        var offset = new Vector3(100, -12, 40);
        RobotDischargeAnchor[] anchors = [Source, Source with { Position = Source.Position + offset }];
        var output = new RobotDischargeStroke[RobotDischargeGeometry.QuadLimit];
        int count = RobotDischargeGeometry.Build(anchors, output);
        Assert.InRange(count, 4, RobotDischargeGeometry.QuadLimit);
        var first = output.Take(count / 2).ToArray();
        var second = output.Skip(count / 2).Take(count / 2).ToArray();
        Assert.Contains(first, stroke => stroke.Arc);
        Assert.Contains(first, stroke => !stroke.Arc);
        Assert.All(output.Take(count), CheckStroke);
        Assert.Equal(first.Length, second.Length);
        for (int index = 0; index < first.Length; index++)
        {
            Assert.InRange(Vector3.Distance(second[index].Head - first[index].Head, offset), 0, 1e-4f);
            Assert.InRange(Vector3.Distance(second[index].Tail - first[index].Tail, offset), 0, 1e-4f);
            Assert.Equal(first[index].Arc, second[index].Arc);
            Assert.Equal(first[index].Colour, second[index].Colour);
        }

        var paused = output.Take(count).ToArray();
        for (int frame = 0; frame < 100; frame++)
        {
            Assert.Equal(count, RobotDischargeGeometry.Build(anchors, output));
            Assert.Equal(paused, output.Take(count).ToArray());
        }
    }

    [Fact]
    public void The_short_circuit_remains_visible_after_twenty_seconds_then_fades_and_stops_at_expiry()
    {
        var output = new RobotDischargeStroke[RobotDischargeGeometry.QuadLimit];
        int early = RobotDischargeGeometry.Build([Source with { AgeSeconds = 1 }], output);
        Assert.True(early > 0);
        float bright = output.Take(early).Sum(s => s.Colour.W);
        foreach (float age in new[] { 20, 22, 24.9f })
        {
            int count = RobotDischargeGeometry.Build([Source with { AgeSeconds = age }], output);
            Assert.True(count > 0, $"The discharge disappeared at age {age} before its 25-second lifetime.");
            Assert.All(output.Take(count), CheckStroke);
        }
        int last = RobotDischargeGeometry.Build([Source with { AgeSeconds = 24.9f }], output);
        Assert.True(output.Take(last).Sum(s => s.Colour.W) < bright * .05f);
        Assert.Equal(0, RobotDischargeGeometry.Build([Source with { AgeSeconds = 25 }], output));
        Assert.Equal(0, RobotDischargeGeometry.Build([Source with { AgeSeconds = 30 }], output));
    }

    [Fact]
    public void Many_robot_pieces_and_tiny_output_spans_cannot_exceed_the_anchor_or_quad_budget()
    {
        var anchors = Enumerable.Range(0, 200).Select(index => new RobotDischargeAnchor(new(index * 30, 0, 0), 4, 25, (uint)index)).ToArray();
        var output = new RobotDischargeStroke[RobotDischargeGeometry.QuadLimit + 10];
        int count = RobotDischargeGeometry.Build(anchors, output);
        Assert.InRange(count, RobotDischargeGeometry.AnchorLimit, RobotDischargeGeometry.QuadLimit);
        Assert.All(output.Take(count), stroke =>
        {
            CheckStroke(stroke);
            int owner = (int)MathF.Round(stroke.Head.X / 30);
            Assert.InRange(owner, 0, RobotDischargeGeometry.AnchorLimit - 1);
            Assert.InRange(Vector3.Distance(stroke.Head, anchors[owner].Position), 0, 12);
        });
        var sentinel = new RobotDischargeStroke(new(-1, -2, -3), new(-4, -5, -6), -1, new(-1), false);
        var small = Enumerable.Repeat(sentinel, 4).ToArray();
        Assert.InRange(RobotDischargeGeometry.Build([Source], small.AsSpan(1, 2)), 1, 2);
        Assert.Equal(sentinel, small[0]);
        Assert.Equal(sentinel, small[3]);
        Assert.Equal(0, RobotDischargeGeometry.Build([Source], []));

        count = RobotDischargeGeometry.Build([Source with { Position = new(float.MaxValue) }], output);
        Assert.InRange(count, 0, RobotDischargeGeometry.QuadLimit);
        Assert.All(output.Take(count), CheckStroke);
    }

    [Fact]
    public void Invalid_or_expired_sources_never_write_invalid_effect_geometry()
    {
        RobotDischargeAnchor[] invalid =
        [
            Source with { Position = new(float.NaN, 0, 0) }, Source with { Position = new(0, float.PositiveInfinity, 0) },
            Source with { AgeSeconds = -1 }, Source with { AgeSeconds = float.NaN }, Source with { AgeSeconds = float.PositiveInfinity },
            Source with { DurationSeconds = 0 }, Source with { DurationSeconds = float.NaN },
            Source with { DurationSeconds = float.PositiveInfinity }, Source with { AgeSeconds = Source.DurationSeconds },
            Source with { Intensity = 0 }, Source with { Intensity = -1 }, Source with { Intensity = float.NaN },
            Source with { Intensity = float.PositiveInfinity },
        ];
        var output = new RobotDischargeStroke[RobotDischargeGeometry.QuadLimit];
        Assert.Equal(0, RobotDischargeGeometry.Build(invalid, output));
        int count = RobotDischargeGeometry.Build([.. invalid, Source], output);
        Assert.True(count > 0);
        Assert.All(output.Take(count), CheckStroke);
    }

    static void CheckStroke(RobotDischargeStroke stroke)
    {
        Assert.True(float.IsFinite(stroke.Head.LengthSquared()));
        Assert.True(float.IsFinite(stroke.Tail.LengthSquared()));
        Assert.True(Vector3.DistanceSquared(stroke.Head, stroke.Tail) > 0);
        Assert.InRange(stroke.Width, .001f, 1);
        Assert.True(float.IsFinite(stroke.Colour.LengthSquared()));
        Assert.InRange(stroke.Colour.W, 0, 4);
        if (stroke.Arc) Assert.True(MathF.Abs(stroke.Colour.Z) > MathF.Abs(stroke.Colour.X));
        else Assert.True(stroke.Colour.X > stroke.Colour.Y && stroke.Colour.Y > stroke.Colour.Z);
    }
}
