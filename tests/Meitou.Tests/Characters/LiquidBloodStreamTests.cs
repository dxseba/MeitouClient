using System.Numerics;
using Meitou.Game;
using Meitou.Rendering.Characters;
using Meitou.Simulation;

namespace Meitou.Tests.Characters;

public class LiquidBloodStreamTests
{
    static readonly CharacterId Victim = new(8, 1);
    static CharacterCutContact? Contact(CharacterId _, int half) => new(new Vector3(0, 12, half == 0 ? 3 : -3), Vector3.UnitZ);

    [Fact]
    public void Connected_streams_start_at_the_moving_cut_and_curve_down_under_gravity()
    {
        var fluid = new LiquidBloodSimulation(new Random(10));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000);
        fluid.Advance(0, Contact, _ => 0);
        float x = 0;
        CharacterCutContact? Moving(CharacterId _, int half) => new(new Vector3(half == 0 ? x : x - 20, 12, half == 0 ? 3 : -3), Vector3.UnitZ);
        for (int i = 1; i <= 10; i++) { x = i * 0.03f; fluid.Advance(i / 30.0, Moving, _ => 0); }
        var chain = fluid.Streams.Where(s => s.Head.X > -5 && s.Tail.X > -5).ToArray();
        Assert.True(chain.Length >= 3);
        Assert.InRange(Vector3.Distance(chain[0].Head, new Vector3(x, 12, 3.06f)), 0, 1e-5f);
        for (int i = 1; i < chain.Length; i++) Assert.Equal(chain[i - 1].Tail, chain[i].Head);
        Assert.Contains(chain, s => s.Tail.Y < 11.8f);
    }

    [Fact]
    public void Pausing_keeps_the_entire_stream_and_expiry_removes_it()
    {
        var fluid = new LiquidBloodSimulation(new Random(11));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0xE4BC32);
        fluid.Advance(0, Contact, _ => 0);
        for (int i = 1; i <= 5; i++) fluid.Advance(i * 0.1, Contact, _ => 0);
        var held = fluid.Streams.ToArray();
        Assert.NotEmpty(held);
        for (int i = 0; i < 20; i++) fluid.Advance(0.5, Contact, _ => 0);
        Assert.Equal(held, fluid.Streams);
        for (int i = 6; i <= 35; i++) fluid.Advance(i * 0.1, Contact, _ => 0);
        Assert.Empty(fluid.Streams);
    }

    [Fact]
    public void Crowds_bound_stream_segments_and_release_free_drops_at_the_ends()
    {
        var fluid = new LiquidBloodSimulation(new Random(12));
        for (int i = 0; i < 80; i++) fluid.Begin(new CharacterId(i, 1), 0, Vector2.UnitX, 0x362118);
        CharacterCutContact? High(CharacterId _, int half) => new(new Vector3(0, 100, half == 0 ? 3 : -3), Vector3.UnitZ);
        fluid.Advance(0, High, _ => 0);
        int peak = 0;
        for (int i = 1; i <= 30; i++)
        {
            fluid.Advance(i / 30.0, High, _ => 0);
            peak = Math.Max(peak, fluid.Streams.Count);
            Assert.InRange(fluid.Streams.Count, 0, LiquidBloodSimulation.StreamSegmentLimit);
            Assert.InRange(fluid.Drops.Count, 0, LiquidBloodSimulation.DropLimit);
            Assert.All(fluid.Streams, segment => Assert.InRange(Vector3.Distance(segment.Head, segment.Tail), 0, 3.001f));
        }
        Assert.True(peak > 400);
        Assert.NotEmpty(fluid.Drops);
        var colour = new Vector3(0x36, 0x21, 0x18) / 255f;
        Assert.All(fluid.Streams, s => Assert.Equal(colour, new Vector3(s.Colour.X, s.Colour.Y, s.Colour.Z)));
    }

    [Fact]
    public void A_fast_moving_fragment_breaks_the_stream_instead_of_stretching_it_across_the_scene()
    {
        var fluid = new LiquidBloodSimulation(new Random(13));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000);
        fluid.Advance(0, Contact, _ => 0);
        fluid.Advance(0.1, Contact, _ => 0);
        CharacterCutContact? Moved(CharacterId _, int half) => new(new Vector3(50, 12, half == 0 ? 3 : -3), Vector3.UnitZ);
        fluid.Advance(0.2, Moved, _ => 0);
        fluid.Advance(0.3, Moved, _ => 0);
        Assert.NotEmpty(fluid.Streams);
        Assert.All(fluid.Streams, s => Assert.InRange(Vector3.Distance(s.Head, s.Tail), 0, 3.001f));
        Assert.Contains(fluid.Streams, s => MathF.Abs(s.Head.X - 50) < 0.01f);
        fluid.Clear();
        Assert.Empty(fluid.Streams);
    }
}
