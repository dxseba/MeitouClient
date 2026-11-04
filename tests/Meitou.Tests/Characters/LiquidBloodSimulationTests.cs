using System.Numerics;

using Meitou.Game;
using Meitou.Rendering.Characters;
using Meitou.Simulation;

namespace Meitou.Tests.Characters;

public sealed class LiquidBloodSimulationTests
{
    static readonly CharacterId Victim = new(4, 1);
    static CharacterCutContact? Contact(CharacterId _, int half) => new(new Vector3(0, 12, half == 0 ? 2 : -2), half == 0 ? Vector3.UnitZ : -Vector3.UnitZ);
    static float Ground(Vector3 _) => 0;

    [Fact]
    public void A_cut_emits_from_both_halves_once_and_does_not_replay_after_expiry()
    {
        var fluid = new LiquidBloodSimulation(new Random(1));
        Assert.True(fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000));
        Assert.False(fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000));
        fluid.Advance(0, Contact, Ground);
        fluid.Advance(0.1, Contact, Ground);
        Assert.Contains(fluid.Drops, p => p.At.Z > 0);
        Assert.Contains(fluid.Drops, p => p.At.Z < 0);
        for (int i = 2; i <= 60; i++) fluid.Advance(i * 0.1, Contact, Ground);
        Assert.Empty(fluid.Drops);
        Assert.Equal(0, fluid.SourceCount);
        Assert.False(fluid.Begin(Victim, 6, Vector2.UnitX, 0x800000));
        Assert.True(fluid.Begin(new CharacterId(4, 2), 6, Vector2.UnitX, 0x800000));
    }

    [Fact]
    public void Pause_keeps_positions_velocities_and_ages_and_emits_no_contact_marks()
    {
        var fluid = new LiquidBloodSimulation(new Random(2));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000);
        fluid.Advance(0, Contact, Ground);
        fluid.Advance(0.1, Contact, Ground);
        var paused = fluid.Drops.ToArray();
        for (int i = 0; i < 30; i++) fluid.Advance(0.1, Contact, Ground);
        Assert.Equal(paused, fluid.Drops);
        Assert.Empty(fluid.Impacts);
    }

    [Theory]
    [InlineData(0x800000)]
    [InlineData(0xE4BC32)]
    [InlineData(0x362118)]
    public void Human_hive_and_robot_fluids_keep_the_supplied_race_colour(int rgb)
    {
        var fluid = new LiquidBloodSimulation(new Random(3));
        fluid.Begin(Victim, 0, Vector2.UnitX, (uint)rgb);
        fluid.Advance(0, Contact, Ground);
        fluid.Advance(0.1, Contact, Ground);
        var colour = new Vector3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255f;
        Assert.NotEmpty(fluid.Drops);
        Assert.All(fluid.Drops, p => Assert.Equal(colour, p.Colour));
    }

    [Fact]
    public void Many_simultaneous_cuts_bound_particles_sources_and_landing_decals_then_drain()
    {
        var fluid = new LiquidBloodSimulation(new Random(4));
        for (int i = 0; i < 100; i++) fluid.Begin(new CharacterId(i, 1), 0, Vector2.UnitX, 0x800000);
        fluid.Advance(0, Contact, Ground);
        int totalImpacts = 0, peak = 0;
        for (int i = 1; i <= 70; i++)
        {
            fluid.Advance(i * 0.1, Contact, Ground);
            peak = Math.Max(peak, fluid.Drops.Count);
            totalImpacts += fluid.Impacts.Count;
            Assert.InRange(fluid.SourceCount, 0, LiquidBloodSimulation.SourceLimit);
            Assert.InRange(fluid.Drops.Count, 0, LiquidBloodSimulation.DropLimit);
            Assert.InRange(fluid.Impacts.Count, 0, LiquidBloodSimulation.ImpactLimit);
        }
        Assert.True(peak > 500);
        Assert.InRange(totalImpacts, 1, 24 + 16 * 7);
        Assert.Empty(fluid.Drops);
        Assert.Equal(0, fluid.SourceCount);
    }

    [Fact]
    public void Drops_follow_the_blow_fall_and_splash_on_the_supplied_upper_floor()
    {
        var fluid = new LiquidBloodSimulation(new Random(5));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000);
        CharacterCutContact? AboveFloor(CharacterId _, int half) => new(new Vector3(0, 30, 0), Vector3.UnitZ);
        fluid.Advance(0, AboveFloor, _ => 25);
        fluid.Advance(0.1, AboveFloor, _ => 25);
        Assert.True(fluid.Drops.Average(p => p.Velocity.X) > 1);
        bool landed = false;
        for (int i = 2; i <= 50; i++)
        {
            fluid.Advance(i * 0.1, AboveFloor, _ => 25);
            foreach (var impact in fluid.Impacts)
            {
                landed = true;
                Assert.Equal(25, impact.At.Y);
                Assert.True(impact.Velocity.Y < 0);
            }
        }
        Assert.True(landed);
    }

    [Fact]
    public void Late_drips_have_less_pressure_and_reset_removes_all_transient_state()
    {
        var fluid = new LiquidBloodSimulation(new Random(6));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000);
        fluid.Advance(0, Contact, Ground);
        fluid.Advance(0.1, Contact, Ground);
        fluid.Advance(0.2, Contact, Ground);
        float first = fluid.Streams[0].Width;
        for (int i = 3; i <= 16; i++) fluid.Advance(i * 0.1, Contact, Ground);
        Assert.NotEmpty(fluid.Streams);
        Assert.True(fluid.Streams[0].Width < first * 0.75f);
        fluid.Clear();
        Assert.Empty(fluid.Drops);
        Assert.Empty(fluid.Impacts);
        Assert.Equal(0, fluid.SourceCount);
        Assert.True(fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000));
    }
    [Fact]
    public void A_half_that_gets_its_render_pose_late_still_receives_its_initial_jet()
    {
        var fluid = new LiquidBloodSimulation(new Random(7));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000);
        fluid.Advance(0, Contact, Ground);
        fluid.Advance(0.1, (id, half) => half == 0 ? Contact(id, half) : null, Ground);
        Assert.DoesNotContain(fluid.Drops, p => p.At.Z < 0);
        fluid.Advance(0.2, Contact, Ground);
        Assert.Contains(fluid.Drops, p => p.At.Z < 0 && p.Age < 0.11f);
        fluid.Advance(0.3, Contact, Ground);
        Assert.Contains(fluid.Streams, p => p.Head.Z < 0);
    }

    [Fact]
    public void A_slow_frame_does_not_advance_fresh_drops_away_from_the_current_cut_surface()
    {
        var fluid = new LiquidBloodSimulation(new Random(8));
        fluid.Advance(0, Contact, Ground);
        fluid.Begin(Victim, 0.45, Vector2.UnitX, 0x800000);
        fluid.Advance(0.5, Contact, Ground);
        Assert.NotEmpty(fluid.Drops);
        Assert.All(fluid.Drops, drop =>
        {
            Assert.Equal(0, drop.Age);
            Assert.True(MathF.Abs(drop.At.X) < 0.2f);
            Assert.InRange(drop.At.Y, 11.8f, 12.4f);
        });
    }

    [Fact]
    public void Each_half_emits_out_of_its_own_cut_face_including_the_downward_facing_half()
    {
        var fluid = new LiquidBloodSimulation(new Random(9));
        fluid.Begin(Victim, 0, Vector2.UnitX, 0x800000);
        CharacterCutContact? Faces(CharacterId _, int half) => new(new Vector3(0, 12, half == 0 ? 3 : -3),
            half == 0 ? -Vector3.UnitY : Vector3.UnitY);
        fluid.Advance(0, Faces, Ground);
        fluid.Advance(0.1, Faces, Ground);
        Assert.All(fluid.Drops.Where(p => p.At.Z > 0), p => Assert.True(p.Velocity.Y < -3));
        Assert.All(fluid.Drops.Where(p => p.At.Z < 0), p => Assert.True(p.Velocity.Y > 3));
    }

}