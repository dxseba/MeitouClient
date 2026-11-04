using System.Numerics;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalitySandboxProfileTests
{
    sealed class Scene : IDisposable
    {
        public readonly PopulationData Data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true));
        public PopulationSystem Population { get; }
        public BrutalityGameplaySystem Gameplay { get; }
        public SimWorld World { get; }
        public Scene()
        {
            Population = new(Data);
            Gameplay = new(null, Data.Bodies.Constants, Data.BodyOptions, (RngPurpose)12345);
            World = new(new WorldSettings { PublishSnapshots = false, Threads = 1 },
                new OpenGroundWalkability(SyntheticTown.Ground), [Population, Gameplay]);
        }
        public CharacterId Spawn(bool player, int variant = 0, int? faction = null) =>
            Population.SpawnSandbox(World, "2-t", new Vector2(1000, 2000), player, variant: variant, faction: faction);
        public void Dispose() => World.Dispose();
    }

    [Theory]
    [InlineData(false, "100", false, 0)]
    [InlineData(true, null, false, 0)]
    [InlineData(true, "", false, 0)]
    [InlineData(true, "NaN", false, 0)]
    [InlineData(true, "Infinity", false, 0)]
    [InlineData(true, "wrong", false, 0)]
    [InlineData(true, "-10", true, 0)]
    [InlineData(true, "65.5", true, 65.5)]
    [InlineData(true, "1000", true, 100)]
    public void Sandbox_profile_is_explicit_developer_only_finite_and_clamped(bool developerTools, string? requested, bool enabled, float expected)
    {
        Assert.Equal(enabled, SandboxProfileAdapter.TryLevel(developerTools, requested, out float actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("100", 100)]
    [InlineData("1000", 100)]
    [InlineData("-10", 0)]
    public void Actual_native_primary_sandbox_spawn_gets_stored_stats_brutality_and_full_adjusted_blood_only_when_opted_in(string requested, float expected)
    {
        var harmony = new Harmony("brutality.sandbox.test." + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(SandboxProfileAdapter.Install(harmony, true, requested));
            using var scene = new Scene();
            var id = scene.Spawn(true); var hero = scene.World.Characters.Cold(id.Slot)!;
            for (int i = 1; i < StatsInfo.Count; i++)
                if (StatsInfo.StorageOf((StatsEnumerated)i) == (StatsEnumerated)i) Assert.Equal(expected, hero.Stats![(StatsEnumerated)i]);
            Assert.Equal(expected, scene.Gameplay.LevelOf(scene.World, id));
            Assert.Equal(MedicalState.BloodCapacity(hero.Race!, expected, hero.Medical!.HasStatsObject, hero.Medical.ScaleK, hero.Medical.ScaleZ), hero.Medical.Blood);
            var npc = scene.Spawn(false); var crowd = scene.Spawn(true, variant: 1);
            Assert.NotEqual(expected, scene.World.Characters.Cold(npc.Slot)!.Stats![StatsEnumerated.Strength]);
            Assert.NotEqual(expected, scene.World.Characters.Cold(crowd.Slot)!.Stats![StatsEnumerated.Strength]);
            Assert.Equal(0, scene.Gameplay.LevelOf(scene.World, crowd));
            // Ordinary new-game creation is a different native method and must retain player progression zero.
            using var ordinary = new Scene();
            var squad = ordinary.Population.StartPlayer(ordinary.World, NewGameStart.From(ordinary.Data.Db.Find("40-t")!));
            Assert.Equal(0, ordinary.Gameplay.LevelOf(ordinary.World, squad.Leader));
            Assert.NotEqual(100f, ordinary.World.Characters.Cold(squad.Leader.Slot)!.Stats![StatsEnumerated.Strength]);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [Fact]
    public void A_disabled_developer_profile_does_not_patch_native_spawn_or_change_player_progression()
    {
        var harmony = new Harmony("brutality.sandbox.disabled." + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(SandboxProfileAdapter.Install(harmony, false, "100"));
            using var scene = new Scene(); var id = scene.Spawn(true);
            Assert.NotEqual(100f, scene.World.Characters.Cold(id.Slot)!.Stats![StatsEnumerated.Strength]);
            Assert.Equal(0, scene.Gameplay.LevelOf(scene.World, id));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
