using System.Collections.Concurrent;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Combat;

public class BrutalityCombatTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(59.999f, 0)] [InlineData(60, .10f)] [InlineData(70, .30f)]
    [InlineData(80, .55f)] [InlineData(90, .80f)] [InlineData(100, .95f)] [InlineData(200, .95f)]
    public void Bisection_probability_hits_each_user_anchor(float level, float expected) =>
        Assert.Equal(expected, BrutalityPolicy.BisectionChance(level));

    [Theory]
    [InlineData(60, .10f, .30f)] [InlineData(70, .30f, .55f)] [InlineData(80, .55f, .80f)] [InlineData(90, .80f, .95f)]
    public void Probability_interpolation_is_monotone_smooth_and_bounded(float level, float low, float high)
    {
        Assert.Equal((low + high) * .5f, BrutalityPolicy.BisectionChance(level + 5), 5);
        float previous = low;
        for (int step = 1; step <= 100; step++)
        {
            float chance = BrutalityPolicy.BisectionChance(level + step * .1f);
            Assert.InRange(chance, previous, high + .000001f);
            previous = chance;
        }

        Assert.True(BrutalityPolicy.BisectionChance(level + .01f) - low < (high - low) * .00001f);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1)]
    public void Invalid_levels_do_not_unlock_gore(float level)
    {
        Assert.Equal(0, BrutalityPolicy.HeadTier(level));
        Assert.Equal(0, BrutalityPolicy.BisectionChance(level));
        Assert.False(BrutalityPolicy.CanDecapitate(level, CombatWoundMechanism.Slash));
    }

    [Fact]
    public void Training_counts_delivered_channels_but_not_armour_stun_or_overkill()
    {
        Assert.Equal(45, BrutalityPolicy.UsefulDamage(new(20, 15, 10, 500), 100));
        Assert.Equal(12, BrutalityPolicy.UsefulDamage(new(2000, 500, 100, 500), 12));
        Assert.Equal(0, BrutalityPolicy.UsefulDamage(new(0, 0, 0, 500), 100));
        Assert.Equal(0, BrutalityPolicy.UsefulDamage(new(100, 0, 0), -1));
        Assert.Equal(0, BrutalityPolicy.UsefulDamage(new(float.NaN, 0, 0), 100));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Helpless_caged_carried_allied_and_noncontested_targets_cannot_train(int reason)
    {
        using var scene = new Scene();
        var state = scene.Combat.StateOf(scene.Target);
        Assert.True(scene.Combat.CanTrainBrutality(scene.World, scene.Attacker.Slot, scene.Target.Slot, state));
        switch (reason)
        {
            case 0:
                scene.Victim.Medical!.KnockOut(600);
                break;
            case 1:
                scene.Victim.InSomething = HeldState.Bed;
                break;
            case 2:
                scene.Victim.InSomething = HeldState.Imprisoned;
                break;
            case 3:
                scene.Victim.CarriedBy = scene.Attacker;
                break;
            case 4:
                scene.Victim.Faction = scene.Source.Faction;
                break;
            case 5:
                scene.Victim.GettingUpUntil = scene.World.Tick + 600;
                break;
            case 6:
                scene.Victim.InCombat = false;
                state.TargetSlot = state.AttackTech = state.ReactTech = -1;
                break;
        }

        Assert.False(scene.Combat.CanTrainBrutality(scene.World, scene.Attacker.Slot, scene.Target.Slot, state));
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, true)] [InlineData(3, true)] [InlineData(4, true)]
    public void Grounded_enemies_need_a_working_arm_but_standing_armless_kickers_can_train(int condition, bool expected)
    {
        using var scene = new Scene();
        scene.Victim.Race = Human;
        var medical = scene.Victim.Medical = MedicalState.Create(Human, 50);
        foreach (var part in medical.Parts)
        {
            if (part.Template.Type is not (BodyPartType.Arm or BodyPartType.Leg)) continue;
            if (condition == 4 && part.IsLeg) continue;
            part.Limb = LimbState.Stump;
            part.Flesh = part.MaxHp;
        }

        var left = medical.Parts.Single(p => p.Template == LeftArm);
        if (condition == 2) left.Limb = LimbState.Original;
        if (condition == 3)
        {
            left.Limb = LimbState.Replaced;
            left.Replacement = new LimbReplacement { StringId = "working-arm", Slot = 50, HpLow = 100, HpHigh = 100 };
        }
        if (condition is 2 or 3) left.Flesh = left.MaxHp;
        if (condition != 0) medical.Tick(1e-4f, Context());

        Assert.False(medical.Incapacitated);
        Assert.Equal(condition is 1 or 2 or 3, medical.Crawling);
        Assert.Equal(condition is 2 or 3, medical.ArmWorks(left: true));
        Assert.False(medical.ArmWorks(left: false));
        var state = scene.Combat.StateOf(scene.Target);
        Assert.Equal(expected, scene.Combat.CanTrainBrutality(scene.World, scene.Attacker.Slot, scene.Target.Slot, state));
    }

    [Fact]
    public void A_neutral_faction_enemy_must_really_retaliate_and_allies_still_do_not_train()
    {
        using var scene = new Scene();
        var relations = FactionRelations.Build([
            new() { Id = "unused", Name = "Unused" },
            new() { Id = "attacker", Name = "Attacker" },
            new() { Id = "defender", Name = "Defender" },
        ]);
        scene.Combat.Relations = relations;
        var state = scene.Combat.StateOf(scene.Target);
        Assert.Equal(scene.Attacker.Slot, state.TargetSlot);
        Assert.Equal(scene.Attacker.Generation, state.TargetGeneration);
        Assert.True(scene.Combat.CanTrainBrutality(scene.World, scene.Attacker.Slot, scene.Target.Slot, state));

        var selectedNeutral = state;
        selectedNeutral.TargetSlot = selectedNeutral.AttackTech = selectedNeutral.ReactTech = -1;
        Assert.False(scene.Combat.CanTrainBrutality(scene.World, scene.Attacker.Slot, scene.Target.Slot, selectedNeutral));
        selectedNeutral = state;
        selectedNeutral.TargetGeneration++;
        Assert.False(scene.Combat.CanTrainBrutality(scene.World, scene.Attacker.Slot, scene.Target.Slot, selectedNeutral));

        relations.Add(1, 2, 60);
        Assert.False(scene.Combat.CanTrainBrutality(scene.World, scene.Attacker.Slot, scene.Target.Slot, state));
        relations.Add(1, 2, -60);
        scene.Hit();
        Assert.True(scene.Source.Brutality.Experience > 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void One_real_landed_hit_awards_postarmour_damage_once_and_publishes_the_pre_hit_level(bool armour)
    {
        using var scene = new Scene(armour: armour);
        var part = scene.Victim.Medical!.Parts[0];
        float available = part.Flesh + part.MaxHp - part.Stun;
        var hit = scene.Hit();
        float expected = MathF.Min(hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce, available) * BaseGame.ExpGainMultiplier;

        Assert.InRange(MathF.Abs(scene.Source.Brutality.Experience - expected), 0, .001f);
        Assert.True(scene.Source.Brutality.Level > 0);
        Assert.Equal(0, hit.AttackerBrutality);
        Assert.Single(scene.Events, e => e.Kind == CombatEventKind.Hit);
    }

    [Theory]
    [InlineData(40, .6f)] [InlineData(25, 0)]
    public void Real_hits_train_less_against_weaker_enemies_and_stop_at_the_native_penalty_limit(float defence, float factor)
    {
        using var scene = new Scene();
        Assert.Equal(50, scene.Source.Stats![StatsEnumerated.MeleeAttack]);
        scene.Victim.Stats![StatsEnumerated.MeleeDefence] = defence;
        var hit = scene.Hit();
        float delivered = hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce;
        Assert.True(delivered > 0);
        float expected = delivered * factor * BaseGame.ExpGainMultiplier;
        Assert.InRange(MathF.Abs(scene.Source.Brutality.Experience - expected), 0, .001f);
    }

    [Fact]
    public void Overkill_is_capped_and_the_hit_crossing_sixty_does_not_use_its_new_unlock()
    {
        var chest = Chest with { StringId = ArmourData.ChestPartId };
        using var scene = new Scene(level: 59.99f, power: 100, targetPart: chest);
        float before = scene.Source.Brutality.Experience;
        float level = scene.Source.Brutality.Level;
        var hit = scene.Hit();

        Assert.True(hit.Damage.Cut > 200);
        Assert.InRange(MathF.Abs(scene.Source.Brutality.Experience - before - 200 * BaseGame.ExpGainMultiplier), 0, .005f);
        Assert.True(scene.Source.Brutality.Level > 60);
        Assert.Equal(level, hit.AttackerBrutality);
        Assert.True(scene.Victim.Medical!.Dead);
        Assert.Null(scene.Victim.TorsoBisection);
        Assert.DoesNotContain(scene.Events, e => e.Kind == CombatEventKind.BodyBisected);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_real_hit_on_a_helpless_enemy_or_newly_friendly_target_awards_no_xp(bool friendly)
    {
        using var scene = new Scene();
        if (friendly) scene.Victim.Faction = scene.Source.Faction;
        else scene.Victim.Medical!.KnockOut(600);

        scene.Hit();
        Assert.Equal(0, scene.Source.Brutality.Experience);
    }

    [Theory]
    [InlineData(39, 2, false)] [InlineData(40, 2, true)] [InlineData(100, 4, false)]
    public void A_deliberate_lethal_head_hit_needs_forty_and_slash(float level, int direction, bool decapitated)
    {
        using var scene = new Scene(level: level, direction: direction, targetPart: Head);
        scene.Victim.Medical!.Parts[0].Flesh = -99;
        scene.Victim.Medical.KnockOut(600);
        scene.Hit();

        Assert.True(scene.Victim.Medical.Dead);
        if (decapitated)
        {
            var cut = Assert.IsType<HeadCutState>(scene.Victim.HeadCut);
            Assert.True(cut.IsValid);
            Assert.Equal(cut.Mode != HeadCutMode.AttachedSplit, scene.Victim.Medical.Decapitated);
        }
        else
        {
            Assert.Null(scene.Victim.HeadCut);
            Assert.False(scene.Victim.Medical.Decapitated);
        }
        Assert.Equal(BrutalityProgress.FromLevel(level).Experience, scene.Source.Brutality.Experience);
    }

    [Theory]
    [InlineData(39, false)] [InlineData(40, true)]
    public void Execution_uses_the_same_head_unlock_but_never_trains(float level, bool decapitated)
    {
        using var scene = new Scene(level: level, targetPart: Head);
        scene.Victim.Medical!.Parts[0].Flesh = -99;
        scene.Victim.Medical.KnockOut(600);
        scene.Victim.InSomething = HeldState.Imprisoned;
        float before = scene.Source.Brutality.Experience;
        Assert.True(scene.Combat.Execute(scene.World, scene.Attacker.Slot, scene.Target.Slot, 0));
        Assert.Equal(decapitated, scene.Victim.Medical.Decapitated);
        Assert.Equal(before, scene.Source.Brutality.Experience);
    }

    [Fact]
    public void Ordinary_limb_amputation_is_unchanged_at_zero_brutality()
    {
        using var scene = new Scene(power: 100, targetPart: LeftArm);
        scene.Hit();
        Assert.Equal(LimbState.Stump, scene.Victim.Medical!.Parts[0].Limb);
        Assert.Contains(scene.Events, e => e.Kind == CombatEventKind.PartSevered);
    }

    sealed class Scene : IDisposable
    {
        public SimWorld World { get; }
        public CombatSystem Combat { get; }
        public CharacterId Attacker { get; }
        public CharacterId Target { get; }
        public CharacterCold Source => World.Characters.Cold(Attacker.Slot)!;
        public CharacterCold Victim => World.Characters.Cold(Target.Slot)!;
        public ConcurrentQueue<CombatEvent> Events { get; } = new();

        public Scene(float level = 0, float power = 1, int direction = 2, BodyPartTemplate? targetPart = null, bool armour = false)
        {
            targetPart ??= Chest;
            var options = BodyOptions.Default;
            options.ForcedHitPart = (targetPart.StringId, targetPart.Name);
            Combat = new([Cut("cut left", direction: direction)], Constants, BaseGame, options, DuelFixture.Lengths,
                new CombatOptions { RecordLog = true, TickMedical = false });
            World = new(new WorldSettings { Seed = 41, PublishSnapshots = false },
                new OpenGroundWalkability((_, _) => 150), [Combat, Senses(Combat)]);
            World.CombatEvents = Events;

            var source = new Combatant { Weapon = Katana with { CutDamageMultiplier = power } }.MakeCold();
            source.Faction = 1;
            source.Brutality = BrutalityProgress.FromLevel(level);
            var victim = new Combatant { Armour = armour ? [BodyPlate] : [] }.MakeCold();
            victim.Faction = 2;
            victim.Race = new() { StringId = Human.StringId, Name = Human.Name, Anatomy = [new(targetPart, 100, 100)] };
            victim.Medical = MedicalState.Create(victim.Race, 50);
            Attacker = World.Characters.Spawn(new() { Position = new(1000, 150, 1000), Yaw = MathF.PI / 2, Health = 100 }, source, 0);
            Target = World.Characters.Spawn(new() { Position = new(1014, 150, 1000), Yaw = -MathF.PI / 2, Health = 100 }, victim, 0);

            World.Commands.Enqueue(new AttackOrder([Attacker], Target) { Tick = 0, Picked = true, AllowIncapacitated = true });
            World.Commands.Enqueue(new AttackOrder([Target], Attacker) { Tick = 0 });
            for (int i = 0; i < 180 && Combat.StateOf(Attacker).AttackTech < 0; i++) World.RunTick();
            Assert.True(Combat.StateOf(Attacker).AttackTech >= 0);
            World.RunTick(); // Both combat buffers now contain the prepared swing, before its impact frame.
            Assert.Empty(Events);
        }

        public CombatEvent Hit()
        {
            var state = Combat.StateOf(Attacker);
            World.Characters.Phase = TablePhase.Commit;
            try
            {
                Combat.Apply(World, new Effect
                {
                    Kind = CombatSystem.BlowEffect, Source = Attacker.Slot, Target = Target.Slot, Arg = 1, Amount = state.AttackSeq,
                });
            }
            finally
            {
                World.Characters.Phase = TablePhase.Outside;
            }

            return Assert.Single(Events, e => e.Kind == CombatEventKind.Hit);
        }

        public void Dispose() => World.Dispose();
    }
}
