using System.Numerics;
using Meitou.Rendering;

namespace Meitou.Tests.Characters;

public class RobotBreakExplosionGeometryTests
{
    static RobotBreakExplosion Source => new(new(3, 100, 5), 0.1f, 123);

    [Fact]
    public void A_break_has_a_brief_flash_radial_blue_and_warm_sparks_then_local_soot()
    {
        var output = new RobotExplosionQuad[RobotBreakExplosionGeometry.QuadLimit];
        int count = RobotBreakExplosionGeometry.Build([Source], output);
        var burst = output.Take(count).ToArray();
        Assert.Contains(burst, q => q.Kind == RobotExplosionQuadKind.Flash);
        Assert.Contains(burst, q => q.Kind == RobotExplosionQuadKind.Smoke);
        var sparks = burst.Where(q => q.Kind == RobotExplosionQuadKind.Spark).ToArray();
        Assert.Equal(48, sparks.Length);
        Assert.Contains(sparks, q => q.Colour.X < 0);
        Assert.Contains(sparks, q => q.Colour.X > q.Colour.Y && q.Colour.Y > q.Colour.Z);
        foreach (int axis in Enumerable.Range(0, 3))
        {
            Assert.Contains(sparks, q => Component(q.Direction, axis) > 0.1f);
            Assert.Contains(sparks, q => Component(q.Direction, axis) < -0.1f);
        }
        Assert.All(burst, CheckQuad);

        count = RobotBreakExplosionGeometry.Build([Source with { Age = 0.3f }], output);
        Assert.DoesNotContain(output.Take(count), q => q.Kind == RobotExplosionQuadKind.Flash);
        count = RobotBreakExplosionGeometry.Build([Source with { Age = 1.1f }], output);
        Assert.InRange(count, 1, 4);
        Assert.All(output.Take(count), q => Assert.Equal(RobotExplosionQuadKind.Smoke, q.Kind));
        Assert.Equal(0, RobotBreakExplosionGeometry.Build([Source with { Age = RobotBreakExplosionGeometry.Lifetime }], output));
    }

    [Fact]
    public void Paused_age_is_deterministic_and_moving_the_contact_translates_the_whole_burst()
    {
        var output = new RobotExplosionQuad[RobotBreakExplosionGeometry.QuadLimit];
        int count = RobotBreakExplosionGeometry.Build([Source], output);
        var paused = output.Take(count).ToArray();
        for (int frame = 0; frame < 10; frame++)
        {
            Assert.Equal(count, RobotBreakExplosionGeometry.Build([Source], output));
            Assert.Equal(paused, output.Take(count).ToArray());
        }
        var offset = new Vector3(20, -2, -30);
        Assert.Equal(count, RobotBreakExplosionGeometry.Build([Source with { Position = Source.Position + offset }], output));
        for (int i = 0; i < count; i++)
        {
            Assert.InRange(Vector3.Distance(output[i].Position - paused[i].Position, offset), 0, 1e-4f);
            Assert.Equal(paused[i] with { Position = output[i].Position }, output[i]);
        }
    }

    [Fact]
    public void A_crowd_respects_independent_core_smoke_and_source_limits()
    {
        var sources = Enumerable.Range(0, 200).Select(i => Source with { Position = new(i * 100, 100, 0), Seed = (uint)i }).ToArray();
        var output = new RobotExplosionQuad[RobotBreakExplosionGeometry.QuadLimit + 100];
        int count = RobotBreakExplosionGeometry.Build(sources, output);
        var crowd = output.Take(count).ToArray();
        Assert.InRange(count, RobotBreakExplosionGeometry.SourceLimit, RobotBreakExplosionGeometry.QuadLimit);
        Assert.InRange(crowd.Count(q => q.Kind != RobotExplosionQuadKind.Smoke), 1, RobotBreakExplosionGeometry.CoreQuadLimit);
        Assert.InRange(crowd.Count(q => q.Kind == RobotExplosionQuadKind.Smoke), 1, RobotBreakExplosionGeometry.SmokeLimit);
        Assert.All(crowd, q =>
        {
            CheckQuad(q);
            int owner = (int)MathF.Round(q.Position.X / 100);
            Assert.InRange(owner, 0, RobotBreakExplosionGeometry.SourceLimit - 1);
            Assert.InRange(Vector3.Distance(q.Position, sources[owner].Position), 0, 5);
        });
        Assert.Equal(count, RobotBreakExplosionGeometry.Build(sources.AsSpan(0, RobotBreakExplosionGeometry.SourceLimit), output));
        Assert.Equal(crowd, output.Take(count).ToArray());
    }

