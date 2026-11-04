using System.Numerics;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalityImpactEffectsTests
{
    const RngPurpose Purpose = (RngPurpose)0xB17A1171;
    sealed class Scene : IDisposable
    {
        public ImpactEffects Effects { get; } = new();
        public BrutalityGameplaySystem Gameplay { get; } = new(null, BaseGame, BodyOptions.Default, Purpose);
        public SimWorld World { get; }
        public CharacterCold Victim { get; } = new Combatant().MakeCold();
        public CharacterId SourceId { get; }
        public CharacterId VictimId { get; }
        public Scene(bool robot = false, ulong seed = 17)
        {
            Victim.Race = new() { Anatomy = Human.Anatomy.Select(entry => entry.Part == Chest ? entry with { Part = Chest with { StringId = ArmourData.ChestPartId } } : entry).ToArray(), IsRobot = robot, BloodColour = 0x00FF00 };
            Victim.Medical = Healthy(Victim.Race);
            World = new(new WorldSettings { Seed = seed, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [Gameplay, Effects]);
            var source = new Combatant().MakeCold(); source.IsPlayer = true; source.Faction = 1; source.InCombat = true; Victim.Faction = 2; Victim.InCombat = true;
            SourceId = World.Characters.Spawn(new(), source, 0); VictimId = World.Characters.Spawn(new() { Position = new(10, 0, 0) }, Victim, 0); Gameplay.Schedule(World);
        }
        public CombatHit Hit(float cut = 20, int part = LArm, int sequence = 1) => new(World.Tick, CombatHitSource.Melee, SourceId, VictimId, sequence, 1, part,
            new(cut, 0, 0), default, new(10, 0, 0), Vector3.UnitX, 0, 2, false, true, true, false);
        public void Dispose() => World.Dispose();
    }
    [Theory]
    [InlineData(1, false, false, 4)]
    [InlineData(2, false, false, 0)]
    [InlineData(3, false, false, 0)]
    [InlineData(0, false, false, 4)]
    [InlineData(1, true, false, 0)]
    [InlineData(1, false, true, 12)]
    public void Mechanical_hits_keep_the_pinned_slash_and_rupture_gates(int mechanism, bool projectile, bool rupture, int flags)
    {
        using var scene = new Scene(robot: true); var hit = scene.Hit() with { Source = projectile ? CombatHitSource.Projectile : CombatHitSource.Melee };
        scene.Effects.Capture(scene.World, hit, (CombatWoundMechanism)mechanism, false, rupture ? 0 : -1); scene.Effects.HitResolved(scene.World, hit); scene.Effects.SlowWorld(scene.World);
        var events = scene.Effects.FrameAt(1)!.Events;
        if (flags == 0) Assert.Empty(events); else Assert.Equal((ImpactEffectKind)flags, Assert.Single(events).Effects);
    }
    [Fact]
    public void Ordinary_organic_spray_uses_the_captured_race_and_only_heavy_openings_throw_splinters()
    {
        using var scene = new Scene(); var light = scene.Hit(14); var heavy = scene.Hit(15, sequence: 2);
        scene.Effects.Capture(scene.World, light, CombatWoundMechanism.Slash, false); scene.Effects.Capture(scene.World, heavy, CombatWoundMechanism.Slash, false);
        scene.Victim.Race = new() { IsRobot = true, BloodColour = 0xFF0000 };
        scene.Effects.HitResolved(scene.World, light); scene.Effects.HitResolved(scene.World, heavy); scene.Effects.SlowWorld(scene.World);
        var events = scene.Effects.FrameAt(1)!.Events; Assert.Equal(2, events.Count);
        Assert.Equal(ImpactEffectKind.OrganicSpray, events[0].Effects); Assert.Equal(ImpactEffectKind.OrganicSpray | ImpactEffectKind.OrganicSplinters, events[1].Effects);
        Assert.All(events, effect => { Assert.Equal(0x00FF00u, effect.BloodColour); Assert.False(effect.Robot); Assert.Equal("Bip01 L UpperArm", effect.ImpactBone); });
    }
    [Fact]
    public void An_accepted_real_organic_torso_cut_keeps_its_separate_Hit_spray_and_splinters()
    {
        ulong seed = 17, counter = CombatRolls.BlowCounter(0, 1, 1, 0);
        while (Rng.Float(Rng.Hash(seed, Rng.Key(new CharacterId(1, 0)) ^ 0x424953454354UL, Purpose, counter)) >= .9f) seed++;
        using var scene = new Scene(seed: seed); scene.Gameplay.SetLevel(scene.World, scene.SourceId, 100);
        int chest = scene.Victim.Medical!.Parts.ToList().FindIndex(part => part.Template.StringId == ArmourData.ChestPartId);
        var hit = scene.Hit(300, chest); var before = scene.Gameplay.CaptureBeforeHit(scene.World, scene.SourceId, scene.VictimId, chest, new(300, 0, 0, 0, 1, 0),
            CombatWoundMechanism.Slash, Context(scene.Victim.Race)) with { ChallengeFactor = 0 };
        scene.Effects.Capture(scene.World, hit, CombatWoundMechanism.Slash, false); scene.Victim.Medical.ApplyHit(chest, hit.Damage, Context(scene.Victim.Race));
        scene.Gameplay.RecordHitBridge(GoreHitKey.From(hit), before); scene.Gameplay.HitResolved(scene.World, hit); scene.Effects.HitResolved(scene.World, hit);
        scene.Gameplay.SlowWorld(scene.World); Assert.Contains(scene.Gameplay.CutRequests, request => request.Kind == GoreCutKind.Torso);
        scene.Effects.SlowWorld(scene.World); var effect = Assert.Single(scene.Effects.FrameAt(1)!.Events);
        Assert.Equal(hit, effect.Hit); Assert.Equal(ImpactEffectKind.OrganicSpray | ImpactEffectKind.OrganicSplinters, effect.Effects);
    }
    [Fact]
    public void Repeated_delivery_is_once_only_and_history_is_bounded_without_resetting_original_birth_ticks()
    {
        using var scene = new Scene(); var first = scene.Hit(); scene.Effects.HitResolved(scene.World, first); scene.Effects.HitResolved(scene.World, first); scene.Effects.SlowWorld(scene.World);
        var old = scene.Effects.FrameAt(1)!; Assert.Single(old.Events);
        for (int i = 2; i <= 700; i++) scene.Effects.HitResolved(scene.World, scene.Hit(sequence: i));
        scene.Effects.SlowWorld(scene.World); var recent = scene.Effects.FrameAt(1)!;
        Assert.Equal(512, recent.Events.Count); Assert.All(recent.Events, effect => Assert.Equal(0, effect.Hit.Tick));
        Assert.Single(old.Events); Assert.Equal(first, old.Events[0].Hit);
    }
    [Fact]
    public void Actual_native_execution_reaches_the_effects_stream_even_without_a_generic_execution_observer()
    {
        var harmony = new Harmony("brutality.impact-execution." + Guid.NewGuid().ToString("N")); CombatAdapter.Install(harmony);
        try
        {
            var combat = new CombatSystem([Cut("cut left")], Constants, BaseGame, BodyOptions.Default, DuelFixture.Lengths, new() { TickMedical = false });
            var gameplay = new BrutalityGameplaySystem(combat, BaseGame, BodyOptions.Default, Purpose); var effects = new ImpactEffects();
            using var world = new SimWorld(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [combat, gameplay, effects]);
            var source = world.Characters.Spawn(new(), new Combatant().MakeCold(), 0); var target = world.Characters.Spawn(new() { Position = new(10, 0, 0) }, new Combatant().MakeCold(), 0);
            world.RunTick(); combat.Execute(world, source.Slot, target.Slot, 1); world.RunTick();
            var effect = Assert.Single(effects.FrameAt(world.Tick)!.Events); Assert.True(effect.Execution); Assert.Equal(source, effect.Hit.Attacker);
            Assert.True(effect.Effects.HasFlag(ImpactEffectKind.OrganicSpray));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
