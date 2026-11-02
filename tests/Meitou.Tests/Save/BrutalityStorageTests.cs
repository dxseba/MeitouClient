using System.Globalization;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Saving;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Save;

public class BrutalityStorageTests
{
    [Fact]
    public void Native_extension_round_trip_keeps_progress_and_unrelated_fields_without_touching_original_stats()
    {
        var state = new FcsRecord { StringId = "brutality-owner", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        state.Strings["unrelated mod field"] = "keep";
        state.Floats["age"] = 1.25f;
        Assert.Equal(0, BrutalitySave.Read(state).Level);
        var skill = new BrutalityProgress();
        Assert.Equal(315, skill.Award(315));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            BrutalitySave.Write(state, skill);
            var file = new FcsFile(); file.Records.Add(state);
            using var bytes = new MemoryStream();
            FcsWriter.Write(file, bytes); bytes.Position = 0;
            var read = Assert.Single(FcsReader.Read(bytes).Records);
            var restored = BrutalitySave.Read(read);
            Assert.Equal(315, restored.Experience);
            Assert.Equal(MathF.Sqrt(31.5f), restored.Level);
            Assert.Equal(45, restored.ExperienceToNextLevel);
            Assert.Equal(65f / 110, restored.Progress01);
            Assert.Equal("keep", read.Strings["unrelated mod field"]);
            Assert.Equal(1.25f, read.Floats["age"]);
            Assert.DoesNotContain(read.Floats.Keys, key => StatsInfo.FromFieldName(key) != StatsEnumerated.None);
            BrutalitySave.Write(read, new());
            Assert.DoesNotContain(BrutalitySave.ExperienceKey, read.Floats.Keys);
            Assert.Equal("keep", read.Strings["unrelated mod field"]);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(float.NaN, 0)] [InlineData(float.PositiveInfinity, 0)] [InlineData(float.NegativeInfinity, 0)]
    [InlineData(-50, 0)] [InlineData(150000, 100000)]
    public void Invalid_saved_experience_is_sanitized_and_never_poisoned_by_an_invalid_award(float saved, float expected)
    {
        var state = new FcsRecord(); state.Floats[BrutalitySave.ExperienceKey] = saved;
        var skill = BrutalitySave.Read(state);
        Assert.Equal(expected, skill.Experience);
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -100f, 0f })
            Assert.Equal(0, skill.Award(invalid));
        Assert.Equal(expected, skill.Experience);
        Assert.InRange(skill.Level, 0, 100);
        Assert.InRange(skill.Progress01, 0, 1);
        skill.Award(float.MaxValue);
        Assert.Equal((100f, 1f, 0f), (skill.Level, skill.Progress01, skill.ExperienceToNextLevel));
    }

    [Fact]
    public void Zero_progress_keeps_existing_hash_bytes_and_loaded_and_stored_characters_hash_actual_progress()
    {
        var original = new StateHasher(); original.Add(123);
        var zero = original; new BrutalityProgress().Hash(ref zero);
        Assert.Equal(original.Value, zero.Value);
        using var world = new SimWorld(new WorldSettings(), new OpenGroundWalkability((_, _) => 0));
        var cold = new CharacterCold { Name = "Skill owner" };
        var id = world.Characters.Spawn(new() { Position = new(100, 0, 100) }, cold, 0);
        ulong without = world.StateHash();
        cold.Brutality.Award(100);
        Assert.NotEqual(without, world.StateHash());
        var registry = new PlatoonRegistry();
        var stored = new StoredSquad { Name = "Stored" };
        stored.Members.Add(new(cold, new(100, 0, 100), 0, true));
        registry.Add(new() { Id = 0, Origin = 0, TemplateId = "12-t", Key = 1, Stored = stored });
        var before = new StateHasher(); registry.Hash(ref before);
        cold.Brutality.Award(1);
        var after = new StateHasher(); registry.Hash(ref after);
        Assert.NotEqual(before.Value, after.Value);
        world.Characters.Remove(id);
        var replacement = new CharacterCold();
        world.Characters.Spawn(new(), replacement, 0);
        Assert.Equal(0, replacement.Brutality.Experience);
    }

    static PopulationData Data()
    {
        var db = SyntheticTown.Database(anatomy: true);
        var record = new FcsRecord { StringId = "2-t", RecordType = FcsRecordType.CHARACTER, Name = "Veteran", Flags = 0x11 };
        record.Ints["combat stats"] = 80; record.Ints["stats randomise"] = 0;
        var patch = new FcsFile(); patch.Records.Add(record); db.Apply(patch, "brutality-veteran.mod");
        return SyntheticTown.Data(db);
    }

    static CharacterCold Assemble(PopulationData data, bool player, int? faction = null)
    {
        var cold = new CharacterCold { Name = "New", IsPlayer = player, RecordId = "2-t",
            Faction = faction ?? (player ? data.PlayerFaction : data.FactionIndex("20-t")) };
        var hot = new CharacterHot();
        new CharacterAssembly(data).Attach(new(1, 1), cold, ref hot, data.Db.Find("2-t"), data.Db.Find("1-t"), 1);
        return cold;
    }

    [Fact]
    public void Only_new_npc_veterans_are_seeded_from_combat_while_new_players_and_saved_npcs_start_at_zero()
    {
        var data = Data();
        var veteran = Assemble(data, false);
        Assert.Equal(80, veteran.Stats![StatsEnumerated.MeleeAttack]);
        Assert.Equal(80, veteran.Brutality.Level);
        Assert.Equal(veteran.Brutality.Experience, Assemble(data, false, -1).Brutality.Experience);
        Assert.Equal(0, Assemble(data, true).Brutality.Level);
        Assert.Equal(0, Assemble(data, false, data.PlayerFaction).Brutality.Level);
        var loaded = new CharacterCold { Faction = data.FactionIndex("20-t") };
        new CharacterAssembly(data).AttachSaved(loaded, veteran.Race!, veteran.Stats!, veteran.Medical!, data.Db.Find("2-t"));
        Assert.Equal(0, loaded.Brutality.Level);
    }

    [Fact]
    public void A_player_keeps_exact_skill_and_published_progress_through_native_capture_load()
    {
        using var fixture = new TorsoCarryFixture();
        var player = fixture.Add("Learner", 0);
        fixture.Cold(player).Brutality.Award(315);
        fixture.World.RunTick();
        var shown = Assert.Single(fixture.World.Snapshot.Characters, character => character.Id == player);
        Assert.Equal(MathF.Sqrt(31.5f), shown.BrutalityLevel);
        Assert.Equal(65f / 110, shown.BrutalityProgress);
        var captured = SaveCapture.Capture(fixture.World, fixture.Data, new(1, 12, 0));
        string root = SaveSamples.Temp("brutality-player");
        try
        {
            string folder = Path.Combine(root, "slot"); captured.Write(folder);
            using var restored = new TorsoCarryFixture();
            SaveLoader.Load(restored.World, SaveGame.Load(folder), restored.Data);
            restored.World.RunTick();
            var after = Assert.Single(restored.World.Snapshot.Characters, character => character.Name == "Learner");
            Assert.Equal(shown.BrutalityLevel, after.BrutalityLevel);
            Assert.Equal(shown.BrutalityProgress, after.BrutalityProgress);
            Assert.Equal(315, restored.Cold(after.Id).Brutality.Experience);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void A_saved_npc_keeps_progress_through_unload_native_save_and_respawn_without_veteran_reseeding()
    {
        var data = Data();
        var builder = new SaveBuilder(31);
        builder.AddFaction("204-gamedata.base", "Nameless", true, 1000);
        builder.AddFaction("20-t", "Testfolk", false, 1000);
        var source = builder.AddPlatoon("Testfolk_0", "Testfolk", "20-t", "12-t", new(1000, 300, 2000));
        source.Record.Strings["basetown"] = "30-t";
        source.Record.Bools["is resident"] = true;
        var character = builder.AddCharacter(source, "2-t", "Saved veteran", "20-t", source.Position, Quaternion.Identity, true);
        var veteran = Assemble(data, false);
        veteran.Stats!.WriteSave(character.Stats!.Floats);
        veteran.Medical!.WriteSave(character.Medical!.Floats, character.Medical.Bools, character.Medical.Strings);
        using var world = new SimWorld(new WorldSettings(), new OpenGroundWalkability(SyntheticTown.Ground));
        var loaded = SaveLoader.Load(world, builder.Game, data);
        var saved = Assert.Single(world.Saved!.Platoons);
        var platoon = Assert.IsType<Platoon>(world.Platoons.Find(saved.PlatoonId));
        var built = SavedCharacters.Prepare(saved, data, null, world.Saved);
        var squad = Assert.IsType<Squad>(SavedCharacters.Spawn(world, platoon, saved, built, data, new CharacterAssembly(data), Vector2.Zero, 100));
        var member = Assert.Single(saved.Members);
        var cold = world.Characters.Cold(member.Live.Slot)!;
        Assert.Equal(0, cold.Brutality.Level); // Missing extension on an old NPC never grants veteran unlocks.
        cold.Brutality.Award(1234);
        SavedCharacters.Store(world, saved, platoon.Position);
        Assert.Equal(1234, member.Brutality!.Experience);
        foreach (var id in squad.Members) world.Characters.Remove(id);
        world.Squads.Remove(squad.Id); platoon.SquadId = -1; platoon.State = PlatoonState.Unloaded;
        Assert.Equal(1234, Assert.Single(SavedCharacters.Prepare(saved, data, null, world.Saved)).Parts.Brutality!.Experience);
        var captured = SaveCapture.Capture(world, data, loaded.Clock, loaded);
        var written = Assert.Single(Assert.Single(captured.Platoons, p => p.FactionId == "20-t").Characters);
        Assert.Equal(1234, BrutalitySave.Read(written.GameState).Experience);
        string root = SaveSamples.Temp("brutality-offscreen");
        try
        {
            string folder = Path.Combine(root, "slot"); captured.Write(folder);
            using var restored = new SimWorld(new WorldSettings(), new OpenGroundWalkability(SyntheticTown.Ground));
            SaveLoader.Load(restored, SaveGame.Load(folder), data);
            var again = Assert.Single(restored.Saved!.Platoons);
            ulong hash = restored.StateHash();
            Assert.Single(again.Members).Source.GameState!.Floats[BrutalitySave.ExperienceKey] = 1235;
            Assert.NotEqual(hash, restored.StateHash());
            Assert.Single(again.Members).Source.GameState!.Floats[BrutalitySave.ExperienceKey] = 1234;
            var next = SavedCharacters.Prepare(again, data, null, restored.Saved);
            var loadedSquad = Assert.IsType<Squad>(SavedCharacters.Spawn(restored, restored.Platoons.Find(again.PlatoonId)!, again, next,
                data, new CharacterAssembly(data), Vector2.Zero, 100));
            Assert.Equal(1234, restored.Characters.Cold(loadedSquad.Leader.Slot)!.Brutality.Experience);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
