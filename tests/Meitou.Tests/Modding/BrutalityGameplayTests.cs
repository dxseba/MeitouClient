using System.Numerics;
using Meitou.Brutality;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

public sealed class BrutalityGameplayTests
{
    const RngPurpose Purpose = (RngPurpose)0xB17A1171;

    sealed class Scene : IDisposable
    {
        public SimWorld World { get; }
        public BrutalityGameplaySystem Gameplay { get; }
        public CharacterCold Source { get; } = new Combatant().MakeCold("source");
        public CharacterCold Victim { get; } = new Combatant().MakeCold("victim");
        public CharacterId SourceId { get; }
        public CharacterId VictimId { get; }
        public Scene(int padding = 0, ulong seed = 17, bool canonicalChest = false)
        {
            Source.IsPlayer = true; Source.Faction = 1; Source.InCombat = true;
            Victim.Faction = 2; Victim.InCombat = true;
            if (canonicalChest)
            {
                Victim.Race = new RaceData { Anatomy = Human.Anatomy.Select(entry => entry.Part == Chest
                    ? entry with { Part = Chest with { StringId = ArmourData.ChestPartId } } : entry).ToArray() };
                Victim.Medical = Healthy(Victim.Race);
            }
            Gameplay = new(null, BaseGame, BodyOptions.Default, Purpose);
            World = new(new WorldSettings { Seed = seed }, new OpenGroundWalkability((_, _) => 0), [Gameplay]);
            for (int i = 0; i < padding; i++) World.Characters.Spawn(new(), new CharacterCold { Faction = 9 }, 0);
            SourceId = World.Characters.Spawn(new() { Position = new(10, 0, 10) }, Source, 0);
            VictimId = World.Characters.Spawn(new() { Position = new(20, 0, 10) }, Victim, 0);
            Gameplay.Schedule(World);
        }
        public CombatHit Hit(int part, HitDamage damage, int sequence = 1) => new(World.Tick, CombatHitSource.Melee, SourceId, VictimId,
            sequence, 0, part, damage, default, new(20, 0, 10), Vector3.UnitX, 0, 2, false, true, true, false);
        public GoreHitBridge Before(int part, DamagePacket packet) => Gameplay.CaptureBeforeHit(World, SourceId, VictimId, part, packet,
            CombatWoundMechanism.Slash, Context(Victim.Race), new CombatSlot
            {
                TargetSlot = SourceId.Slot, TargetGeneration = SourceId.Generation, AttackTech = -1, ReactTech = -1,
            });
        public void Deliver(in CombatHit hit, GoreHitBridge? bridge)
        {
            if (bridge is not null) Gameplay.RecordHitBridge(GoreHitKey.From(hit), bridge);
            Gameplay.HitResolved(World, hit);
        }
        public BrutalityActorView View(CharacterId id) => Assert.Single(Gameplay.ActorViews, view => view.Character == id);
        public void Dispose() => World.Dispose();
    }

    [Theory]
    [InlineData(19.99f, 0, 0f, 0f)]
    [InlineData(20f, 1, 0f, 0f)]
    [InlineData(40f, 2, 0f, 0f)]
    [InlineData(59.99f, 2, 0f, 0f)]
    [InlineData(60f, 2, .10f, 0f)]
    [InlineData(70f, 2, .30f, 0f)]
    [InlineData(80f, 2, .55f, .01f)]
    [InlineData(90f, 2, .80f, .03f)]
    [InlineData(100f, 2, .95f, .05f)]
    public void The_DLL_keeps_the_reference_unlocks_and_probability_anchors(float level, int headTier, float torso, float pair)
    {
        Assert.Equal(headTier, BrutalityPolicy.HeadTier(level));
        Assert.Equal(torso, BrutalityPolicy.BisectionChance(level), 6);
        Assert.Equal(pair, PairedLimbPolicy.Chance(level), 6);
        Assert.Equal(level >= 40, BrutalityPolicy.CanDecapitate(level, CombatWoundMechanism.Slash));
        Assert.False(BrutalityPolicy.CanDecapitate(level, CombatWoundMechanism.Thrust));
        Assert.False(BrutalityPolicy.CanDecapitate(level, CombatWoundMechanism.Crush));
    }

