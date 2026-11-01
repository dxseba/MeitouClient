using System.Globalization;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Save;

public class TorsoFragmentCarrySaveTests
{
    static SaveCharacter Named(SaveGame save, string name) => save.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == name);

    [Fact]
    public void Native_save_load_restores_two_different_carriers_without_duplicating_the_owner_or_inventory()
    {
        using var rig = new TorsoCarryFixture();
        var negative = rig.Add("Negative carrier", 0);
        var positive = rig.Add("Positive carrier", 100);
        var body = rig.Cut();
        Assert.True(rig.Cold(body).Inventory!.Add(new ItemInstance { Record = "70-t", Name = "Ration", Type = FcsRecordType.ITEM,
            Quantity = 1, StackLimit = 1, Width = 1, Height = 1, UnitWeight = 1 }));
        rig.PickUp(negative, body, 0);
        rig.PickUp(positive, body, 1);
        var captured = SaveCapture.Capture(rig.World, rig.Data, new(1, 13, 0));
        var originalOwner = Named(captured, "Body");
        Assert.Equal(originalOwner.Handle, Hand.Read(Named(captured, "Negative carrier").GameState!, "carrying"));
        Assert.Equal(originalOwner.Handle, Hand.Read(Named(captured, "Positive carrier").GameState!, "carrying"));
        Assert.Equal(1, TorsoCarrySave.Side(Named(captured, "Positive carrier").GameState));

        string folder = SaveSamples.Temp("torso-two-carries");
        try
        {
            captured.Write(folder);
            using var restored = new TorsoCarryFixture();
            var loaded = SaveLoader.Load(restored.World, SaveGame.Load(folder), restored.Data);
            restored.World.RunTicks(25);
            var n = restored.Named("Negative carrier");
            var p = restored.Named("Positive carrier");
            var b = restored.Named("Body");
            Assert.Equal(b, restored.Cold(n).Carrying);
            Assert.Equal(b, restored.Cold(p).Carrying);
            Assert.Equal(0, restored.Cold(n).CarryingTorsoSide);
            Assert.Equal(1, restored.Cold(p).CarryingTorsoSide);
            Assert.Equal(n, restored.Cold(b).CarriedBy);
            Assert.Equal(p, restored.Cold(b).TorsoPositiveCarriedBy);
            Assert.Equal(rig.Cold(body).TorsoBisection, restored.Cold(b).TorsoBisection);
            Assert.Equal(1, Assert.Single(restored.Cold(b).Inventory!.All()).Quantity);
            Assert.False(restored.Cold(b).IsPlayer);

            // A second capture keeps the same owner once; a corpse restored outside its original zone is not exported twice.
            var again = SaveCapture.Capture(restored.World, restored.Data, loaded.Clock, loaded);
            var ownerAgain = Named(again, "Body");
            Assert.Equal(1, SaveInventory.Read(ownerAgain.Platoon.File!, ownerAgain.Inventory!).Where(i => i.BaseId == "70-t").Sum(i => i.Quantity));
            Assert.Single(restored.World.Saved!.Platoons.SelectMany(s => s.Members), m => m.Live == b);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_carried_positive_half_restores_its_owner_even_when_the_negative_half_is_far_from_the_player()
    {
        using var rig = new TorsoCarryFixture();
        var positive = rig.Add("Positive carrier", 100);
        var body = rig.Cut(negativeX: 10000, positiveX: 100);
        rig.PickUp(positive, body, 1);
        var captured = SaveCapture.Capture(rig.World, rig.Data, new(1, 13, 0));
        string folder = SaveSamples.Temp("torso-positive-remote-owner");
        try
        {
            captured.Write(folder);
            using var restored = new TorsoCarryFixture();
            SaveLoader.Load(restored.World, SaveGame.Load(folder), restored.Data);
            restored.World.RunTicks(25);
            var p = restored.Named("Positive carrier");
            var b = restored.Named("Body");
            Assert.Equal(b, restored.Cold(p).Carrying);
            Assert.Equal(p, restored.Cold(b).TorsoPositiveCarriedBy);
            Assert.True(restored.Cold(b).CarriedBy.IsNone);
            Assert.InRange(restored.Hot(b).Position.X, 9999, 10001);
            Assert.InRange(restored.Cold(b).TorsoPositivePosition!.Value.X, 90, 110);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Fallback_cut_and_positive_placement_survive_native_bytes_without_a_physics_pose()
    {
        var cold = new CharacterCold { TorsoBisection = new(new(1, 0, 0, -0.25f), true), TorsoPositivePosition = new(9000.25f, 301.5f, -40.75f), TorsoPositiveYaw = 0.8f,
            Carrying = new(8, 4), CarryingTorsoSide = 1, TorsoPositiveReleaseAt = new(8995, 300, -40) };
        var state = new FcsRecord { StringId = "torso-carry", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            TorsoCarrySave.Write(state, cold);
            var file = new FcsFile();
            file.Records.Add(state);
            using var bytes = new MemoryStream();
            FcsWriter.Write(file, bytes);
            bytes.Position = 0;
            var saved = Assert.Single(FcsReader.Read(bytes).Records);
            var restored = new CharacterCold();
            TorsoCarrySave.Read(saved, restored);
            Assert.Equal(1, TorsoCarrySave.Side(saved));
            Assert.Equal(cold.TorsoBisection, restored.TorsoBisection);
            Assert.Equal(cold.TorsoPositivePosition, restored.TorsoPositivePosition);
            Assert.Equal(cold.TorsoPositiveYaw, restored.TorsoPositiveYaw);
            Assert.Equal(cold.TorsoPositiveReleaseAt, restored.TorsoPositiveReleaseAt);
            Assert.DoesNotContain(RagdollSave.Key, saved.Strings.Keys);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Missing_legacy_keys_default_to_the_original_body_and_nonfinite_fragment_placement_is_ignored()
    {
        var state = new FcsRecord();
        Assert.Equal(0, TorsoCarrySave.Side(state));
        var cold = new CharacterCold();
        TorsoCarrySave.Read(state, cold);
        Assert.Null(cold.TorsoBisection);
        Assert.Null(cold.TorsoPositivePosition);
        state.Ints[TorsoCarrySave.SideKey] = 99;
        state.Strings[TorsoCarrySave.CutKey] = "0 1 0 -12 0";
        state.Strings[TorsoCarrySave.PositiveKey] = "NaN 300 0 0";
        TorsoCarrySave.Read(state, cold);
        Assert.Equal(0, TorsoCarrySave.Side(state));
        Assert.NotNull(cold.TorsoBisection);
        Assert.Null(cold.TorsoPositivePosition);
    }
}
