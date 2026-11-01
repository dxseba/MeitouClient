using System.Globalization;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Save;

public class RagdollFragmentSaveTests
{
    [Theory]
    [InlineData(0, 1, 0, false)]
    [InlineData(1, 0, 0, true)]
    [InlineData(-0.6f, 0.8f, 0, true)]
    public void Native_bytes_preserve_both_fragment_poses_and_the_exact_cut(float x, float y, float z, bool fullBody)
    {
        var cut = new TorsoBisectionState(new(x, y, z, -4), fullBody);
        var positive = new SavedRagdoll(false, 1.25f, [At(11, 12, 13), At(14, 15, 16)]);
        var negative = new SavedRagdoll(true, 4.5f, [At(-1, 2, 3)]);
        var saved = negative with { Torso = new(cut, positive, negative) };
        var record = new FcsRecord { StringId = "fragment-state", Name = "Fragment state", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            RagdollSave.Write(record, saved);
            var file = new FcsFile();
            file.Records.Add(record);
            using var bytes = new MemoryStream();
            FcsWriter.Write(file, bytes);
            bytes.Position = 0;
            var restored = Assert.IsType<SavedRagdoll>(RagdollSave.Read(Assert.Single(FcsReader.Read(bytes).Records)));
            var halves = Assert.IsType<SavedTorsoRagdoll>(restored.Torso);
            Assert.Equal(cut, halves.Cut);
            Assert.False(halves.Positive.Frozen);
            Assert.True(halves.Negative.Frozen);
            Assert.Equal(positive.Age, halves.Positive.Age);
            Assert.Equal(negative.Age, halves.Negative.Age);
            Assert.Equal(positive.Pose, halves.Positive.Pose);
            Assert.Equal(negative.Pose, halves.Negative.Pose);
            Assert.Equal(negative.Pose, restored.Pose);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Fact]
    public void Legacy_whole_body_ragdolls_and_absent_keys_keep_their_old_representation()
    {
        var record = new FcsRecord();
        Assert.Null(RagdollSave.Read(record));
        var original = new SavedRagdoll(true, 2.25f, [At(1, 2, 3)]);
        RagdollSave.Write(record, original);
        Assert.False(record.Strings[RagdollSave.Key].StartsWith("T2 ", StringComparison.Ordinal));
        var restored = Assert.IsType<SavedRagdoll>(RagdollSave.Read(record));
        Assert.Null(restored.Torso);
        Assert.Equal(original.Pose, restored.Pose);
        RagdollSave.Write(record, null);
        Assert.DoesNotContain(RagdollSave.Key, record.Strings.Keys);
    }

    [Theory]
    [InlineData("T2 0 0 0 0 1 | 1 0 1 0 0 0 0 0 0 1 | 1 0 1 0 0 0 0 0 0 1")]
    [InlineData("T2 0 1 0 -12 2 | 1 0 1 0 0 0 0 0 0 1 | 1 0 1 0 0 0 0 0 0 1")]
    [InlineData("T2 0 1 0 -12 1 | 1 0 1 0 0 0 0 0 0 0 | 1 0 1 0 0 0 0 0 0 1")]
    [InlineData("T2 0 1 0 -12 1 | 1 0 1 0 0 0 0 0 0 1")]
    [InlineData("T2 0 1 0 -12 1 | 1 -1 1 0 0 0 0 0 0 1 | 1 0 1 0 0 0 0 0 0 1")]
    public void Malformed_fragment_states_do_not_feed_invalid_transforms_to_physics(string text)
    {
        var record = new FcsRecord();
        record.Strings[RagdollSave.Key] = text;
        Assert.Null(RagdollSave.Read(record));
    }

    static RigidTransform At(float x, float y, float z) => new(new(x, y, z), Quaternion.Identity);
}
