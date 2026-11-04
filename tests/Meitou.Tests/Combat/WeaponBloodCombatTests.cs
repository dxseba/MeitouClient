using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using static Meitou.Tests.Combat.DuelFixture;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Combat;

public class WeaponBloodCombatTests
{
    [Fact]
    public void A_landed_weapon_hit_stains_the_used_item_not_an_identical_weapon_in_the_other_slot()
    {
        using var fight = new Delivery();
        Assert.Same(fight.Used, fight.World.Characters.Cold(fight.Attacker.Slot)!.Fighter!.Weapon!.SourceItem);
        Assert.Equal(fight.Used.Record, fight.Other.Record);
        Assert.Equal(BlowOutcome.Hit, fight.FirstOutcome().Outcome);
        Assert.True(fight.Target.Medical!.Parts[0].Flesh < 100);
        Assert.True(fight.Used.WeaponBlood.W > 0);
        Assert.Equal(Vector4.Zero, fight.Other.WeaponBlood);
    }

    [Fact]
    public void Armour_reduces_weapon_blood_with_the_delivered_wound_instead_of_the_raw_weapon_packet()
    {
        using var bare = new Delivery();
        using var armoured = new Delivery(armour: true);
        Assert.Equal(BlowOutcome.Hit, bare.FirstOutcome().Outcome);
        Assert.Equal(BlowOutcome.Hit, armoured.FirstOutcome().Outcome);
        var exposedHit = Assert.Single(bare.Events, e => e.Kind == CombatEventKind.Hit);
        var protectedHit = Assert.Single(armoured.Events, e => e.Kind == CombatEventKind.Hit);
        Assert.True(protectedHit.Damage.Cut < exposedHit.Damage.Cut);
        Assert.True(armoured.Used.WeaponBlood.W < bare.Used.WeaponBlood.W);
        Assert.Equal(Vector4.Zero, armoured.Other.WeaponBlood);
    }

