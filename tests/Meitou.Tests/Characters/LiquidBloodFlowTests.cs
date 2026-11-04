using System.Numerics;
using Meitou.Game;
using Meitou.Rendering.Characters;
using Meitou.Simulation;

namespace Meitou.Tests.Characters;

public class LiquidBloodFlowTests
{
    static readonly Vector3 RaceColour = new(0x4A / 255f, 0x17 / 255f, 0x33 / 255f);

    [Fact]
    public void A_moving_wound_keeps_a_short_jointed_flow_with_pulsing_tapered_widths_at_different_frame_rates()
    {
        foreach (double dt in new[] { 0.016, 0.1, 0.4 })
        {
            var fluid = new LiquidBloodSimulation(new Random(41));
            fluid.Begin(new CharacterId(1, 1), 0, Vector2.UnitX, 0x4A1733);
            double now = 0;
            CharacterCutContact? Contact(CharacterId _, int half) => half == 0
                ? new(new Vector3((float)now * 50, 100, 0), Vector3.UnitZ) : null;
            fluid.Advance(0, Contact, _ => 0);
            var headWidths = new List<float>();
            int connectedFrames = 0;
            for (now = dt; now < 1.8; now += dt)
            {
                fluid.Advance(now, Contact, _ => 0);
                float length = 0;
                for (int segment = 0; segment < fluid.Streams.Count; segment++)
                {
                    var stream = fluid.Streams[segment];
                    if (segment > 0 && fluid.Streams[segment - 1].Tail != stream.Head)
                    {
                        Assert.InRange(length, 0, 4.8001f);
                        length = 0; // Detached filaments have their own bounded connected reach.
                    }
                    length += Vector3.Distance(stream.Head, stream.Tail);
                    Assert.True(float.IsFinite(stream.Width) && float.IsFinite(stream.TailWidth));
                    Assert.InRange(stream.Width, 0.0001f, 0.34f);
                    Assert.InRange(stream.TailWidth, 0.0001f, 0.34f);
                    Assert.Equal(RaceColour, new Vector3(stream.Colour.X, stream.Colour.Y, stream.Colour.Z));
                    if (segment == 0) continue;
                    if (fluid.Streams[segment - 1].Tail == stream.Head)
                        Assert.Equal(fluid.Streams[segment - 1].TailWidth, stream.Width);
                }
                Assert.InRange(length, 0, 4.8001f);
                if (fluid.Streams.Count == 0) continue;
                connectedFrames++;
                headWidths.Add(fluid.Streams[0].Width);
            }
            Assert.True(connectedFrames >= 3);
            // Pressure decays overall but a living flow also swells between pulses instead of thinning monotonically.
            if (dt < 0.02)
                Assert.Contains(headWidths.Zip(headWidths.Skip(1), (a, b) => b > a * 1.01f), increasing => increasing);
        }
    }

    [Fact]
    public void A_steadily_translated_cut_carries_its_liquid_instead_of_leaving_a_trailing_tether()
    {
        var fluid = new LiquidBloodSimulation(new Random(43));
        fluid.Begin(new CharacterId(1, 1), 0, Vector2.UnitY, 0x4A1733);
        double now = 0;
        // Outflow and its sideways wobble lie in Y/Z, isolating the inherited X velocity of the wound.
        CharacterCutContact? Contact(CharacterId _, int half) => half == 0
            ? new(new Vector3((float)now * 10, 100, 0), Vector3.UnitY) : null;
        fluid.Advance(0, Contact, _ => 0);
        for (int frame = 1; frame <= 36; frame++)
        {
            now = frame / 60.0;
            fluid.Advance(now, Contact, _ => 0);
            if (frame < 25) continue; // All retained liquid was born after the cut's motion was known.
            Assert.NotEmpty(fluid.Streams);
            float cutX = (float)now * 10;
            Assert.All(fluid.Streams, stream =>
            {
                Assert.InRange(MathF.Abs(stream.Head.X - cutX), 0, 0.3f);
                Assert.InRange(MathF.Abs(stream.Tail.X - cutX), 0, 0.3f);
            });
        }
    }

    [Fact]
    public void A_severely_moved_cut_releases_its_old_tail_as_free_blobs_without_emitting_while_paused()
    {
        var fluid = new LiquidBloodSimulation(new Random(42));
        for (int i = 0; i < LiquidBloodSimulation.SourceLimit; i++)
            fluid.Begin(new CharacterId(i, 1), 0, Vector2.UnitX, 0x4A1733);
        float shift = 0;
        CharacterCutContact? Contact(CharacterId owner, int half) => half == 0
            ? new(new Vector3(owner.Slot * 16 + shift, 100, 0), Vector3.UnitZ) : null;
        fluid.Advance(0, Contact, _ => 0);
        for (int i = 1; i <= 4; i++) fluid.Advance(i * 0.04, Contact, _ => 0);
        shift = 50;
        fluid.Advance(0.18, Contact, _ => 0);
        // Newly born regular spray is beside the new cut; age-zero blobs at the old cut are the broken liquid tail.
        Assert.Contains(fluid.Drops, drop => drop.Age == 0 && drop.At.X < 5);
        var heldDrops = fluid.Drops.ToArray();
        var heldStreams = fluid.Streams.ToArray();
        for (int i = 0; i < 30; i++)
            fluid.Advance(0.18, (_, _) => throw new InvalidOperationException("Paused flow queried a cut"),
                _ => throw new InvalidOperationException("Paused flow queried the floor"));
        Assert.Equal(heldDrops, fluid.Drops);
        Assert.Equal(heldStreams, fluid.Streams);

        int peak = fluid.Drops.Count;
        for (int i = 1; i <= 20; i++)
        {
            shift = i % 2 == 0 ? 50 : 0;
            fluid.Advance(0.18 + i * 0.04, Contact, _ => 0);
            peak = Math.Max(peak, fluid.Drops.Count);
            Assert.InRange(fluid.Drops.Count, 0, LiquidBloodSimulation.DropLimit);
            Assert.InRange(fluid.Streams.Count, 0, LiquidBloodSimulation.StreamSegmentLimit);
            Assert.All(fluid.Drops, drop => Assert.Equal(RaceColour, drop.Colour));
        }
        Assert.True(peak > 400);
    }
}
