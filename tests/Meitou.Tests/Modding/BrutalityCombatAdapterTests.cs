using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Data.Gameplay;
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

[CollectionDefinition("Brutality native combat", DisableParallelization = true)]
public sealed class BrutalityCombatCollection { }

[Collection("Brutality native combat")]
public sealed class BrutalityCombatAdapterTests
{
    const RngPurpose Purpose = (RngPurpose)0xB17A1171;
    sealed class Patches : IDisposable
    {
        public Harmony Harmony { get; } = new("brutality.combat.test." + Guid.NewGuid().ToString("N"));
        public Patches() => CombatAdapter.Install(Harmony, Purpose);
        public void Dispose() => Harmony.UnpatchAll(Harmony.Id);
    }
    sealed class Recorder : ICombatHitObserver
    {
        public List<CombatHit> Hits { get; } = [];
        public void HitResolved(SimWorld world, in CombatHit hit) => Hits.Add(hit);
    }
    sealed class Scene : IDisposable
    {
        public GameConstants Game { get; } = BaseGame with { ImmediateBloodLoss = 0 };
        public CombatSystem Combat { get; }
        public BrutalityGameplaySystem Gameplay { get; }
        public Recorder Observer { get; } = new();
        public SimWorld World { get; }
        public CharacterCold Source { get; }
        public CharacterCold Victim { get; }
        public CharacterId SourceId { get; }
        public CharacterId VictimId { get; }
        public BodyOptions Options { get; }
        public Scene(BodyOptions options, int direction = 2, ulong seed = 11, CharacterCold? source = null, CharacterCold? victim = null)
        {
            Options = options; Source = source ?? new Combatant().MakeCold(); Victim = victim ?? new Combatant().MakeCold();
            Source.IsPlayer = true; Source.Faction = 1; Victim.Faction = 2;
            Combat = new([Cut("cut left", direction: direction)], Constants, Game, options, DuelFixture.Lengths,
                new CombatOptions { TickMedical = false, RecordLog = true });
            Gameplay = new(Combat, Game, options, Purpose);
            World = new(new WorldSettings { Seed = seed, PublishSnapshots = false, Threads = 1 },
                new OpenGroundWalkability((_, _) => 150), [Combat, Senses(Combat), Gameplay, Observer]);
            SourceId = Put(Source, new(1000, 150, 1000));
            VictimId = Put(Victim, new(Source.Fighter?.Crossbow is null ? 1014 : 1300, 150, 1000));
            World.RunTick(); // Allocate native read/write combat slots, without issuing an attack.
        }
        CharacterId Put(CharacterCold cold, Vector3 at) => World.Characters.Spawn(new() { Position = at, Health = 100 }, cold, 0);
        public void Apply()
        {
            // Drive the real committed native contact; only its already-selected attack slot is the fixture setup.
            var write = (CombatSlot[])typeof(CombatSystem).GetField("write", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Combat)!;
            var attack = CombatSlot.Fresh(SourceId.Generation);
            attack.AttackTech = 0; attack.AttackSeq = 1; attack.TargetSlot = VictimId.Slot; attack.TargetGeneration = VictimId.Generation;
            var defend = CombatSlot.Fresh(VictimId.Generation);
            defend.TargetSlot = SourceId.Slot; defend.TargetGeneration = SourceId.Generation;
            write[SourceId.Slot] = attack; write[VictimId.Slot] = defend;
            Source.InCombat = Victim.InCombat = true;
            Combat.Apply(World, new Effect { Kind = CombatSystem.BlowEffect, Source = SourceId.Slot, Target = VictimId.Slot, Amount = 1, Arg = 1 });
            Gameplay.SlowWorld(World);
        }
        public BrutalityActorView View(CharacterId id) => Assert.Single(Gameplay.ActorViews, view => view.Character == id);
        public void Dispose() => World.Dispose();
    }

