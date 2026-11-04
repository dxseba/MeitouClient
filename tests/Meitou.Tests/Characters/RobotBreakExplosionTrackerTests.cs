using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Characters;

public class RobotBreakExplosionTrackerTests
{
    static readonly CharacterId Owner = new(4, 2);
    static CombatEvent Hit => new(CombatEventKind.Hit, 120, CombatHitSource.Melee, new(1, 0), Owner,
        2, -1, Vector3.Zero, Vector2.UnitX, new(100, 0, 0), CutDirection: 2, Mechanism: CombatWoundMechanism.Slash);
    static CharacterCold Victim(bool robot = true) => new()
    {
        Race = new() { StringId = "robot-burst", Name = "Robot", IsRobot = robot, Anatomy = Human.Anatomy }, Medical = MedicalState.Create(Human, 50),
    };

    [Fact]
    public void Real_torso_limb_and_head_breaks_each_emit_once_across_repeated_notifications()
    {
        var victim = Victim();
        var tracker = new RobotBreakExplosionTracker();
        var torso = Hit with { Kind = CombatEventKind.BodyBisected };
        Assert.True(tracker.Observe(torso, victim, 10, Vector3.One, 7));
        Assert.False(tracker.Observe(torso with { Tick = 121 }, victim, 11, Vector3.One, 7));
        var limb = Hit with { Part = LArm, Kind = CombatEventKind.PartSevered };
        Assert.True(tracker.Observe(limb, victim, 10, Vector3.One, 7));
        victim.HeadCut = new(new(0, 1, 0, 0), HeadCutMode.DetachedSplit);
        var head = Hit with { Part = HeadIx };
        Assert.True(tracker.Observe(head, victim, 10, Vector3.One, 7));
        Assert.False(tracker.Observe(head with { Kind = CombatEventKind.PartSevered }, victim, 10, Vector3.One, 7));
        Assert.Equal(new[] { 2, 0, 1 }, tracker.Active.Select(b => b.Kind));
        tracker.Prune(13, _ => true);
        Assert.Empty(tracker.Active);
        Assert.False(tracker.Observe(torso, victim, 13, Vector3.One, 7));
    }

    [Fact]
    public void Ordinary_hits_medical_death_and_organic_breaks_do_not_explode()
    {
        var victim = Victim();
        var tracker = new RobotBreakExplosionTracker();
        foreach (var hit in new[] { Hit, Hit with { Kind = CombatEventKind.Died }, Hit with { Part = HeadIx },
            Hit with { Part = LArm }, Hit with { Kind = CombatEventKind.PartSevered } })
            Assert.False(tracker.Observe(hit, victim, 0, Vector3.Zero, 0));
        Assert.False(tracker.Observe(Hit with { Kind = CombatEventKind.BodyBisected }, Victim(false), 0, Vector3.Zero, 0));
        Assert.Equal(0, tracker.HistoryCount);
    }

    [Fact]
    public void Explosion_captures_the_current_contact_once_and_pause_holds_its_game_age()
    {
        var tracker = new RobotBreakExplosionTracker();
        Assert.True(tracker.Observe(Hit with { Kind = CombatEventKind.BodyBisected }, Victim(), 5, Vector3.One, 7));
        var burst = Assert.Single(tracker.Active);
        Assert.Null(burst.Position);
        burst.Capture(new(float.NaN, 0, 0));
        Assert.Null(burst.Position);
        burst.Capture(new(12, 18, -3));
        burst.Capture(new(90, 18, 0));
        for (int frame = 0; frame < 1000; frame++) tracker.Prune(5, _ => true);
        Assert.Equal(new Vector3(12, 18, -3), burst.Position);
        Assert.Equal(5, burst.Born);
        Assert.Same(burst, Assert.Single(tracker.Active));
        tracker.Prune(6.999, _ => true);
        Assert.Single(tracker.Active);
        tracker.Prune(7, _ => true);
        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void Crowds_bound_active_geometry_sources_and_replay_history_without_replaying_evicted_effects()
    {
        var tracker = new RobotBreakExplosionTracker();
        var victim = Victim();
        var first = Hit with { Kind = CombatEventKind.BodyBisected };
        for (int i = 0; i < 100; i++)
            Assert.True(tracker.Observe(first with { Victim = new(i + 4, 2) }, victim, 0, Vector3.Zero, 1));
        Assert.Equal(RobotBreakExplosionTracker.Capacity, tracker.Count);
        Assert.DoesNotContain(tracker.Active, b => b.Victim == Owner);
        Assert.False(tracker.Observe(first, victim, 1, Vector3.Zero, 1));
        for (int i = 100; i < 1000; i++)
            tracker.Observe(first with { Victim = new(i + 4, 2) }, victim, 0, Vector3.Zero, 1);
        Assert.Equal(RobotBreakExplosionTracker.Capacity, tracker.Count);
        Assert.Equal(RobotBreakExplosionTracker.HistoryLimit, tracker.HistoryCount);
    }

    [Fact]
    public void Medical_death_keeps_the_burst_but_removed_identities_and_world_changes_clear_it()
    {
        var table = new CharacterTable();
        var victim = Victim();
        var owner = table.Spawn(new() { Health = 100 }, victim, 0);
        var hit = Hit with { Victim = owner, Kind = CombatEventKind.BodyBisected };
        var tracker = new RobotBreakExplosionTracker();
        Assert.True(tracker.Observe(hit, victim, 1, Vector3.Zero, 1));
        victim.Medical!.Butcher();
        tracker.Prune(1, table.IsAlive);
        Assert.Single(tracker.Active);
        Assert.True(table.Remove(owner));
        var replacement = table.Spawn(new() { Health = 100 }, Victim(), 1);
        Assert.Equal(owner.Slot, replacement.Slot);
        Assert.NotEqual(owner.Generation, replacement.Generation);
        tracker.Prune(1, table.IsAlive);
        Assert.Empty(tracker.Active);
        Assert.True(tracker.Observe(hit with { Victim = replacement }, Victim(), 1, Vector3.Zero, 1));
        tracker.Clear();
        Assert.Empty(tracker.Active);
        Assert.Equal(0, tracker.HistoryCount);
        Assert.True(tracker.Observe(hit, victim, 1, Vector3.Zero, 1));
    }

    [Fact]
    public void Invalid_inputs_cannot_poison_the_tracker_and_visual_seeds_are_repeatable()
    {
        var hit = Hit with { Kind = CombatEventKind.BodyBisected };
        var victim = Victim();
        var tracker = new RobotBreakExplosionTracker();
        Assert.False(tracker.Observe(hit with { Victim = CharacterId.None }, victim, 0, Vector3.Zero, 0));
        Assert.False(tracker.Observe(hit with { Part = -1 }, victim, 0, Vector3.Zero, 0));
        Assert.False(tracker.Observe(hit, victim, double.NaN, Vector3.Zero, 0));
        Assert.False(tracker.Observe(hit, victim, 0, new(float.PositiveInfinity, 0, 0), 0));
        Assert.Equal(0, tracker.HistoryCount);
        Assert.True(tracker.Observe(hit, victim, 1, Vector3.Zero, 10));
        var other = new RobotBreakExplosionTracker();
        Assert.True(other.Observe(hit, victim, 1, Vector3.One, 10));
        Assert.Equal(Assert.Single(tracker.Active).Seed, Assert.Single(other.Active).Seed);
        tracker.Prune(double.NaN, _ => false);
        Assert.Single(tracker.Active);
    }
}
