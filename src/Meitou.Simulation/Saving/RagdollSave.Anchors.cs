using System.Globalization;
using System.Numerics;
using System.Text;
using Meitou.Data.Fcs;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Simulation.Saving;

/// <summary>Every rig bone's physical actor and its retained local transform, including unphysical carried children.</summary>
public sealed record SavedFragmentAnchors(int[] Anchors, RigidTransform[] Offsets);

public static partial class RagdollSave
{
    public const string TorsoAnchorsKey = "meitou torso anchors";

    static void WriteAnchors(FcsRecord state, SavedTorsoRagdoll? torso)
    {
        if (torso is null || torso.PositiveAnchors is null && torso.NegativeAnchors is null)
        {
            state.Strings.Remove(TorsoAnchorsKey);
            return;
        }
        var text = new StringBuilder("A1 | ");
        AppendAnchors(text, torso.PositiveAnchors, torso.Positive.Pose.Length);
        text.Append(" | ");
        AppendAnchors(text, torso.NegativeAnchors, torso.Negative.Pose.Length);
        state.Strings[TorsoAnchorsKey] = text.ToString();
    }

    static void AppendAnchors(StringBuilder text, SavedFragmentAnchors? saved, int actors)
    {
        if (saved is null || saved.Anchors.Length == 0 || saved.Anchors.Length != saved.Offsets.Length
            || saved.Anchors.Any(actor => actor < 0 || actor >= actors)
            || saved.Offsets.Any(pose => !FiniteOffset(pose)))
        {
            text.Append('-');
            return;
        }
        text.Append(saved.Anchors.Length);
        for (int bone = 0; bone < saved.Anchors.Length; bone++)
        {
            var pose = saved.Offsets[bone];
            text.Append(' ').Append(saved.Anchors[bone].ToString(CultureInfo.InvariantCulture));
            foreach (float value in (ReadOnlySpan<float>)[pose.Position.X, pose.Position.Y, pose.Position.Z, pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W])
                text.Append(' ').Append(F(value));
        }
    }

    static (SavedFragmentAnchors? Positive, SavedFragmentAnchors? Negative) ReadAnchors(FcsRecord state, int positiveActors, int negativeActors)
    {
        if (!state.Strings.TryGetValue(TorsoAnchorsKey, out var text)) return default;
        var parts = text.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || parts[0] != "A1") return default;
        return (ReadAnchors(parts[1], positiveActors), ReadAnchors(parts[2], negativeActors));
    }

    static SavedFragmentAnchors? ReadAnchors(string text, int actors)
    {
        if (text == "-") return null;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            || count <= 0 || parts.Length != 1L + count * 8L) return null;
        var anchors = new int[count];
        var offsets = new RigidTransform[count];
        Span<float> values = stackalloc float[7];
        for (int bone = 0; bone < count; bone++)
        {
            int start = 1 + bone * 8;
            if (!int.TryParse(parts[start], NumberStyles.Integer, CultureInfo.InvariantCulture, out int actor) || actor < 0 || actor >= actors) return null;
            for (int value = 0; value < values.Length; value++) if (!TryF(parts[start + 1 + value], out values[value])) return null;
            var offset = new RigidTransform(new(values[0], values[1], values[2]), new(values[3], values[4], values[5], values[6]));
            if (!FiniteOffset(offset)) return null;
            anchors[bone] = actor;
            offsets[bone] = offset with { Rotation = Quaternion.Normalize(offset.Rotation) };
        }
        return new(anchors, offsets);
    }

    static bool FiniteOffset(RigidTransform pose) => float.IsFinite(pose.Position.X) && float.IsFinite(pose.Position.Y) && float.IsFinite(pose.Position.Z)
        && float.IsFinite(pose.Rotation.LengthSquared()) && pose.Rotation.LengthSquared() >= 1e-12f;
}
