using System.Globalization;
using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Saving;
using Meitou.Tests.Physics;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Save;

public class LimbCutStorageTests
{
    static readonly LimbCutState[] Cuts = [new(2, .28423455f, new(.375f, -.2654321f)), new(3, .7382156f, new(-.6123456f, .1876543f))];

    [Fact]
    public void Native_extension_bytes_keep_both_exact_cuts_under_a_foreign_culture_without_changing_other_fields()
    {
        var state = new FcsRecord { StringId = "limb-owner", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        state.Strings["unrelated mod field"] = "keep";
        state.Floats["age"] = 1.25f;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            LimbCutSave.Write(state, Cuts.Reverse().ToArray());
            var file = new FcsFile();
            file.Records.Add(state);
            using var bytes = new MemoryStream();
            FcsWriter.Write(file, bytes);
            bytes.Position = 0;
            var read = Assert.Single(FcsReader.Read(bytes).Records);
            var restored = LimbCutSave.Read(read);
            AssertGeometry(Cuts, restored);
            Assert.Equal("keep", read.Strings["unrelated mod field"]);
            Assert.Equal(1.25f, read.Floats["age"]);
            LimbCutSave.Write(read, []);
            Assert.DoesNotContain(LimbCutSave.Key, read.Strings.Keys);
            Assert.Equal("keep", read.Strings["unrelated mod field"]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("")]
    [InlineData("L2 2 .5 .2 .3")]
    [InlineData("L1 2 .5 .2")]
    [InlineData("L1 2 .5 .2 .3 tail")]
    [InlineData("L1 2 .5 .2 .3 2 .7 .4 .1")]
    [InlineData("L1 4 .5 .2 .3")]
    [InlineData("L1 2 .079 .2 .3")]
    [InlineData("L1 2 .921 .2 .3")]
    [InlineData("L1 2 NaN .2 .3")]
    [InlineData("L1 2 .5 Infinity .3")]
    [InlineData("L1 2 .5 1 1")]
    [InlineData("L1 0 .5 0 0 1 .5 0 0 2 .5 0 0 3 .5 0 0 0 .6 0 0")]
    public void Malformed_or_duplicate_rows_are_rejected_as_a_whole_instead_of_feeding_partial_geometry_to_physics(string text)
    {
        var state = new FcsRecord();
        state.Strings[LimbCutSave.Key] = text;
        Assert.Empty(LimbCutSave.Read(state));
    }

    [Fact]
    public void Legacy_states_stay_empty_and_writing_bad_runtime_rows_cannot_leave_an_old_cut_or_duplicate_slots()
    {
        Assert.Empty(LimbCutSave.Read(null));
        var state = new FcsRecord();
        Assert.Empty(LimbCutSave.Read(state));
        LimbCutSave.Write(state, [Cuts[1], new(2, float.NaN, default), Cuts[0], Cuts[0] with { Fraction = .5f }]);
        AssertGeometry(Cuts, LimbCutSave.Read(state));
        LimbCutSave.Write(state, [new(2, .5f, new(float.NaN, 0))]);
        Assert.DoesNotContain(LimbCutSave.Key, state.Strings.Keys);
    }

    [Fact]
    public void A_player_keeps_both_missing_limbs_and_the_exact_published_cut_geometry_through_native_capture_and_load()
    {
        using var fixture = new TorsoCarryFixture();
        var player = fixture.Add("Amputee", 0);
        var cold = fixture.Cold(player);
        SeverBothLegs(cold);
        cold.LimbSeverances = Cuts;
        fixture.World.RunTick();
        var shown = Assert.Single(fixture.World.Snapshot.Characters, character => character.Id == player);
        AssertGeometry(Cuts, shown.LimbSeverances);
        int limbBits = cold.Medical!.LimbBits;
        var capture = SaveCapture.Capture(fixture.World, fixture.Data, new(1, 12, 0));
        var written = Assert.Single(capture.Platoons.SelectMany(platoon => platoon.Characters), character => character.Name == "Amputee");
        AssertGeometry(Cuts, LimbCutSave.Read(written.GameState));
        string root = SaveSamples.Temp("limb-player");
        try
        {
            string folder = Path.Combine(root, "slot");
            capture.Write(folder);
            using var restored = new TorsoCarryFixture();
            SaveLoader.Load(restored.World, SaveGame.Load(folder), restored.Data);
            restored.World.RunTick();
            var after = Assert.Single(restored.World.Snapshot.Characters, character => character.Name == "Amputee");
            var loaded = restored.Cold(after.Id);
            Assert.Equal(limbBits, loaded.Medical!.LimbBits);
            Assert.All(loaded.Medical.Parts.Where(part => part.Template.LimbSlot is 2 or 3), part => Assert.Equal(LimbState.Stump, part.Limb));
            AssertGeometry(Cuts, loaded.LimbSeverances);
            AssertGeometry(shown.LimbSeverances, after.LimbSeverances);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void A_saved_npc_keeps_both_stumps_and_exact_cuts_through_unload_prepare_native_capture_reload_and_spawn()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true));
        var builder = new SaveBuilder(31);
        builder.AddFaction("204-gamedata.base", "Nameless", true, 1000);
        builder.AddFaction("20-t", "Testfolk", false, 1000);
        var source = builder.AddPlatoon("Testfolk_0", "Testfolk", "20-t", "12-t", new(1000, 300, 2000));
        source.Record.Strings["basetown"] = "30-t";
        source.Record.Bools["is resident"] = true;
        var character = builder.AddCharacter(source, "2-t", "Saved amputee", "20-t", source.Position, Quaternion.Identity, true);
        var race = data.Bodies.Race(data.Db.Find("1-t")!);
        var medical = MedicalState.Create(race, 50);
        medical.WriteSave(character.Medical!.Floats, character.Medical.Bools, character.Medical.Strings);
        using var world = new SimWorld(new WorldSettings(), new OpenGroundWalkability(SyntheticTown.Ground));
        var loaded = SaveLoader.Load(world, builder.Game, data);
        var saved = Assert.Single(world.Saved!.Platoons);
        var platoon = Assert.IsType<Platoon>(world.Platoons.Find(saved.PlatoonId));
        var prepared = SavedCharacters.Prepare(saved, data, null, world.Saved);
        var squad = Assert.IsType<Squad>(SavedCharacters.Spawn(world, platoon, saved, prepared, data, new CharacterAssembly(data), Vector2.Zero, 100));
        var member = Assert.Single(saved.Members);
        var cold = world.Characters.Cold(member.Live.Slot)!;
        Assert.Empty(cold.LimbSeverances);
        SeverBothLegs(cold);
        cold.LimbSeverances = Cuts;
        int limbBits = cold.Medical!.LimbBits;
        SavedCharacters.Store(world, saved, platoon.Position);
        AssertGeometry(Cuts, member.LimbSeverances!);
        foreach (var id in squad.Members) world.Characters.Remove(id);
        world.Squads.Remove(squad.Id);
        platoon.SquadId = -1;
        platoon.State = PlatoonState.Unloaded;
        var stored = Assert.Single(SavedCharacters.Prepare(saved, data, null, world.Saved)).Parts;
        AssertGeometry(Cuts, stored.LimbSeverances!);
        Assert.Equal(limbBits, stored.Medical!.LimbBits);
        var captured = SaveCapture.Capture(world, data, loaded.Clock, loaded);
        var written = Assert.Single(Assert.Single(captured.Platoons, savedPlatoon => savedPlatoon.FactionId == "20-t").Characters);
        AssertGeometry(Cuts, LimbCutSave.Read(written.GameState));
        string root = SaveSamples.Temp("limb-offscreen");
        try
        {
            string folder = Path.Combine(root, "slot");
            captured.Write(folder);
            using var restored = new SimWorld(new WorldSettings(), new OpenGroundWalkability(SyntheticTown.Ground));
            SaveLoader.Load(restored, SaveGame.Load(folder), data);
            var again = Assert.Single(restored.Saved!.Platoons);
            var next = SavedCharacters.Prepare(again, data, null, restored.Saved);
            AssertGeometry(Cuts, Assert.Single(next).Parts.LimbSeverances!);
            var respawned = Assert.IsType<Squad>(SavedCharacters.Spawn(restored, restored.Platoons.Find(again.PlatoonId)!, again, next,
                data, new CharacterAssembly(data), Vector2.Zero, 100));
            var after = restored.Characters.Cold(respawned.Leader.Slot)!;
            Assert.Equal(limbBits, after.Medical!.LimbBits);
            Assert.All(after.Medical.Parts.Where(part => part.Template.LimbSlot is 2 or 3), part => Assert.Equal(LimbState.Stump, part.Limb));
            AssertGeometry(Cuts, after.LimbSeverances);
            restored.RunTick();
            AssertGeometry(Cuts, Assert.Single(restored.Snapshot.Characters, shown => shown.Id == respawned.Leader).LimbSeverances);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static void SeverBothLegs(CharacterCold cold)
    {
        var context = new MedicalContext(GameConstants.Default, BodyOptions.Default, cold.Race!) { Strength = 50, Toughness = 50, Seed = 31 };
        for (int part = 0; part < cold.Medical!.Parts.Count; part++)
            if (cold.Medical.Parts[part].Template.LimbSlot is 2 or 3) cold.Medical.Sever(part, context);
        Assert.Equal(2, cold.Medical.Parts.Count(part => part.Limb == LimbState.Stump));
    }

    static void AssertGeometry(IReadOnlyList<LimbCutState> expected, IReadOnlyList<LimbCutState> actual)
    {
        Assert.Equal(expected, actual);
        var rig = LimbSeveranceGeometryTests.HumanRig().Rig;
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.True(LimbCutGeometry.TryPlane(rig, expected[index], out var before));
            Assert.True(LimbCutGeometry.TryPlane(rig, actual[index], out var after));
            Assert.Equal(before, after);
        }
    }
}
