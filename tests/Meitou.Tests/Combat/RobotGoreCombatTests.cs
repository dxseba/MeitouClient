using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Combat;

public class RobotGoreCombatTests
{
    static readonly BodyPartTemplate RobotChest = Chest with { StringId = ArmourData.ChestPartId };
    static readonly BodyPartTemplate RobotStomach = Stomach with { StringId = BodyPartTemplate.StomachPartId };

    [Fact]
    public void Robot_torso_cuts_keep_the_delivered_damage_skill_source_and_living_target_gates()
    {
        foreach (var part in new[] { RobotChest, RobotStomach })
        {
            var eligible = new TorsoBisectionInput(CombatHitSource.Melee, CombatWoundMechanism.Slash, part, 2, 150, 200,
                Robot: true) { Brutality = 60, Lethal = true };
            Assert.Equal(.10f, TorsoBisectionPolicy.Chance(eligible));
            Assert.Equal(.95f, TorsoBisectionPolicy.Chance(eligible with { Brutality = 100 }));
            TorsoBisectionInput[] excluded =
            [
                eligible with { Cut = 149.99f }, eligible with { Maximum = 400 }, eligible with { Brutality = 59.99f },
                eligible with { Lethal = false }, eligible with { AliveBeforeHit = false }, eligible with { AlreadyBisected = true },
                eligible with { Animal = true }, eligible with { Source = CombatHitSource.Bolt },
                eligible with { Source = CombatHitSource.Execution }, eligible with { Mechanism = CombatWoundMechanism.Crush },
                eligible with { Mechanism = CombatWoundMechanism.Thrust }, eligible with { CutDirection = 4 },
            ];
            foreach (var input in excluded) Assert.False(TorsoBisectionPolicy.ShouldBisect(input, 0));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Robot_wear_and_native_death_apply_once_before_a_chest_or_stomach_cut(bool stomach)
    {
        var part = stomach ? RobotStomach : RobotChest;
        var survivor = Victim(part);
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(250, 0, 0);
        survivor.Medical!.ApplyHit(0, damage, Context(survivor.Race), events);
        Assert.False(survivor.Medical.Dead);
        var struck = survivor.Medical.Parts[0];
        Assert.Equal(10, struck.Wear);
        var before = (struck.Flesh, struck.Wear, survivor.Medical.Blood);
        Assert.False(CombatSystem.TryBisect(survivor, 0, damage, CombatWoundMechanism.Slash, true, 0, 2, events, brutality: 100));
        Assert.Equal(before, (struck.Flesh, struck.Wear, survivor.Medical.Blood));
        Assert.Null(survivor.TorsoBisection);

        var victim = Victim(part);
        events.Clear();
        damage = new HitDamage(700, 0, 0);
        victim.Medical!.ApplyHit(0, damage, Context(victim.Race), events);
        Assert.True(victim.Medical.Dead);
        struck = victim.Medical.Parts[0];
        Assert.Equal(28, struck.Wear);
        before = (struck.Flesh, struck.Wear, victim.Medical.Blood);
        Assert.True(CombatSystem.TryBisect(victim, 0, damage, CombatWoundMechanism.Slash, true, 0, 2, events,
            visualSeed: 1, style: TorsoBisectionStyle.Transverse, brutality: 100));
        Assert.Equal(before, (struck.Flesh, struck.Wear, victim.Medical.Blood));
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);
        Assert.Equal(TorsoBisectionPolicy.StateForHit(part, 2, 1, style: TorsoBisectionStyle.Transverse), victim.TorsoBisection);
        Assert.False(CombatSystem.TryBisect(victim, 0, damage, CombatWoundMechanism.Slash, false, 0, 2, events, brutality: 100));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_real_robot_torso_hit_publishes_the_cut_after_native_death_with_its_original_part(bool stomach)
    {
        using var scene = new Scene(stomach ? RobotStomach : RobotChest, level: 100, power: 100);
        var hit = scene.Hit();
        Assert.True(scene.Victim.Medical!.Dead);
        var cut = Assert.Single(scene.Events, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Equal((scene.Attacker, scene.Target, hit.Part, hit.Damage, hit.Direction, hit.CutDirection, hit.Mechanism),
            (cut.Attacker, cut.Victim, cut.Part, cut.Damage, cut.Direction, cut.CutDirection, cut.Mechanism));
        var published = scene.Events.ToArray();
        Assert.Single(published, e => e.Kind == CombatEventKind.Died);
        Assert.True(Array.IndexOf(published, cut) > Array.FindIndex(published, e => e.Kind == CombatEventKind.Died));
        Assert.True(scene.Victim.TorsoBisection!.Value.IsValid);
        Assert.Equal(100, hit.AttackerBrutality);
    }

    [Theory]
    [InlineData(39, 2, false)] [InlineData(40, 2, true)] [InlineData(100, 4, false)] [InlineData(100, 1, true)]
    public void A_robot_head_only_splits_after_native_destruction_and_the_same_slash_unlock(float level, int direction, bool cuts)
    {
        using var scene = new Scene(Head, level: level, direction: direction);
        scene.Victim.Medical!.Parts[0].Flesh = -199;
        scene.Victim.Medical.KnockOut(600);
        float experience = scene.Source.Brutality.Experience;
        scene.Hit();
        Assert.True(scene.Victim.Medical.Dead);
        if (cuts)
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
        Assert.Single(scene.Events, e => e.Kind == CombatEventKind.Died);
        Assert.Equal(experience, scene.Source.Brutality.Experience);
    }

    [Fact]
    public void A_nonlethal_robot_head_hit_cannot_use_the_visual_candidate_to_kill_or_separate_it()
    {
        using var scene = new Scene(Head, level: 100);
        scene.Hit();
        Assert.False(scene.Victim.Medical!.Dead);
        Assert.True(scene.Victim.Medical.Parts[0].Flesh > 0);
        Assert.Null(scene.Victim.HeadCut);
        Assert.False(scene.Victim.Medical.Decapitated);
        Assert.DoesNotContain(scene.Events, e => e.Kind == CombatEventKind.Died);
    }

    [Theory]
    [InlineData(39, false)] [InlineData(40, true)]
    public void Robot_execution_uses_the_head_unlock_without_training_on_a_prisoner(float level, bool cuts)
    {
        using var scene = new Scene(Head, level: level);
        scene.Victim.Medical!.Parts[0].Flesh = -199;
        scene.Victim.Medical.KnockOut(600);
        scene.Victim.InSomething = HeldState.Imprisoned;
        float experience = scene.Source.Brutality.Experience;
        Assert.True(scene.Combat.Execute(scene.World, scene.Attacker.Slot, scene.Target.Slot, 0));
        Assert.True(scene.Victim.Medical.Dead);
        Assert.Equal(cuts, scene.Victim.HeadCut is { IsValid: true });
        Assert.Equal(cuts, scene.Victim.Medical.Decapitated);
        Assert.Equal(experience, scene.Source.Brutality.Experience);
    }

    [Fact]
    public void Native_robot_limbs_still_sever_but_a_fitted_replacement_keeps_its_replacement_rules()
    {
        var victim = Victim(LeftArm, 100);
        var events = new List<MedicalEvent>();
        victim.Medical!.ApplyHit(0, new(350, 0, 0), Context(victim.Race), events);
        Assert.Equal(LimbState.Stump, victim.Medical.Parts[0].Limb);
        Assert.Single(events, e => e.Kind == MedicalEventKind.PartSevered);
        Assert.Equal(14, victim.Medical.Parts[0].Wear);

        victim = Victim(LeftArm, 100);
        var replacement = victim.Medical!.Parts[0];
        replacement.Limb = LimbState.Replaced;
        replacement.Replacement = new() { StringId = "fitted-robot-arm", Slot = 50, HpLow = 100, HpHigh = 100 };
        events.Clear();
        victim.Medical.ApplyHit(0, new(350, 0, 0), Context(victim.Race), events);
        Assert.Equal(LimbState.Replaced, replacement.Limb);
        Assert.NotNull(replacement.Replacement);
        Assert.DoesNotContain(events, e => e.Kind == MedicalEventKind.PartSevered);
        Assert.Equal(14, replacement.Wear);
    }

    [Fact]
    public void Actual_robot_head_and_torso_cuts_survive_native_character_state_bytes()
    {
        using var head = new Scene(Head, level: 100);
        head.Victim.Medical!.Parts[0].Flesh = -199;
        head.Hit();
        var headCut = Assert.IsType<HeadCutState>(head.Victim.HeadCut);
        using var torso = new Scene(RobotStomach, level: 100, power: 100);
        torso.Hit();
        var torsoCut = Assert.IsType<TorsoBisectionState>(torso.Victim.TorsoBisection);
        var headState = new FcsRecord { StringId = "robot-head", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        var torsoState = new FcsRecord { StringId = "robot-torso", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        HeadCutSave.Write(headState, headCut, corpse: true);
        TorsoCarrySave.Write(torsoState, torso.Victim);
        var file = new FcsFile();
        file.Records.AddRange([headState, torsoState]);
        using var bytes = new MemoryStream();
        FcsWriter.Write(file, bytes);
        bytes.Position = 0;
        var records = FcsReader.Read(bytes).Records;
        Assert.Equal(headCut, HeadCutSave.Read(records.Single(r => r.StringId == "robot-head")));
        var restored = new CharacterCold { Race = torso.Victim.Race };
        TorsoCarrySave.Read(records.Single(r => r.StringId == "robot-torso"), restored);
        Assert.True(restored.Race!.IsRobot);
        Assert.Equal(torsoCut, restored.TorsoBisection);
    }

    static CharacterCold Victim(BodyPartTemplate part, float hp = 200)
    {
        var race = new RaceData { StringId = "robot-cut-target", Name = "Skeleton", IsRobot = true, BleedRate = .1f,
            Anatomy = [new(part, 100, hp)] };
        return new() { Race = race, Medical = MedicalState.Create(race, 50), Fighter = new Fighter(null) };
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

        public Scene(BodyPartTemplate targetPart, float level = 0, float power = 1, int direction = 2)
        {
            var options = BodyOptions.Default with { Gore = true };
            options.ForcedHitPart = (targetPart.StringId, targetPart.Name);
            Combat = new([Cut("cut left", direction: direction)], Constants, BaseGame, options, DuelFixture.Lengths,
                new CombatOptions { RecordLog = true, TickMedical = false });
            World = new(new WorldSettings { Seed = 7, PublishSnapshots = false },
                new OpenGroundWalkability((_, _) => 150), [Combat, Senses(Combat)]);
            World.CombatEvents = Events;
            var source = new Combatant { Weapon = Katana with { CutDamageMultiplier = power } }.MakeCold();
            source.Faction = 1;
            source.Brutality = BrutalityProgress.FromLevel(level);
            var victim = new Combatant().MakeCold();
            var target = RobotGoreCombatTests.Victim(targetPart);
            victim.Faction = 2;
            victim.Race = target.Race;
            victim.Medical = target.Medical;
            Attacker = World.Characters.Spawn(new() { Position = new(1000, 150, 1000), Yaw = MathF.PI / 2, Health = 100 }, source, 0);
            Target = World.Characters.Spawn(new() { Position = new(1014, 150, 1000), Yaw = -MathF.PI / 2, Health = 100 }, victim, 0);
            World.Commands.Enqueue(new AttackOrder([Attacker], Target) { Tick = 0, Picked = true, AllowIncapacitated = true });
            World.Commands.Enqueue(new AttackOrder([Target], Attacker) { Tick = 0 });
            for (int i = 0; i < 180 && Combat.StateOf(Attacker).AttackTech < 0; i++) World.RunTick();
            Assert.True(Combat.StateOf(Attacker).AttackTech >= 0);
            World.RunTick();
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
            finally { World.Characters.Phase = TablePhase.Outside; }
            return Assert.Single(Events, e => e.Kind == CombatEventKind.Hit);
        }

        public void Dispose() => World.Dispose();
    }
}