    [Fact]
    public void A_multi_victim_strike_accumulates_on_one_actual_weapon_item()
    {
        using var single = new Delivery();
        using var multiple = new Delivery(multiple: true);
        single.FirstOutcome();
        multiple.FirstOutcome();
        Assert.Equal(2, multiple.Events.Count(e => e.Kind == CombatEventKind.Hit));
        Assert.True(single.Used.WeaponBlood.W is > 0 and < 1);
        Assert.True(multiple.Used.WeaponBlood.W > single.Used.WeaponBlood.W);
        Assert.InRange(multiple.Used.WeaponBlood.W, 0, 1);
        Assert.Equal(Vector4.Zero, multiple.Other.WeaponBlood);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_fist_or_kick_while_holding_a_weapon_does_not_stain_that_weapon(int limb)
    {
        using var fight = new Delivery(limb: limb);
        Assert.Equal(BlowOutcome.Hit, fight.FirstOutcome().Outcome);
        Assert.True(fight.Target.Medical!.Parts[0].Flesh < 100);
        Assert.Equal(Vector4.Zero, fight.Used.WeaponBlood);
        Assert.Equal(Vector4.Zero, fight.Other.WeaponBlood);
    }

    [Fact]
    public void An_unarmed_packet_does_not_stain_even_a_linked_fists_item()
    {
        using var fight = new Delivery(limb: 0, unarmed: true);
        Assert.Equal(BlowOutcome.Hit, fight.FirstOutcome().Outcome);
        Assert.True(fight.Target.Medical!.Parts[0].Flesh < 100);
        Assert.Equal(Vector4.Zero, fight.Used.WeaponBlood);
    }

    [Theory]
    [InlineData(Defence.Block, BlowOutcome.Blocked)]
    [InlineData(Defence.Dodge, BlowOutcome.Dodged)]
    public void A_successful_block_or_dodge_leaves_the_attacking_weapon_clean(Defence defence, BlowOutcome expected)
    {
        bool observed = false;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            using var fight = new Delivery(seed: seed, defence: defence);
            if (fight.FirstOutcome().Outcome != expected) continue;
            observed = true;
            Assert.DoesNotContain(fight.Events, e => e.Kind == CombatEventKind.Hit);
            Assert.All(fight.Target.Medical!.Parts, p => Assert.Equal(p.MaxHp, p.Flesh));
            Assert.Equal(Vector4.Zero, fight.Used.WeaponBlood);
            Assert.Equal(Vector4.Zero, fight.Other.WeaponBlood);
            break;
        }
        Assert.True(observed, $"The fixture did not produce a native {expected} outcome.");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Real_hits_on_robots_and_bloodless_races_do_not_stain(bool robot, bool bloodless)
    {
        using var fight = new Delivery(robot: robot, bloodless: bloodless);
        Assert.Equal(BlowOutcome.Hit, fight.FirstOutcome().Outcome);
        Assert.True(fight.Target.Medical!.Parts[0].Flesh < 100);
        Assert.Equal(Vector4.Zero, fight.Used.WeaponBlood);
    }

    [Fact]
    public void A_severed_organic_arm_stains_but_a_destroyed_prosthetic_does_not()
    {
        using var flesh = new Delivery(part: RightArm, strong: true);
        using var prosthetic = new Delivery(part: RightArm, strong: true, prosthetic: true);
        Assert.Equal(BlowOutcome.Hit, flesh.FirstOutcome().Outcome);
        Assert.Equal(BlowOutcome.Hit, prosthetic.FirstOutcome().Outcome);
        Assert.Equal(LimbState.Stump, flesh.Target.Medical!.Parts[0].Limb);
        Assert.Single(flesh.Events, e => e.Kind == CombatEventKind.PartSevered);
        Assert.True(flesh.Used.WeaponBlood.W > 0);
        var metal = prosthetic.Target.Medical!.Parts[0];
        Assert.True(metal.IsDestroyed);
        Assert.Equal(LimbState.Replaced, metal.Limb); // Native rules never sever a replacement.
        Assert.DoesNotContain(prosthetic.Events, e => e.Kind == CombatEventKind.PartSevered);
        Assert.Equal(Vector4.Zero, prosthetic.Used.WeaponBlood);
    }

    [Fact]
    public void A_real_execution_packet_stains_its_actual_weapon_item()
    {
        using var fight = new Delivery();
        fight.World.RunTick(); // Initialize the combat state without waiting for the scheduled attack.
        fight.Combat.Execute(fight.World, fight.Attacker.Slot, fight.Defender.Slot, 1);
        Assert.Single(fight.Events, e => e.Kind == CombatEventKind.Hit && e.Source == CombatHitSource.Execution);
        Assert.True(fight.Used.WeaponBlood.W > 0);
        Assert.Equal(Vector4.Zero, fight.Other.WeaponBlood);
    }

    [Fact]
    public void Accumulation_blends_victim_colours_and_saturates_without_exceeding_valid_colour_ranges()
    {
        var item = Weapon("back");
        var red = new RaceData { StringId = "red", BloodColour = 0xFF0000 };
        var blue = new RaceData { StringId = "blue", BloodColour = 0x0000FF };
        Assert.True(CombatSystem.DepositWeaponBlood(item, red, new(20, 0, 0), true, true));
        var first = item.WeaponBlood;
        Assert.True(first.X > 0 && first.Z == 0 && first.W > 0);
        Assert.True(CombatSystem.DepositWeaponBlood(item, blue, new(20, 0, 0), true, true));
        Assert.True(item.WeaponBlood.X > 0 && item.WeaponBlood.Z > 0 && item.WeaponBlood.W > first.W);
        CombatSystem.DepositWeaponBlood(item, red, new(10000, 0, 0), true, true);
        Assert.Equal(1, item.WeaponBlood.W);
        for (int component = 0; component < 4; component++) Assert.InRange(item.WeaponBlood[component], 0, 1);
    }

    [Fact]
    public void Crush_wounds_blood_the_weapon_but_incidental_blunt_stun_and_invalid_hits_do_not()
    {
        var crushed = Weapon("back");
        var untouched = Weapon("back");
        Assert.True(CombatSystem.DepositWeaponBlood(crushed, Human, new(0, 80, 0), true, true, CombatWoundMechanism.Crush));
        Assert.InRange(crushed.WeaponBlood.W, 0.27f, 0.29f);
        Assert.False(CombatSystem.DepositWeaponBlood(untouched, Human, new(0, 80, 0), true, true, CombatWoundMechanism.Slash));
        Assert.False(CombatSystem.DepositWeaponBlood(untouched, Human, new(20, 0, 0), false, true));
        Assert.False(CombatSystem.DepositWeaponBlood(untouched, Human, new(20, 0, 0), true, false));
        Assert.False(CombatSystem.DepositWeaponBlood(untouched, Human, new(float.NaN, 0, 0), true, true));
        Assert.Equal(Vector4.Zero, untouched.WeaponBlood);
    }

    public enum Defence { None, Block, Dodge }

    static ItemInstance Weapon(string section, string? record = null) => new()
    {
        Record = record ?? Katana.StringId, Name = "same weapon", Type = FcsRecordType.WEAPON, Section = section, Level = 50,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_an_enabled_gore_world_stains_the_real_weapon_after_a_wound(bool enabled)
    {
        using var fight = new Delivery(gore: enabled);
        Assert.Equal(BlowOutcome.Hit, fight.FirstOutcome().Outcome);
        Assert.True(fight.Target.Medical!.Parts[0].Flesh < 100);
        Assert.Equal(enabled, fight.Used.WeaponBlood.W > 0);
        Assert.Equal(Vector4.Zero, fight.Other.WeaponBlood);
    }

    sealed class Delivery : IDisposable
    {
        public SimWorld World { get; }
        public CombatSystem Combat { get; }
        public CharacterId Attacker { get; }
        public CharacterId Defender { get; }
        public CharacterCold Target { get; }
        public ItemInstance Used { get; }
        public ItemInstance Other { get; }
        public ConcurrentQueue<CombatEvent> Events { get; } = new();

        public Delivery(ulong seed = 11, int limb = 4, bool unarmed = false, bool armour = false, bool multiple = false,
            bool robot = false, bool bloodless = false, BodyPartTemplate? part = null, bool strong = false, bool prosthetic = false,
            Defence defence = Defence.None, bool gore = true)
        {
            var weapon = unarmed ? Katana with { StringId = "fists-test", SkillCategory = 5, AnimationCategory = 5 } : Katana;
            if (strong) weapon = weapon with { CutDamageMultiplier = 25 };
            var cut = Cut("cut left") with
            {
                Limb1 = limb, Limb2 = limb, Animal = unarmed ? 5 : 0,
                Kinds = unarmed ? WeaponKinds.Unarmed : WeaponKinds.Katana,
                MaxSimultaneousHits = multiple ? 2 : 1,
            };
            var techniques = new List<CombatTechnique> { cut };
            if (defence == Defence.Block) techniques.Add(Block("block left", 2));
            if (defence == Defence.Dodge) techniques.Add(Dodge("dodge back"));
            World = NewWorld(seed, 1, out var combat, techniques: techniques, gore: gore);
            Combat = combat;
            World.CombatEvents = Events;
            (Attacker, Defender) = Pair(World,
                new Combatant { Weapon = weapon, Attack = defence == Defence.None ? 50 : 1 },
                new Combatant { Weapon = defence == Defence.Block ? Katana : null, Armour = armour ? [BodyPlate] : [],
                    Defence = 100, Dodge = defence == Defence.Dodge ? 100 : 0 });
            Used = Weapon("back", weapon.StringId);
            Other = Weapon("hip", weapon.StringId);
            var attacker = World.Characters.Cold(Attacker.Slot)!;
            attacker.Inventory = new Inventory();
            attacker.Inventory.Items.AddRange([Other, Used]);
            attacker.Fighter = new Fighter(WeaponInstance.Create(0.5f, weapon, null, null, Constants, Other))
            {
                BackWeapon = WeaponInstance.Create(0.5f, weapon, null, null, Constants, Used),
            };
            Target = World.Characters.Cold(Defender.Slot)!;
            var race = new RaceData
            {
                StringId = "weapon-blood-target", Name = "target", IsRobot = robot, BleedRate = bloodless ? 0 : 1,
                Anatomy = defence == Defence.None ? [new(part ?? Chest, 100, 100)] : Human.Anatomy,
            };
            Target.Race = race;
            Target.Medical = MedicalState.Create(race, 50);
            Target.Orders |= CharacterOrders.Block;
            if (prosthetic)
            {
                var arm = Target.Medical.Parts[0];
                arm.Limb = LimbState.Replaced;
                arm.Replacement = new LimbReplacement { StringId = "test-arm", Slot = 51, HpLow = 100, HpHigh = 100 };
                arm.Flesh = arm.MaxHp;
            }
            if (multiple)
            {
                var extra = Spawn(World, new Combatant { Weapon = null }, new(1014, 150, 1002));
                var cold = World.Characters.Cold(extra.Slot)!;
                cold.Faction = 1;
                cold.Race = race;
                cold.Medical = MedicalState.Create(race, 50);
            }
        }

        public CombatLogEntry FirstOutcome()
        {
            for (int tick = 0; tick < 300; tick++)
            {
                World.RunTick();
                foreach (var entry in Combat.Log)
                    if (entry.Attacker == Attacker.Slot) return entry;
            }
            throw new InvalidOperationException("The fixture did not deliver an attack.");
        }

        public void Dispose() => World.Dispose();
    }
}
