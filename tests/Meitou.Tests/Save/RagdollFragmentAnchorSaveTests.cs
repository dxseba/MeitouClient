using System.Globalization;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Save;

public class RagdollFragmentAnchorSaveTests
{
    static SavedRagdoll Body(float x) => new(true, 3.5f,
    [
        new(new(x, 4, 8), Quaternion.CreateFromYawPitchRoll(0.3f, -0.2f, 0.1f)),
        new(new(x + 2, 6, 9), Quaternion.CreateFromYawPitchRoll(-0.4f, 0.1f, 0.2f)),
    ]);

    [Fact]
    public void Retained_carried_child_locals_survive_native_bytes_independently_for_both_halves()
    {
        var positive = Body(100);
        var negative = Body(900);
        var cut = new TorsoBisectionState(new(0, 1, 0, -12));
        var torso = new SavedTorsoRagdoll(cut, positive, negative);
        var saved = negative with { Torso = torso };
        var state = new FcsRecord { StringId = "fragment-anchors", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        RagdollSave.Write(state, saved);
        string originalPhysicalRow = state.Strings[RagdollSave.Key];
        var legacy = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(RagdollSave.Read(state)).Torso);
        Assert.Null(legacy.PositiveAnchors);
        Assert.Null(legacy.NegativeAnchors);
        var positiveLocals = new SavedFragmentAnchors([0, 1, 1],
        [
            new(Vector3.Zero, Quaternion.Identity),
            new(new(0.1f, 0.2f, -0.3f), Quaternion.Identity),
            new(new(1.25f, -2.75f, 0.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.8f)),
        ]);
        var negativeLocals = new SavedFragmentAnchors([0, 0, 1],
        [
            new(Vector3.Zero, Quaternion.Identity),
            new(new(-2, 0.25f, 1.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.7f)),
            new(new(0.4f, -0.2f, 0.6f), Quaternion.Identity),
        ]);
        saved = saved with { Torso = torso with { PositiveAnchors = positiveLocals, NegativeAnchors = negativeLocals } };
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            RagdollSave.Write(state, saved);
            Assert.Equal(originalPhysicalRow, state.Strings[RagdollSave.Key]);
            var file = new FcsFile();
            file.Records.Add(state);
            using var stream = new MemoryStream();
            FcsWriter.Write(file, stream);
            stream.Position = 0;
            var native = Assert.Single(FcsReader.Read(stream).Records);
            var restored = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(RagdollSave.Read(native)).Torso);
            Check(positiveLocals, positive, Assert.IsType<SavedFragmentAnchors>(restored.PositiveAnchors), restored.Positive);
            Check(negativeLocals, negative, Assert.IsType<SavedFragmentAnchors>(restored.NegativeAnchors), restored.Negative);
            RagdollSave.Write(native, negative);
            Assert.DoesNotContain(RagdollSave.TorsoAnchorsKey, native.Strings.Keys);
            RagdollSave.Write(state, null);
            Assert.DoesNotContain(RagdollSave.Key, state.Strings.Keys);
            Assert.DoesNotContain(RagdollSave.TorsoAnchorsKey, state.Strings.Keys);
        }
        finally { CultureInfo.CurrentCulture = culture; }

        static void Check(SavedFragmentAnchors before, SavedRagdoll beforeActors, SavedFragmentAnchors after, SavedRagdoll afterActors)
        {
            Assert.Equal(before.Anchors, after.Anchors);
            Assert.Equal(before.Offsets.Length, after.Offsets.Length);
            for (int bone = 0; bone < before.Anchors.Length; bone++)
            {
                // Unphysical fingertips/clavicles must keep their rendered location after the physical actor poses are loaded.
                var oldWorld = before.Offsets[bone].Then(beforeActors.Pose[before.Anchors[bone]]);
                var newWorld = after.Offsets[bone].Then(afterActors.Pose[after.Anchors[bone]]);
                Assert.InRange(Vector3.Distance(oldWorld.Position, newWorld.Position), 0, 0.0001f);
                Assert.True(MathF.Abs(Quaternion.Dot(oldWorld.Rotation, newWorld.Rotation)) > 0.99999f);
            }
        }
    }

    [Theory]
    [InlineData("1 2 0 0 0 0 0 0 1")]
    [InlineData("1 0 NaN 0 0 0 0 0 1")]
    [InlineData("1 0 0 0 0 0 0 0 0")]
    [InlineData("2 0 0 0 0 0 0 0 1")]
    public void Invalid_optional_side_locals_do_not_discard_valid_physics_or_the_other_sides_locals(string invalid)
    {
        var positive = Body(100);
        var negative = Body(900);
        var state = new FcsRecord();
        var cut = new TorsoBisectionState(new(0, 1, 0, -12));
        RagdollSave.Write(state, negative with { Torso = new(cut, positive, negative) });
        state.Strings[RagdollSave.TorsoAnchorsKey] = "A1 | " + invalid + " | 1 1 0 0 0 0 0 0 1";
        var restored = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(RagdollSave.Read(state)).Torso);
        Assert.Equal(cut, restored.Cut);
        Assert.Equal(2, restored.Positive.Pose.Length);
        Assert.Equal(2, restored.Negative.Pose.Length);
        Assert.Null(restored.PositiveAnchors);
        Assert.Equal(1, Assert.Single(Assert.IsType<SavedFragmentAnchors>(restored.NegativeAnchors).Anchors));
    }
}