    [Fact]
    public void Tiny_output_spans_and_invalid_sources_cannot_overrun_or_produce_nonfinite_quads()
    {
        var sentinel = new RobotExplosionQuad(new(-1), new(-2), -1, -2, -3, new(-4), RobotExplosionQuadKind.Flash);
        foreach (int capacity in new[] { 0, 1, 2, 3, 7, 31, 320, 321, 383, 384 })
        {
            var output = Enumerable.Repeat(sentinel, capacity + 2).ToArray();
            int count = RobotBreakExplosionGeometry.Build([Source], output.AsSpan(1, capacity));
            Assert.InRange(count, 0, capacity);
            Assert.All(output.Skip(1).Take(count), CheckQuad);
            Assert.Equal(sentinel, output[0]);
            Assert.Equal(sentinel, output[^1]);
        }
        RobotBreakExplosion[] invalid =
        [
            Source with { Position = new(float.NaN, 0, 0) }, Source with { Position = new(float.PositiveInfinity) },
            Source with { Position = new(float.MaxValue) }, Source with { Age = -1 }, Source with { Age = float.NaN },
            Source with { Age = float.PositiveInfinity }, Source with { Age = 2 }, Source with { Age = 20 },
            Source with { Scale = 0 }, Source with { Scale = -1 }, Source with { Scale = float.NaN },
            Source with { Scale = float.PositiveInfinity },
        ];
        Assert.Equal(0, RobotBreakExplosionGeometry.Build(invalid, new RobotExplosionQuad[RobotBreakExplosionGeometry.QuadLimit]));
    }

    [Fact]
    public void A_single_burst_never_repeats_and_large_input_scales_are_clamped()
    {
        var output = new RobotExplosionQuad[RobotBreakExplosionGeometry.QuadLimit];
        foreach (float age in new[] { 0f, 0.25f, 0.7f, 1.1f, 1.9f, 1.999f })
        {
            int count = RobotBreakExplosionGeometry.Build([Source with { Age = age, Scale = float.MaxValue }], output);
            Assert.InRange(count, 1, RobotBreakExplosionGeometry.QuadLimit);
            Assert.All(output.Take(count), q =>
            {
                CheckQuad(q);
                Assert.InRange(Vector3.Distance(q.Position, Source.Position), 0, 40);
                Assert.InRange(q.Width, 0.001f, 20);
            });
            if (age > 1.05f) Assert.All(output.Take(count), q => Assert.Equal(RobotExplosionQuadKind.Smoke, q.Kind));
        }
    }

    static float Component(Vector3 direction, int axis) => axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
    static void CheckQuad(RobotExplosionQuad quad)
    {
        Assert.True(float.IsFinite(quad.Position.LengthSquared()));
        Assert.True(float.IsFinite(quad.Direction.LengthSquared()));
        Assert.True(float.IsFinite(quad.Colour.LengthSquared()));
        Assert.True(float.IsFinite(quad.Rotation));
        Assert.InRange(quad.Width, float.Epsilon, 20);
        Assert.InRange(quad.Height, float.Epsilon, 20);
        Assert.InRange(quad.Colour.W, float.Epsilon, 1);
    }
}
