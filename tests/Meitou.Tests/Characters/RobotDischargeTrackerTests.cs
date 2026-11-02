using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Characters;

public class RobotDischargeTrackerTests
{
    static readonly CharacterId Owner = new(4, 2);
    static CombatEvent Hit => new(CombatEventKind.Hit, 120, CombatHitSource.Melee, new(1, 0), Owner,
        2, -1, Vector3.Zero, new(3, 4), new(70, 0, 0), CutDirection: 2, Mechanism: CombatWoundMechanism.Slash);

    [Fact]
    public void A_robot_short_circuit_lasts_twenty_to_thirty_game_seconds_and_expires_at_its_deadline()
    {
        var lifetimes = new List<double>();
        for (long tick = 0; tick < 64; tick++)
        {
            var tracker = new RobotDischargeTracker();
            Assert.True(tracker.Observe(Hit with { Tick = tick }, true, 100, new(12, 5, -3), worldSeed: 7));
            var discharge = Assert.Single(tracker.Active);
            lifetimes.Add(discharge.Expires - discharge.Born);
            Assert.InRange(discharge.Expires, 120, 130);
            tracker.Prune(119.999, _ => true);
            Assert.Equal(1, tracker.Count);
            tracker.Prune(discharge.Expires - .001, _ => true);
            Assert.Equal(1, tracker.Count);
            tracker.Prune(discharge.Expires, _ => true);
            Assert.Empty(tracker.Active);
        }
        Assert.True(lifetimes.Max() - lifetimes.Min() > 5, "Different strikes all produced the same short-circuit duration.");
    }

