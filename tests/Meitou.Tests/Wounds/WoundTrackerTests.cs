using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Wounds;

public class WoundTrackerTests
{
    static readonly CharacterId Victim = new(3, 1);

    // The victim faces +Z at yaw 0, so a blow travelling -Z lands on its front.
    static CombatEvent Hit(long tick = 1, int limb = -1, float dirZ = -1, CombatEventKind kind = CombatEventKind.Hit, int cut = 1) =>
        new(kind, tick, CombatHitSource.Melee, CharacterId.None, Victim, 2, limb, default, new Vector2(0, dirZ), new HitDamage(40, 0, 0), 0, cut);

    [Fact]
    public void Torso_hits_deepen_one_wound_and_a_blow_from_behind_lands_on_the_back()
    {
        var t = new WoundTracker();
        for (int i = 0; i < 3; i++) Assert.True(t.Apply(Hit(i), WoundRegion.Abdomen, 100));
        Assert.True(t.Apply(Hit(9, dirZ: 1), WoundRegion.Abdomen, 100));
        var body = t.Of(Victim)!;
        Assert.Equal(3, body.Torso[(int)WoundRegion.Abdomen]!.Value.Hits);
        Assert.True(body.DeepAbdomen);
        Assert.Equal(1, body.Torso[(int)WoundRegion.Back]!.Value.Hits);
    }

    [Fact]
    public void Limb_wounds_stay_on_that_limb_and_the_slots_are_bounded()
    {
        var t = new WoundTracker();
        for (int i = 0; i < 200; i++) Assert.True(t.Apply(Hit(i, limb: 3), WoundRegion.Unknown, 100));
        var limbs = t.Of(Victim)!.Limbs.ToArray();
        Assert.Equal(WoundBody.LimbSlots, limbs.Length);
        Assert.All(limbs, w => Assert.InRange(w!.Value.Segment, 6, 7));
    }

    [Fact]
    public void Only_melee_hits_open_wounds()
    {
        var t = new WoundTracker();
        Assert.False(t.Apply(Hit(kind: CombatEventKind.PartSevered), WoundRegion.Chest, 100));
        Assert.False(t.Apply(Hit(cut: -1), WoundRegion.Chest, 100));
        Assert.False(t.Apply(Hit(), WoundRegion.Unknown, 100));
        Assert.Null(t.Of(Victim));
    }
}
