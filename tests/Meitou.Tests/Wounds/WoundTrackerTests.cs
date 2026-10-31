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
    public void Each_torso_hit_opens_its_own_wound_and_ten_heavy_on_the_belly_are_deep()
    {
        var t = new WoundTracker();
        for (int i = 0; i < 10; i++) Assert.True(t.Apply(Hit(i * 40), WoundRegion.Abdomen, 100));
        var body = t.Of(Victim)!;
        Assert.True(body.DeepAbdomen);
        Assert.Contains(body.Limbs.ToArray(), w => w is { Segment: 9 });
    }

    [Fact]
    public void Limb_wounds_stay_on_that_limb_and_the_slots_are_bounded()
    {
        var t = new WoundTracker();
        for (int i = 0; i < 200; i++) Assert.True(t.Apply(Hit(i, limb: 3), WoundRegion.Unknown, 100));
        var limbs = t.Of(Victim)!.Limbs.ToArray();
        Assert.Equal(WoundBody.LimbSlots, limbs.Length);
        Assert.All(limbs.Where(w => w is not null), w => Assert.InRange(w!.Value.Segment, 6, 7));
    }

    [Fact]
    public void Wounds_survive_their_side_file()
    {
        var t = new WoundTracker();
        t.Apply(Hit(), WoundRegion.Chest, 100);
        t.Apply(Hit(limb: 1), WoundRegion.Unknown, 100);
        var lines = t.Write(_ => (7, 9)).ToArray();
        var back = new WoundTracker();
        back.Read(lines, key => key == (7, 9) ? Victim : null);
        Assert.Equal(t.Of(Victim)!.Limbs[1], back.Of(Victim)!.Limbs[1]);
        Assert.Equal(t.Of(Victim)!.Limbs[0], back.Of(Victim)!.Limbs[0]);
    }

    [Fact]
    public void Wounds_grow_shallower_and_close()
    {
        var t = new WoundTracker();
        t.Apply(Hit(), WoundRegion.Chest, 100);
        float dose = t.Of(Victim)!.Limbs[0]!.Value.Dose;
        t.Heal(dose / 2);
        Assert.Equal(dose / 2, t.Of(Victim)!.Limbs[0]!.Value.Dose, 4);
        t.Heal(dose);
        Assert.Null(t.Of(Victim));
    }

    [Fact]
    public void Only_melee_hits_open_wounds()
    {
        var t = new WoundTracker();
        Assert.False(t.Apply(Hit(kind: CombatEventKind.PartSevered), WoundRegion.Chest, 100));
        Assert.False(t.Apply(Hit(cut: -1), WoundRegion.Chest, 100));
        Assert.Null(t.Of(Victim));
        Assert.True(t.Apply(Hit(), WoundRegion.Unknown, 100));   // neither torso nor limb: the head
    }
}
