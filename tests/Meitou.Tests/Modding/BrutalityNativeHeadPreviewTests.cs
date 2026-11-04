using System.Numerics;
using Meitou.Brutality;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalityNativeHeadPreviewTests
{
    sealed class Recorder : ICombatHitObserver
    {
        public readonly List<CombatHit> Hits = [];
        public void HitResolved(SimWorld world, in CombatHit hit) => Hits.Add(hit);
    }
    sealed class Scene : IDisposable
    {
        public readonly BodyOptions Options = BodyOptions.Default;
        public readonly BrutalityGameplaySystem Gameplay;
        public readonly SimWorld World;
        public readonly Recorder Observer = new();
        public readonly CharacterCold Source = new Combatant().MakeCold("source"), Victim = new Combatant().MakeCold("victim");
        public readonly CharacterId Attacker, Target;
        public Scene(string profile)
        {
            var database = new GameDatabase(); var file = new FcsFile();
            file.Records.Add(new FcsRecord { StringId = "native-preview-race", Name = "Human", RecordType = FcsRecordType.RACE }); database.Apply(file, "test.mod");
            Source.Appearance = new CharacterAppearance { Race = database.Find("native-preview-race")! }; Victim.Appearance = Source.Appearance;
            Source.IsPlayer = true; Source.Faction = 1; Victim.Faction = 2;
            var combat = new CombatSystem([Cut()], Constants, BaseGame, Options, DuelFixture.Lengths, new CombatOptions { TickMedical = false });
            Gameplay = new(combat, BaseGame, Options, (RngPurpose)0xB17A1171);
            World = new(new WorldSettings { Seed = 17, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 500),
                [new GoreVisualCheckSystem(profile), combat, Gameplay, Observer]);
            Attacker = World.Characters.Spawn(new() { Position = new(-15, 500, 0), Health = 100 }, Source, 0);
            Target = World.Characters.Spawn(new() { Position = new(15, 500, 0), Yaw = .45f, Velocity = new(3, 0, 2), Health = 100 }, Victim, 0);
            Assert.True(Gameplay.SetLevel(World, Attacker, 42));
        }
        public void Dispose() => World.Dispose();
    }

    [Theory]
    [InlineData(0, "oblique", false)] [InlineData(1, "split", true)] [InlineData(2, "neck", true)]
    public void Native_reference_fires_at_tick120_with_the_pinned_head_packet_seed_and_launch(int variant, string style, bool decapitated)
    {
        using var scene = new Scene($"head:{variant}:native");
        scene.World.RunTicks(120);
        Assert.Empty(scene.Observer.Hits); Assert.False(scene.Victim.Medical!.Dead);
        int head = scene.Victim.Medical.Parts.ToList().FindIndex(part => part.Template.Type == Meitou.Data.Gameplay.Bodies.BodyPartType.Head);
        float headBefore = scene.Victim.Medical.Parts[head].Flesh;
        int chest = scene.Victim.Medical.Parts.ToList().FindIndex(part => part.Template == Chest);
        float chestBefore = scene.Victim.Medical.Parts[chest].Flesh;
        var previousMotion = scene.World.Characters.Previous[scene.Target.Slot].Velocity;
        scene.World.RunTick();
        var hit = Assert.Single(scene.Observer.Hits);
        Assert.Equal(120, hit.Tick); Assert.Equal(scene.Attacker, hit.Attacker); Assert.Equal(scene.Target, hit.Victim);
        Assert.Equal(head, hit.Part); Assert.Equal(new HitDamage(300, 0, 0), hit.Damage); Assert.Equal(1, hit.CutDirection);
        Assert.Equal(new Vector3(30, 0, 0), hit.Travel); Assert.True(hit.VictimWasAlive); Assert.False(hit.VictimWasIncapacitated);
        Assert.Equal(chestBefore, scene.Victim.Medical.Parts[chest].Flesh); Assert.Equal(headBefore - 300, scene.Victim.Medical.Parts[head].Flesh);
        Assert.True(scene.Victim.Medical.Dead); Assert.Equal(decapitated, scene.Victim.Medical.Decapitated); Assert.True(scene.Options.Decapitation);
        var blow = Assert.IsType<ValueTuple<Vector3, Vector3>>(scene.Victim.Blow);
        Assert.Equal(new Vector3(50, 10, 0), blow.Item2); Assert.Equal(previousMotion + blow.Item2, blow.Item1);
        Assert.Equal(Vector3.Zero, scene.World.Characters.Previous[scene.Target.Slot].Velocity);
        ulong seed = Rng.Hash(scene.World.Seed, Rng.Key(scene.Target) ^ Rng.Mix(Rng.Key(scene.Attacker)), (RngPurpose)32, 120);
        var expected = HeadCutPolicy.Preview(style, 1, seed, new(30, 0), .45f);
        var view = Assert.Single(scene.Gameplay.ActorViews, actor => actor.Character == scene.Target);
        Assert.Equal(expected, view.Head);
        Assert.Single(scene.Gameplay.CutRequests, cut => cut.Kind == GoreCutKind.Head);
        Assert.Equal(42, scene.Gameplay.LevelOf(scene.World, scene.Attacker), .001f);
        Assert.False(scene.Gameplay.ApplyMatchedNativeHeadCut(scene.World, scene.Target, scene.Attacker, variant));
        Assert.Single(scene.Observer.Hits);
    }

    [Fact]
    public void Preview_without_native_token_keeps_its_existing_early_visual_only_recipe()
    {
        using var scene = new Scene("head:1");
        scene.World.RunTicks(4);
        Assert.True(scene.Victim.Medical!.Dead); Assert.Empty(scene.Observer.Hits); Assert.Null(scene.Victim.Blow);
        Assert.Contains(scene.Gameplay.CutRequests, request => request.Kind == GoreCutKind.Head && request.Tick == 3);
    }
}