    [Theory]
    [InlineData(0, Dismemberment.Rare, 1, false)]
    [InlineData(100, Dismemberment.Never, 1, false)]
    [InlineData(100, Dismemberment.Rare, 1, true)]
    [InlineData(100, Dismemberment.Rare, 2, true)]
    [InlineData(100, Dismemberment.Rare, 4, false)]
    public void Real_head_hits_require_mastery_the_native_medical_gate_and_a_slash(float level, Dismemberment setting, int direction, bool expectCut)
    {
        using var patches = new Patches();
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (Head.StringId, Head.Name), Dismemberment = setting }, direction);
        scene.Gameplay.SetLevel(scene.World, scene.SourceId, level);
        var head = scene.Victim.Medical!.Parts[HeadIx]; head.Flesh = -head.MaxHp + 1;
        scene.Apply();
        Assert.True(scene.Victim.Medical.Dead);
        var hit = Assert.Single(scene.Observer.Hits);
        Assert.Equal(HeadIx, hit.Part);
        Assert.Equal(direction, hit.CutDirection);
        var cut = scene.View(scene.VictimId).Head;
        Assert.Equal(expectCut, cut is not null);
        Assert.Equal(expectCut && cut!.Value.Mode != HeadCutMode.AttachedSplit, scene.Victim.Medical.Decapitated);
        Assert.False(scene.Gameplay.Gaps.HasFlag(GoreIntegrationGap.PreHitContext));
        Assert.False(scene.Gameplay.Gaps.HasFlag(GoreIntegrationGap.HeadMedicalGate));
        Assert.False(CombatAdapter.HasActiveContact);
    }

    [Fact]
    public void The_real_selected_part_and_pre_damage_health_limit_XP_before_native_combat_XP_changes_stats()
    {
        using var patches = new Patches();
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (LeftArm.StringId, LeftArm.Name), GlobalDamageMultiplier = 10 });
        var part = scene.Victim.Medical!.Parts[LArm]; part.Flesh = 10;
        float before = part.Flesh + part.MaxHp - part.Stun;
        float challenge = new XpService(scene.Game, scene.Options).SkillDifferenceFactor(
            scene.Source.Stats![StatsEnumerated.MeleeAttack] * scene.Source.Stats.Scale * scene.Source.Medical!.StatMultiplier(StatsEnumerated.MeleeAttack),
            scene.Victim.Stats![StatsEnumerated.MeleeDefence] * scene.Victim.Stats.Scale * scene.Victim.Medical.StatMultiplier(StatsEnumerated.MeleeDefence));
        Assert.InRange(challenge, .001f, 6);
        scene.Apply();
        var hit = Assert.Single(scene.Observer.Hits);
        Assert.Equal(LArm, hit.Part);
        Assert.True(hit.Damage.Cut > before);
        Assert.Equal(before * challenge * scene.Game.ExpGainMultiplier, scene.View(scene.SourceId).Experience, 4);
        Assert.Equal(LimbState.Stump, part.Limb);
        Assert.Contains(scene.View(scene.VictimId).Limbs, limb => limb.LimbSlot == 0);
        Assert.Equal(GoreIntegrationGap.None, scene.Gameplay.Gaps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rare_real_followthrough_respects_the_other_limbs_own_armour_and_health(bool protectedCounterpart)
    {
        using var patches = new Patches();
        ulong counter = CombatRolls.BlowCounter(0, 1, 1, 0), seed = 0;
        while (Rng.Float(Rng.Hash(seed, Rng.Key(new CharacterId(1, 0)) ^ 0x504149524544UL, Purpose, counter)) >= .05f) seed++;
        var protector = new ArmourData
        {
            StringId = "right-arm-only", Class = ArmourClass.Heavy, Material = ArmourType.MetalPlate,
            CutDefBonus = 100, Coverage = [new(RightArm.StringId, 100)],
        };
        var victim = new Combatant { Armour = protectedCounterpart ? [protector] : [] }.MakeCold();
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (LeftArm.StringId, LeftArm.Name), GlobalDamageMultiplier = 10 }, seed: seed, victim: victim);
        scene.Gameplay.SetLevel(scene.World, scene.SourceId, 100);
        var other = scene.Victim.Medical!.Parts[RArm]; float otherFlesh = other.Flesh;
        scene.Apply();
        Assert.False(scene.Victim.Medical.Dead);
        Assert.Equal(LimbState.Stump, scene.Victim.Medical.Parts[LArm].Limb);
        Assert.Equal(protectedCounterpart ? LimbState.Original : LimbState.Stump, other.Limb);
        if (protectedCounterpart) Assert.Equal(otherFlesh, other.Flesh);
        Assert.Equal(protectedCounterpart ? 1 : 2, scene.View(scene.VictimId).Limbs.Count);
        Assert.DoesNotContain(scene.Gameplay.CutRequests, request => request.Kind == GoreCutKind.PairedLimbAttempt);
        Assert.Equal(GoreIntegrationGap.None, scene.Gameplay.Gaps);
        Assert.Single(scene.Observer.Hits); // The secondary limb is one swing's native medical consequence, not a second contact/XP event.
    }

    [Fact]
    public void A_real_bolt_keeps_its_thrust_mechanism_and_cannot_decapitate_or_train_brutality()
    {
        using var patches = new Patches();
        var bow = RangedTests.Ranger with { PierceDamageMin0 = 100, PierceDamageMin1 = 100, PierceDamageMax0 = 100, PierceDamageMax1 = 100 };
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (Head.StringId, Head.Name) }, source: RangedTests.Shooter(100, 100, 1, bow: bow));
        // Player characters require their Ranged order; unlike NPCs they default to melee.
        scene.Source.Orders |= CharacterOrders.Ranged;
        scene.Gameplay.SetLevel(scene.World, scene.SourceId, 50);
        var head = scene.Victim.Medical!.Parts[HeadIx]; head.Flesh = -head.MaxHp + 1;
        float xp = BrutalityProgress.FromLevel(50).Experience;
        scene.World.Commands.Enqueue(new AttackOrder([scene.SourceId], scene.VictimId) { Tick = scene.World.Tick });
        for (int i = 0; i < 900 && scene.Observer.Hits.Count == 0; i++) scene.World.RunTick();
        var hit = Assert.Single(scene.Observer.Hits);
        Assert.Equal(CombatHitSource.Projectile, hit.Source);
        Assert.True(scene.Combat.ProjectilesFired > 0);
        Assert.True(Assert.Single(scene.Combat.Log).Ranged);
        Assert.True(scene.Victim.Medical.Dead);
        Assert.False(scene.Victim.Medical.Decapitated);
        Assert.Null(scene.View(scene.VictimId).Head);
        Assert.Equal(xp, scene.View(scene.SourceId).Experience);
        Assert.False(scene.Gameplay.Gaps.HasFlag(GoreIntegrationGap.PreHitContext));
        Assert.False(CombatAdapter.HasActiveContact);
    }

    [Fact]
    public void Executing_a_prisoner_uses_the_head_gate_but_never_awards_training_XP()
    {
        using var patches = new Patches();
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (Head.StringId, Head.Name) });
        scene.Gameplay.SetLevel(scene.World, scene.SourceId, 50);
        scene.Victim.InSomething = Meitou.Simulation.Locks.HeldState.Imprisoned;
        var head = scene.Victim.Medical!.Parts[HeadIx]; head.Flesh = -head.MaxHp + 1;
        Assert.True(scene.Combat.Execute(scene.World, scene.SourceId.Slot, scene.VictimId.Slot, 1));
        scene.Gameplay.SlowWorld(scene.World);
        Assert.Equal(BrutalityProgress.FromLevel(50).Experience, scene.View(scene.SourceId).Experience);
        Assert.NotNull(scene.View(scene.VictimId).Head);
        Assert.True(scene.Victim.Medical.Decapitated);
        Assert.Empty(scene.Observer.Hits); // API 1 does not promise general Execute observation.
        Assert.Equal(GoreIntegrationGap.None, scene.Gameplay.Gaps);
    }

    static void ThrowDuringMedical()
    {
        if (CombatAdapter.HasActiveContact) throw new InvalidOperationException("fixture medical failure");
    }

    [Fact]
    public void A_native_exception_restores_the_thread_scope_and_does_not_capture_unrelated_medical_damage()
    {
        using var patches = new Patches();
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (LeftArm.StringId, LeftArm.Name) });
        patches.Harmony.Patch(AccessTools.Method(typeof(MedicalState), nameof(MedicalState.ApplyHit)),
            prefix: new HarmonyMethod(typeof(BrutalityCombatAdapterTests), nameof(ThrowDuringMedical)));
        Assert.Throws<InvalidOperationException>(() => scene.Apply());
        Assert.False(CombatAdapter.HasActiveContact);
        scene.Victim.Medical!.ApplyHit(RArm, new(1, 0, 0), Context());
        scene.Gameplay.SlowWorld(scene.World);
        Assert.Equal(0, scene.View(scene.SourceId).Experience);
        Assert.Empty(scene.Observer.Hits);
        Assert.Empty(scene.Gameplay.CutRequests);
    }
    [Theory]
    [InlineData(false, "1", false)]
    [InlineData(true, null, false)]
    [InlineData(true, "0", false)]
    [InlineData(true, "true", false)]
    [InlineData(true, "1", true)]
    public void Actual_contact_tracing_requires_both_developer_tools_and_the_explicit_environment_flag(bool developerTools, string? request, bool expected) =>
        Assert.Equal(expected, DeveloperCombatDiagnostics.EnabledBy(developerTools, request));

    [Fact]
    public void Actual_contact_trace_joins_native_medical_events_with_delivered_damage_and_respects_its_world_limit()
    {
        using var patches = new Patches();
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (LeftArm.StringId, LeftArm.Name), GlobalDamageMultiplier = .1f });
        var lines = new List<string>();
        DeveloperCombatDiagnostics.EnableForTest(scene.World, lines.Add, limit: 2);
        scene.Apply(); scene.Apply(); scene.Apply();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line =>
        {
            Assert.Contains("Brutality real hit:", line);
            Assert.Contains("actualCut=", line); Assert.Contains("usefulBefore=", line);
            Assert.Contains("brutality=", line); Assert.Contains("torsoAccepted=False", line);
            Assert.DoesNotContain("nativeEvents=-1", line);
        });
        Assert.Contains("brutality=0.000", lines[0]);
        Assert.Equal(3, scene.Observer.Hits.Count);
        Assert.False(DeveloperCombatDiagnostics.Active(scene.World));
    }

    static CharacterCold CanonicalTorsoVictim(string partId)
    {
        var victim = new Combatant().MakeCold();
        victim.Race = new RaceData { Anatomy = Human.Anatomy.Select(entry => entry.Part == Chest
            ? entry with { Part = Chest with { StringId = partId } } : entry).ToArray() };
        victim.Medical = Healthy(victim.Race);
        return victim;
    }

    [Theory]
    [InlineData(0, ArmourData.ChestPartId, false)]
    [InlineData(59.9f, ArmourData.ChestPartId, false)]
    [InlineData(100, ArmourData.ChestPartId, true)]
    [InlineData(100, GorePartIds.Stomach, true)]
    public void Real_committed_chest_and_stomach_blows_bisect_only_after_the_skill_gate_with_actual_lethal_delivered_cut(float level, string partId, bool expected)
    {
        using var patches = new Patches();
        ulong counter = CombatRolls.BlowCounter(0, 1, 1, 0), seed = 0;
        while (Rng.Float(Rng.Hash(seed, Rng.Key(new CharacterId(1, 0)) ^ 0x424953454354UL, Purpose, counter)) >= .95f) seed++;
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (partId, Chest.Name), GlobalDamageMultiplier = 10 },
            seed: seed, victim: CanonicalTorsoVictim(partId));
        Assert.True(scene.Gameplay.SetLevel(scene.World, scene.SourceId, level));
        var lines = new List<string>(); DeveloperCombatDiagnostics.EnableForTest(scene.World, lines.Add);
        scene.Apply();
        var hit = Assert.Single(scene.Observer.Hits);
        var part = scene.Victim.Medical!.Parts[hit.Part];
        Assert.Equal(partId, part.Template.StringId); Assert.True(scene.Victim.Medical.Dead);
        Assert.True(hit.Damage.Cut >= MathF.Max(70, part.MaxHp * .75f));
        Assert.Equal(expected, scene.View(scene.VictimId).Torso is not null);
        Assert.Equal(expected, scene.Gameplay.CutRequests.Any(request => request.Kind == GoreCutKind.Torso));
        var line = Assert.Single(lines);
        Assert.Contains(expected ? "torsoGate=eligible" : "torsoGate=brutality-locked", line);
        Assert.Contains($"torsoAccepted={expected}", line);
        Assert.DoesNotContain("nativeEvents=-1", line);
        Assert.Equal(GoreIntegrationGap.None, scene.Gameplay.Gaps);
    }

    [Fact]
    public void A_real_lethal_low_cut_does_not_bypass_the_single_blow_threshold_even_at_mastery()
    {
        using var patches = new Patches();
        using var scene = new Scene(BodyOptions.Default with { ForcedHitPart = (ArmourData.ChestPartId, Chest.Name), GlobalDamageMultiplier = .1f },
            victim: CanonicalTorsoVictim(ArmourData.ChestPartId));
        Assert.True(scene.Gameplay.SetLevel(scene.World, scene.SourceId, 100));
        var chest = scene.Victim.Medical!.Parts.Single(part => part.Template.StringId == ArmourData.ChestPartId);
        chest.Flesh = -chest.MaxHp + .001f;
        var lines = new List<string>(); DeveloperCombatDiagnostics.EnableForTest(scene.World, lines.Add);
        scene.Apply();
        var hit = Assert.Single(scene.Observer.Hits);
        Assert.True(scene.Victim.Medical.Dead); Assert.InRange(hit.Damage.Cut, .001f, 69.99f);
        Assert.Null(scene.View(scene.VictimId).Torso);
        Assert.Contains("torsoGate=cut-below-threshold", Assert.Single(lines));
    }

}