    [Fact]
    public void Paused_and_invalid_clocks_do_not_advance_or_erase_an_active_discharge()
    {
        var tracker = new RobotDischargeTracker();
        Assert.True(tracker.Observe(Hit, true, 5, Vector3.One));
        var before = Assert.Single(tracker.Active);
        for (int frame = 0; frame < 1000; frame++) tracker.Prune(5, _ => true);
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            tracker.Prune(invalid, _ => false);
        Assert.Equal(before, Assert.Single(tracker.Active));
        tracker.Prune(before.Expires - .001, _ => true);
        Assert.Equal(before, Assert.Single(tracker.Active));
        tracker.Prune(before.Expires + .001, _ => true);
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void A_destroyed_robot_keeps_discharging_until_its_identity_is_removed_and_cannot_attach_to_a_reused_slot()
    {
        var race = new RaceData { StringId = "electrical-robot", Name = "Skeleton", IsRobot = true, Anatomy = [new(Head, 100, 200)] };
        var medical = MedicalState.Create(race, 50);
        var table = new CharacterTable();
        var owner = table.Spawn(new() { Health = 100 }, new()
        {
            Race = race, Medical = medical, HeadCut = new(new(1, 0, 0, 0), HeadCutMode.DetachedSplit),
            TorsoBisection = new(new(0, 1, 0, -12)),
        }, 0);
        var tracker = new RobotDischargeTracker();
        Assert.True(tracker.Observe(Hit with { Victim = owner }, true, 0, Vector3.Zero));
        medical.Butcher();
        Assert.True(medical.Dead);
        Assert.True(table.IsAlive(owner));
        tracker.Prune(10, table.IsAlive);
        Assert.Equal(owner, Assert.Single(tracker.Active).Victim);

        Assert.True(table.Remove(owner));
        var replacement = table.Spawn(new() { Health = 100 }, new() { Race = race, Medical = MedicalState.Create(race, 50) }, 10);
        Assert.Equal(owner.Slot, replacement.Slot);
        Assert.NotEqual(owner.Generation, replacement.Generation);
        tracker.Prune(10, table.IsAlive);
        Assert.Empty(tracker.Active);
        Assert.True(tracker.Observe(Hit with { Victim = replacement }, true, 10, Vector3.One));
        Assert.Equal(replacement, Assert.Single(tracker.Active).Victim);
    }

    [Fact]
    public void A_replayed_contact_cannot_extend_the_effect_but_a_new_hit_refreshes_the_same_part()
    {
        var tracker = new RobotDischargeTracker();
        Assert.True(tracker.Observe(Hit, true, 10, Vector3.Zero));
        var first = Assert.Single(tracker.Active);
        Assert.False(tracker.Observe(Hit, true, first.Expires - .1, new(100, 100, 100)));
        Assert.Equal(first, Assert.Single(tracker.Active));
        var renewed = Hit with { Tick = Hit.Tick + 1, LowStrike = true, Direction = new(float.NaN, 0), Damage = new(7000, 0, 0) };
        double now = first.Expires - .1;
        Assert.True(tracker.Observe(renewed, true, now, new(6, 4, 2)));
        var after = Assert.Single(tracker.Active);
        Assert.Equal(first.Victim, after.Victim);
        Assert.Equal(first.Part, after.Part);
        Assert.True(after.LowStrike);
        Assert.Equal(new Vector3(6, 4, 2), after.FallbackPosition);
        Assert.Equal(Vector2.UnitX, after.Travel);
        Assert.Equal(1, after.Force);
        Assert.InRange(after.Expires - now, 20, 30);
        Assert.True(after.Expires > first.Expires);
        Assert.True(after.Serial > first.Serial);
        Assert.NotEqual(first.Seed, after.Seed);
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void Repeated_part_hits_and_global_overflow_keep_a_bounded_set_and_retain_recently_refreshed_parts()
    {
        var tracker = new RobotDischargeTracker();
        for (int strike = 0; strike < 200; strike++)
            Assert.True(tracker.Observe(Hit with { Tick = strike, Part = strike % 7 }, true, strike * .01, Vector3.Zero));
        Assert.Equal(7, tracker.Count);
        Assert.Equal(7, tracker.Active.Select(d => (d.Victim, d.Part)).Distinct().Count());

        tracker.Clear();
        for (int index = 0; index < RobotDischargeTracker.Capacity; index++)
            Assert.True(tracker.Observe(Hit with { Victim = new(index + 10, 1) }, true, 0, Vector3.Zero));
        var refreshed = Hit with { Victim = new(10, 1), Tick = 121 };
        Assert.True(tracker.Observe(refreshed, true, 1, Vector3.Zero));
        Assert.True(tracker.Observe(Hit with { Victim = new(1000, 1) }, true, 1, Vector3.Zero));
        Assert.Equal(RobotDischargeTracker.Capacity, tracker.Count);
        Assert.Contains(tracker.Active, d => d.Victim == refreshed.Victim);
        Assert.DoesNotContain(tracker.Active, d => d.Victim == new CharacterId(11, 1));
        Assert.Contains(tracker.Active, d => d.Victim == new CharacterId(1000, 1));
    }

    [Fact]
    public void Organic_noncut_secondary_and_invalid_contacts_cannot_schedule_a_persistent_short_circuit()
    {
        var tracker = new RobotDischargeTracker();
        Assert.False(tracker.Observe(Hit, false, 0, Vector3.Zero));
        CombatEvent[] ignored =
        [
            Hit with { Kind = CombatEventKind.BodyBisected }, Hit with { Kind = CombatEventKind.PartSevered },
            Hit with { Kind = CombatEventKind.Died }, Hit with { Source = CombatHitSource.Bolt },
            Hit with { Mechanism = CombatWoundMechanism.Crush }, Hit with { Mechanism = CombatWoundMechanism.Thrust },
            Hit with { Damage = new(0, 200, 0) }, Hit with { Damage = new(float.NaN, 0, 0) },
            Hit with { Victim = CharacterId.None }, Hit with { Part = -1 },
        ];
        foreach (var contact in ignored) Assert.False(tracker.Observe(contact, true, 0, Vector3.Zero));
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.False(tracker.Observe(Hit, true, invalid, Vector3.Zero));
        foreach (var invalid in new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0) })
            Assert.False(tracker.Observe(Hit, true, 0, invalid));
        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void Clearing_a_world_removes_all_timing_and_replay_state_without_contaminating_a_new_world()
    {
        var tracker = new RobotDischargeTracker();
        Assert.True(tracker.Observe(Hit, true, 10000, Vector3.One, worldSeed: 11));
        tracker.Clear();
        Assert.Equal(0, tracker.Count);
        Assert.Empty(tracker.Active);
        Assert.True(tracker.Observe(Hit, true, 0, new(3, 2, 1), worldSeed: 12));
        var discharge = Assert.Single(tracker.Active);
        Assert.Equal(0, discharge.Born);
        Assert.Equal(new Vector3(3, 2, 1), discharge.FallbackPosition);
        Assert.InRange(discharge.Expires, 20, 30);
    }
}