    [Fact]
    public void Useful_XP_stops_at_pre_hit_health_and_ignores_overkill_extra_stun_and_missing_capture()
    {
        using var scene = new Scene();
        var limb = scene.Victim.Medical!.Parts[LArm];
        limb.Flesh = -limb.MaxHp + 10;
        // Supply a fixed honest-training multiplier so this test isolates the useful-damage cap.
        var bridge = scene.Before(LArm, new(100, 0, 0, 999, 1, 0)) with { ChallengeFactor = 1 };
        Assert.Equal(10f, bridge.UsefulRemainingBeforeHit);
        Assert.Equal(1f, bridge.ChallengeFactor);
        var hit = scene.Hit(LArm, new(100, 0, 0, 999));
        scene.Victim.Medical.ApplyHit(LArm, hit.Damage, Context());
        Assert.Equal(LimbState.Stump, limb.Limb);
        scene.Deliver(hit, bridge with { SeveredLimbSlots = [0] });
        scene.Gameplay.SlowWorld(scene.World);
        Assert.Equal(10 * BaseGame.ExpGainMultiplier, scene.View(scene.SourceId).Experience, 5);
        Assert.Contains(scene.Gameplay.CutRequests, request => request.Kind == GoreCutKind.Limb && request.LimbSlot == 0);

        var later = scene.Hit(HeadIx, new(1000, 1000, 1000, 1000), sequence: 2);
        scene.Deliver(later, null);
        scene.Gameplay.SlowWorld(scene.World);
        Assert.Equal(10 * BaseGame.ExpGainMultiplier, scene.View(scene.SourceId).Experience, 5);
        Assert.True(scene.Gameplay.Gaps.HasFlag(GoreIntegrationGap.PreHitContext));
        var progress = new BrutalityProgress();
        progress.Award(float.MaxValue);
        Assert.Equal(100f, progress.Level);
        Assert.Equal(0f, progress.Award(1));
        Assert.Equal(0f, progress.Award(float.NaN));
    }

