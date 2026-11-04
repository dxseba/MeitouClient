using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Combat;

public partial class BrutalityCombatTests
{
    static Scene PairedScene(BodyPartTemplate part, float level = 90)
    {
        using var probe = new Scene(level: level, power: 100, targetPart: part, paired: true);
        var state = probe.Combat.StateOf(probe.Attacker);
        ulong counter = CombatRolls.BlowCounter(probe.Attacker.Slot, state.AttackSeq, 1, 0);
        ulong seed = 0;
        while (CombatRolls.Float(seed, Rng.Key(probe.Target), CombatRoll.PairedLimb, counter) >= .01f) seed++;
        var scene = new Scene(level: level, power: 100, targetPart: part, paired: true, seed: seed);
        Assert.Equal(counter, CombatRolls.BlowCounter(scene.Attacker.Slot, scene.Combat.StateOf(scene.Attacker).AttackSeq, 1, 0));
        return scene;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_real_paired_hit_publishes_two_losses_but_one_hit_and_one_attacker_brutality_award(bool legs)
    {
        using var scene = PairedScene(legs ? LeftLeg : LeftArm);
        float before = scene.Source.Brutality.Experience;
        var hit = scene.Hit();
        var losses = scene.Events.Where(e => e.Kind == CombatEventKind.PartSevered).ToArray();
        Assert.Equal(2, losses.Length);
        Assert.Equal(2, losses.Select(e => e.LimbSlot).Distinct().Count());
        Assert.Equal(legs ? new[] { 2, 3 } : new[] { 0, 1 }, losses.Select(e => e.LimbSlot).Order());
        Assert.Single(scene.Events, e => e.Kind == CombatEventKind.Hit);
        Assert.All(scene.Victim.Medical!.Parts, p => Assert.Equal(LimbState.Stump, p.Limb));
        Assert.Equal(90, hit.AttackerBrutality, 3);
        Assert.InRange(scene.Source.Brutality.Experience - before, 149.99f, 150.01f);
        Assert.Equal(2, scene.World.Characters.LimbsChanged.Count);
    }

    [Fact]
    public void Living_unconscious_targets_can_lose_both_limbs_but_give_no_skill_experience()
    {
        using var scene = PairedScene(LeftArm);
        scene.Victim.Medical!.KnockOut(600);
        float before = scene.Source.Brutality.Experience;
        scene.Hit();
        Assert.Equal(2, scene.Events.Count(e => e.Kind == CombatEventKind.PartSevered));
        Assert.Equal(before, scene.Source.Brutality.Experience);
    }

    [Fact]
    public void A_prisoner_execution_does_not_gain_the_melee_paired_sweep_even_with_a_qualifying_seed()
    {
        using var scene = PairedScene(LeftArm);
        scene.Victim.Medical!.KnockOut(600);
        scene.Victim.InSomething = global::Meitou.Simulation.Locks.HeldState.Imprisoned;
        float before = scene.Source.Brutality.Experience;
        scene.Combat.Execute(scene.World, scene.Attacker.Slot, scene.Target.Slot, 0);
        Assert.Single(scene.Events, e => e.Kind == CombatEventKind.PartSevered);
        Assert.Equal(before, scene.Source.Brutality.Experience);
    }

    [Fact]
    public void The_hit_that_crosses_eighty_does_not_gain_the_paired_unlock_for_that_swing()
    {
        using var scene = PairedScene(LeftArm, 79.99f);
        var hit = scene.Hit();
        Assert.True(scene.Source.Brutality.Level > 80);
        Assert.Equal(79.99f, hit.AttackerBrutality, 3);
        Assert.Single(scene.Events, e => e.Kind == CombatEventKind.PartSevered);
    }
}
