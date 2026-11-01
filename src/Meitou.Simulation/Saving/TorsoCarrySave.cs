using System.Globalization;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Saving;

/// <summary>Meitou-only torso ownership and fallback placement; the physical pair stays in <see cref="RagdollSave"/>.</summary>
public static class TorsoCarrySave
{
    public const string SideKey = "meitou carry torso side", CutKey = "meitou torso cut", PositiveKey = "meitou torso positive", ReleaseKey = "meitou torso positive release";
    internal const string StandaloneKey = "meitou torso corpse";

    public static void Write(FcsRecord state, CharacterCold cold)
    {
        if (!cold.Carrying.IsNone && cold.CarryingTorsoSide == 1) state.Ints[SideKey] = 1;
        else state.Ints.Remove(SideKey);
        if (cold.TorsoBisection is not { IsValid: true } cut)
        {
            state.Strings.Remove(CutKey);
            state.Strings.Remove(PositiveKey);
            state.Strings.Remove(ReleaseKey);
            return;
        }
        var p = cut.Plane;
        state.Strings[CutKey] = FormattableString.Invariant($"{p.X:R} {p.Y:R} {p.Z:R} {p.W:R} {(cut.FullBody ? 1 : 0)}");
        if (cold.TorsoPositivePosition is { } at && Finite(at) && float.IsFinite(cold.TorsoPositiveYaw))
            state.Strings[PositiveKey] = FormattableString.Invariant($"{at.X:R} {at.Y:R} {at.Z:R} {cold.TorsoPositiveYaw:R}");
        else state.Strings.Remove(PositiveKey);
        if (cold.TorsoPositiveReleaseAt is { } release && Finite(release))
            state.Strings[ReleaseKey] = FormattableString.Invariant($"{release.X:R} {release.Y:R} {release.Z:R}");
        else state.Strings.Remove(ReleaseKey);
    }

    public static int Side(FcsRecord? state) => state?.Ints.GetValueOrDefault(SideKey) == 1 ? 1 : 0;

    /// <summary>Read before restoring carries: a saved positive holder may be far from the corpse's negative root.</summary>
    public static void Read(FcsRecord? state, CharacterCold cold)
    {
        if (state is null) return;
        if (Tokens(state, CutKey) is { Length: 5 } plane && TryF(plane[0], out float x) && TryF(plane[1], out float y)
            && TryF(plane[2], out float z) && TryF(plane[3], out float w) && plane[4] is "0" or "1")
        {
            var cut = new TorsoBisectionState(new(x, y, z, w), plane[4] == "1");
            if (cut.IsValid) cold.TorsoBisection = cut;
        }
        cold.TorsoBisection ??= cold.Save?.Ragdoll?.Torso?.Cut;
        if (cold.TorsoBisection is not { IsValid: true }) return;
        if (Tokens(state, PositiveKey) is { Length: 4 } place && TryF(place[0], out float px) && TryF(place[1], out float py)
            && TryF(place[2], out float pz) && TryF(place[3], out float yaw))
            (cold.TorsoPositivePosition, cold.TorsoPositiveYaw) = (new(px, py, pz), yaw);
        if (Tokens(state, ReleaseKey) is { Length: 3 } release && TryF(release[0], out float rx) && TryF(release[1], out float ry)
            && TryF(release[2], out float rz)) cold.TorsoPositiveReleaseAt = new(rx, ry, rz);
    }

    static string[]? Tokens(FcsRecord state, string key) => state.Strings.TryGetValue(key, out var text)
        ? text.Split(' ', StringSplitOptions.RemoveEmptyEntries) : null;
    static bool TryF(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
