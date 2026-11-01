using System.Globalization;
using System.Numerics;
using System.Text;
using Meitou.Data.Fcs;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Saving;

/// <summary>A whole-body ragdoll as saved: settled (<see cref="Frozen"/>) or not, its age in seconds and every body's world pose in the ragdoll definition's order.</summary>
public sealed record SavedRagdoll(bool Frozen, float Age, RigidTransform[] Pose, SavedTorsoRagdoll? Torso = null);

/// <summary>The exact physical sides of a cut; the immutable cut plane identifies the matching collider topology.</summary>
public sealed record SavedTorsoRagdoll(TorsoBisectionState Cut, SavedRagdoll Positive, SavedRagdoll Negative);

/// <summary>
/// A knocked-down or dead character's ragdoll in a save (docs/game/ragdolls.md "Saves"): Meitou's own GAMESTATE_CHARACTER string <c>meitou ragdoll</c>, "frozen age count" then
/// position and rotation (x y z, x y z w) per body, invariant floats. Only a whole-body ragdoll is written (carried and limb ragdolls follow the animation and are made again);
/// the original saves none and ignores the key. Read once, when the character is made from the save; the ragdoll system takes it the first time it sees the character.
/// </summary>
public static class RagdollSave
{
    public const string Key = "meitou ragdoll";

    public static void Write(FcsRecord state, SavedRagdoll? ragdoll)
    {
        if (ragdoll is null) { state.Strings.Remove(Key); return; }
        var s = new StringBuilder();
        if (ragdoll.Torso is { } torso)
        {
            var plane = torso.Cut.Plane;
            s.Append("T2 ").Append(F(plane.X)).Append(' ').Append(F(plane.Y)).Append(' ').Append(F(plane.Z)).Append(' ').Append(F(plane.W))
                .Append(' ').Append(torso.Cut.FullBody ? '1' : '0').Append(" | ");
            Append(s, torso.Positive);
            s.Append(" | ");
            Append(s, torso.Negative);
        }
        else Append(s, ragdoll);
        state.Strings[Key] = s.ToString();
    }

    /// <summary>The saved ragdoll; null when there is none or the text does not parse.</summary>
    public static SavedRagdoll? Read(FcsRecord? state)
    {
        if (state is null || !state.Strings.TryGetValue(Key, out var text)) return null;
        if (text.StartsWith("T2 ", StringComparison.Ordinal))
        {
            var sections = text.Split('|', StringSplitOptions.TrimEntries);
            if (sections.Length != 3) return null;
            var header = sections[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length != 6 || !TryF(header[1], out float x) || !TryF(header[2], out float y)
                || !TryF(header[3], out float z) || !TryF(header[4], out float w) || header[5] is not ("0" or "1")) return null;
            var cut = new TorsoBisectionState(new(x, y, z, w), header[5] == "1");
            if (!cut.IsValid || ReadBody(sections[1]) is not { } positive || ReadBody(sections[2]) is not { } negative) return null;
            return negative with { Torso = new SavedTorsoRagdoll(cut, positive, negative) };
        }
        return ReadBody(text);
    }

    static void Append(StringBuilder s, SavedRagdoll ragdoll)
    {
        s.Append(ragdoll.Frozen ? '1' : '0').Append(' ').Append(F(ragdoll.Age)).Append(' ').Append(ragdoll.Pose.Length);
        foreach (var p in ragdoll.Pose)
            foreach (float v in (ReadOnlySpan<float>)[p.Position.X, p.Position.Y, p.Position.Z, p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W])
                s.Append(' ').Append(F(v));
    }

    static SavedRagdoll? ReadBody(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] is not ("0" or "1")
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n <= 0 || parts.Length != 3L + 7L * n
            || !TryF(parts[1], out float age) || age < 0) return null;
        var pose = new RigidTransform[n];
        Span<float> v = stackalloc float[7];
        for (int b = 0; b < n; b++)
        {
            for (int k = 0; k < 7; k++)
                if (!TryF(parts[3 + 7 * b + k], out v[k])) return null;
            var rotation = new Quaternion(v[3], v[4], v[5], v[6]);
            if (rotation.LengthSquared() < 1e-12f || !float.IsFinite(rotation.LengthSquared())) return null;
            pose[b] = new RigidTransform(new Vector3(v[0], v[1], v[2]), Quaternion.Normalize(rotation));
        }
        return new SavedRagdoll(parts[0] == "1", age, pose);
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static bool TryF(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && float.IsFinite(v);
}
