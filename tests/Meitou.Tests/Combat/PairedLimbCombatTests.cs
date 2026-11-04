using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;

namespace Meitou.Tests.Combat;

public class PairedLimbCombatTests
{
    readonly struct Covered : ICoverRoll { public float Roll(int piece) => 0; }

    sealed class Blow
    {
        public BodyOptions Options { get; } = BodyOptions.Default;
        public CharacterCold Victim { get; }
        public MedicalContext Context { get; set; }
        public CombatSystem Combat { get; }
        public List<MedicalEvent> Events { get; } = [];
        public int Part { get; }
        public HitDamage Primary { get; } = new(250, 0, 0);
        public DamagePacket Packet { get; set; } = new(250, 0, 0, 0, 1, 0);
        public HealthPart Other => Victim.Medical!.LimbPart(Victim.Medical.Parts[Part].Template.LimbSlot ^ 1)!;
        public Blow(bool legs = false, bool robot = false)
        {
            Victim = new Combatant().MakeCold();
            Victim.Race = new() { Anatomy = Human.Anatomy, IsRobot = robot };
            Victim.Medical = Healthy(Victim.Race);
            Context = new(BaseGame with { ImmediateBloodLoss = 0 }, Options, Victim.Race) { Toughness = 50, Strength = 50, Seed = 42, CharacterKey = 7 };
            Combat = new([Cut("cut left")], Constants, BaseGame, Options, DuelFixture.Lengths);
            Part = legs ? LArm + 2 : LArm;
            Victim.Medical.Tick(.001f, Context); // Match the live body update that latches posture before the swing.
            Victim.Medical.ApplyHit(Part, Primary, Context, Events);
            Assert.Single(Events, e => e.Kind == MedicalEventKind.PartSevered && e.Part == Part);
        }
        public bool Follow(float level = 100, int direction = 2, CombatWoundMechanism mechanism = CombatWoundMechanism.Slash,
            float roll = 0, bool alive = true) => Combat.TrySeverCounterpart(Victim, Part, Packet, Primary, Context, new Covered(),
                50, alive, level, direction, mechanism, roll, Events);
    }

    [Theory]
    [InlineData(79.99f, 0)] [InlineData(80, .01f)] [InlineData(90, .03f)] [InlineData(100, .05f)] [InlineData(200, .05f)]
    public void Follow_through_stays_rare_at_each_unlock_anchor(float level, float chance) => Assert.Equal(chance, PairedLimbPolicy.Chance(level), 6);

    [Theory]
    [InlineData(float.NaN, 100)] [InlineData(float.PositiveInfinity, 100)] [InlineData(99.99f, 100)]
    [InlineData(250, float.NaN)] [InlineData(250, float.PositiveInfinity)] [InlineData(250, 0)]
    public void Invalid_or_weak_cut_channels_do_not_unlock_a_second_limb(float cut, float maximum) =>
        Assert.False(PairedLimbPolicy.ShouldFollowThrough(100, CombatWoundMechanism.Slash, 2, cut, maximum, 0));

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void One_sweep_can_sever_both_native_arms_or_legs_and_repeated_calls_do_nothing(bool legs, bool robot)
    {
        var blow = new Blow(legs, robot);
        Assert.True(blow.Follow());
        Assert.Equal(LimbState.Stump, blow.Other.Limb);
        Assert.True(blow.Other.Flesh < 0);
        Assert.Equal(2, blow.Events.Count(e => e.Kind == MedicalEventKind.PartSevered));
        Assert.Equal(2, blow.Events.Count(e => e.Kind == MedicalEventKind.PartDown));
        if (legs)
        {
            Assert.True(blow.Victim.Medical!.Unconscious);
            Assert.Single(blow.Events, e => e.Kind == MedicalEventKind.KnockedOut);
        }
        var before = (blow.Victim.Medical!.Blood, blow.Other.Flesh, blow.Events.Count);
        Assert.False(blow.Follow());
        Assert.Equal(before, (blow.Victim.Medical.Blood, blow.Other.Flesh, blow.Events.Count));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void Ineligible_follow_through_leaves_the_other_limb_untouched(int reason)
    {
        var blow = new Blow();
        switch (reason)
        {
            case 0: blow.Options.AdvancedRagdolls = false; break;
            case 1: blow.Options.Dismemberment = Dismemberment.Never; break;
            case 2: blow.Other.Limb = LimbState.Replaced; break;
            case 3: blow.Other.Limb = LimbState.Stump; break;
            case 4: blow.Events.Clear(); break;
            case 5: blow.Victim.Fighter = new Fighter(KatanaAt(.5f)) { IsAnimal = true }; break;
            case 6: blow.Context = blow.Context with { HasStats = false }; break;
            case 7: blow.Context = blow.Context with { ExemptFromWear = true }; break;
            case 8: blow.Victim.Medical!.Butcher(); break;
            case 9: blow.Packet = blow.Packet with { Cut = 100, Blunt = 1000, Stun = 1000 }; break;
        }
        var before = (blow.Other.Limb, blow.Other.Flesh, blow.Victim.Medical!.Blood, blow.Events.Count);
        Assert.False(blow.Follow(level: reason == 10 ? 79.99f : 100, alive: reason != 11));
        Assert.Equal(before, (blow.Other.Limb, blow.Other.Flesh, blow.Victim.Medical.Blood, blow.Events.Count));
    }

    [Theory]
    [InlineData(1, CombatWoundMechanism.Slash, 0)] [InlineData(4, CombatWoundMechanism.Thrust, 0)]
    [InlineData(2, CombatWoundMechanism.Crush, 0)] [InlineData(2, CombatWoundMechanism.Slash, .05f)]
    [InlineData(2, CombatWoundMechanism.Slash, -1)] [InlineData(2, CombatWoundMechanism.Slash, float.NaN)]
    public void Only_a_successful_slashing_sweep_roll_can_reach_the_other_side(int direction, CombatWoundMechanism mechanism, float roll)
    {
        var blow = new Blow();
        Assert.False(blow.Follow(direction: direction, mechanism: mechanism, roll: roll));
        Assert.Equal(LimbState.Original, blow.Other.Limb);
    }

    [Fact]
    public void The_opposite_limbs_own_armour_can_stop_the_follow_through_without_a_phantom_wound()
    {
        var blow = new Blow();
        var plate = BodyPlate with { Coverage = [new(blow.Other.Template.StringId, 100)] };
        blow.Victim.Fighter = new(KatanaAt(.5f), [new ArmourPiece(plate, 1, 1, 1, 1, 0)]);
        float before = blow.Other.Flesh;
        Assert.False(blow.Follow());
        Assert.Equal(before, blow.Other.Flesh);
        Assert.Equal(LimbState.Original, blow.Other.Limb);
        Assert.Single(blow.Events, e => e.Kind == MedicalEventKind.PartSevered);
    }

    [Fact]
    public void Both_stumps_round_trip_through_the_existing_native_limb_bits()
    {
        var blow = new Blow(legs: true);
        Assert.True(blow.Follow());
        var loaded = Healthy(blow.Victim.Race);
        loaded.ApplySavedLimbs(blow.Victim.Medical!.LimbBits, _ => null);
        Assert.Equal(LimbState.Stump, loaded.LimbPart(2)!.Limb);
        Assert.Equal(LimbState.Stump, loaded.LimbPart(3)!.Limb);
        Assert.Equal(LimbState.Original, loaded.LimbPart(0)!.Limb);
    }
}
