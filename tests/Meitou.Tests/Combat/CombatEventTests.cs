using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.DuelFixture;

namespace Meitou.Tests.Combat;

/// <summary><see cref="World.CombatEvents"/>: what a hit publishes, in which order, and that reading it changes nothing.</summary>
public class CombatEventTests
{
    [Fact]
    public void A_duels_hits_match_the_log_and_each_consequence_follows_its_hit()
    {
        using var world = NewWorld(11, 1, out var combat);
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (a, b) = Pair(world, new Combatant { Name = "A" }, new Combatant { Name = "B" });
        RunToEnd(world, a, b);

        var events = queue.ToArray();
        Assert.Equal(combat.Log.Count(e => e.Outcome == BlowOutcome.Hit), events.Count(e => e.Kind == CombatEventKind.Hit));
        Assert.Equal(CombatEventKind.Hit, events[0].Kind);
        CombatEvent hit = events[0];
        foreach (var e in events)
        {
            if (e.Kind == CombatEventKind.Hit) { hit = e; Assert.True(e.Part >= 0); Assert.True(e.CutDirection >= 0); Assert.True(e.Damage.Cut + e.Damage.Blunt + e.Damage.Pierce > 0); continue; }
            Assert.Equal((hit.Tick, hit.Attacker, hit.Victim), (e.Tick, e.Attacker, e.Victim));
        }
    }

    [Fact]
    public void A_severed_part_keeps_its_own_part_and_limb_slot_after_the_hit()
    {
        using var world = NewWorld(11, 1, out var combat);
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (a, b) = Pair(world, new Combatant { Name = "A" }, new Combatant { Name = "B" });
        var medical = Healthy();
        var events = new List<MedicalEvent>();
        medical.ApplyHit(RArm, new HitDamage(250, 0, 0), Context(), events);
        combat.PublishHit(world, CombatHitSource.Melee, a, b.Slot, medical, HeadIx, new HitDamage(250, 0, 0), Vector2.UnitX, events);

        var published = queue.ToArray();
        Assert.Equal(CombatEventKind.Hit, published[0].Kind);
        Assert.Equal(HeadIx, published[0].Part);
        var severed = Assert.Single(published, e => e.Kind == CombatEventKind.PartSevered);
        Assert.Equal(RArm, severed.Part);
        Assert.Equal(medical.Parts[RArm].Template.LimbSlot, severed.LimbSlot);
        Assert.Equal(b, severed.Victim);
    }
}