    [Theory]
    [InlineData("prisoner")]
    [InlineData("unconscious")]
    [InlineData("bed")]
    [InlineData("ally")]
    [InlineData("carried")]
    public void Captured_helpless_or_allied_targets_never_train_brutality(string reason)
    {
        using var scene = new Scene();
        switch (reason)
        {
            case "prisoner": scene.Victim.InSomething = Meitou.Simulation.Locks.HeldState.Imprisoned; break;
            case "unconscious": scene.Victim.Medical!.KnockOut(100); break;
            case "bed": scene.Victim.InSomething = Meitou.Simulation.Locks.HeldState.Bed; break;
            case "ally": scene.Victim.Faction = scene.Source.Faction; break;
            case "carried": scene.Victim.CarriedBy = scene.SourceId; break;
        }
        var bridge = scene.Before(LArm, new(20, 0, 0, 0, 1, 0));
        Assert.Equal(0f, bridge.ChallengeFactor);
        scene.Deliver(scene.Hit(LArm, new(20, 0, 0)), bridge);
        scene.Gameplay.SlowWorld(scene.World);
        Assert.Equal(0f, scene.View(scene.SourceId).Experience);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_queued_torso_hit_keeps_its_own_lethal_state_when_another_hit_arrives_before_drain(bool lethalTorso)
    {
        // Pick an eligible high-level cleave roll, so a missing cut cannot hide the causality regression.
        ulong seed = 0;
        ulong counter = CombatRolls.BlowCounter(0, 1, 0, 0);
        while (Rng.Float(Rng.Hash(seed, Rng.Key(new CharacterId(1, 0)) ^ 0x424953454354UL, Purpose, counter)) >= .9f) seed++;
        using var scene = new Scene(seed: seed, canonicalChest: true);
        Assert.True(scene.Gameplay.SetLevel(scene.World, scene.SourceId, 100));
        int chest = scene.Victim.Medical!.Parts.ToList().FindIndex(part => part.Template.StringId == ArmourData.ChestPartId);
        var damage = new HitDamage(lethalTorso ? 300 : 110, 0, 0);
        var bridge = scene.Before(chest, new(damage.Cut, 0, 0, 0, 1, 0)) with { ChallengeFactor = 0 };
        var torsoHit = scene.Hit(chest, damage);
        scene.Victim.Medical.ApplyHit(chest, damage, Context(scene.Victim.Race));
        Assert.Equal(lethalTorso, scene.Victim.Medical.Dead);
        scene.Deliver(torsoHit, bridge);
        if (!lethalTorso)
        {
            var headHit = scene.Hit(HeadIx, new(300, 0, 0), sequence: 2) with { VictimWasIncapacitated = true };
            var headBridge = scene.Before(HeadIx, new(300, 0, 0, 0, 1, 0)) with { ChallengeFactor = 0, HeadSeveranceAccepted = false };
            scene.Victim.Medical.ApplyHit(HeadIx, headHit.Damage, Context(scene.Victim.Race));
            scene.Deliver(headHit, headBridge);
        }
        Assert.True(scene.Victim.Medical.Dead);
        scene.Gameplay.SlowWorld(scene.World);
        var state = scene.View(scene.VictimId);
        Assert.Equal(lethalTorso, state.Torso is not null);
        Assert.Equal(lethalTorso, scene.Gameplay.CutRequests.Any(request => request.Kind == GoreCutKind.Torso));
    }

    [Fact]
    public void Durable_keys_restore_the_right_character_after_runtime_slots_change_and_preserve_unspawned_state()
    {
        const string sourceKey = "save/34/2", victimKey = "save/99/4";
        using var original = new Scene();
        original.Gameplay.SetLevel(original.World, original.SourceId, 73);
        original.Victim.Medical!.Sever(LArm, Context());
        original.Gameplay.RecordLimbSeverance(original.Hit(LArm, default), 0, 21);
        var saved = original.Gameplay.ExportDurable(id => id == original.SourceId ? sourceKey : id == original.VictimId ? victimKey : null);
        using var bytes = new MemoryStream();
        BrutalityStateCodec.Write(bytes, saved); bytes.Position = 0;
        var decoded = BrutalityStateCodec.Read(bytes);
        Assert.Equal(2, decoded.Count);

        using var restored = new Scene(padding: 3);
        Assert.NotEqual(original.SourceId.Slot, restored.SourceId.Slot);
        int bound = restored.Gameplay.ImportDurable(restored.World, decoded, key => key == sourceKey ? restored.SourceId : null);
        Assert.Equal(1, bound);
        Assert.Equal(73f, restored.Gameplay.LevelOf(restored.World, restored.SourceId), 5);
        var stillPending = restored.Gameplay.ExportDurable(id => id == restored.SourceId ? sourceKey : null);
        var pendingVictim = Assert.Single(stillPending, row => row.Key == victimKey);
        Assert.Equal(Assert.Single(saved, row => row.Key == victimKey).Limbs, pendingVictim.Limbs);
        Assert.Equal(1, restored.Gameplay.ResolveDurable(restored.World, key => key == victimKey ? restored.VictimId : null));
        restored.Gameplay.SlowWorld(restored.World);
        Assert.Equal(Assert.Single(pendingVictim.Limbs), Assert.Single(restored.View(restored.VictimId).Limbs));

        original.World.Characters.Remove(original.SourceId);
        original.Gameplay.SlowWorld(original.World);
        Assert.DoesNotContain(original.Gameplay.ActorViews, row => row.Character == original.SourceId);
        var offscreen = original.Gameplay.DurableFor(original.Source, sourceKey);
        Assert.NotNull(offscreen);
        Assert.Equal(73f, MathF.Sqrt(offscreen.Experience / BrutalityProgress.ExperiencePerLevelSquared), 5);
        Assert.Empty(original.Gameplay.ExportDurable(_ => null));
    }
}
