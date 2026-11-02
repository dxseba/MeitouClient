using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Saving;

/// <summary>The immutable cut in normalized head-box coordinates, independent of transient loose-piece physics.</summary>
public static class HeadCutSave
{
    public const string Key = "meitou head cut";
    internal const string CorpseKey = "meitou head corpse";

    public static void Write(FcsRecord state, HeadCutState? cut, bool corpse = false)
    {
        if (cut is not { IsValid: true } saved)
        {
            state.Strings.Remove(Key);
            state.Bools.Remove(CorpseKey);
            return;
        }
        var p = saved.Plane;
        state.Strings[Key] = FormattableString.Invariant($"H1 {(int)saved.Mode} {p.X:R} {p.Y:R} {p.Z:R} {p.W:R}");
        if (corpse) state.Bools[CorpseKey] = true;
        else state.Bools.Remove(CorpseKey);
    }

    public static HeadCutState? Read(FcsRecord? state)
    {
        if (state is null || !state.Strings.TryGetValue(Key, out var text)) return null;
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 6 || fields[0] != "H1"
            || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode)) return null;
        Span<float> plane = stackalloc float[4];
        for (int index = 0; index < plane.Length; index++)
            if (!float.TryParse(fields[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out plane[index])
                || !float.IsFinite(plane[index])) return null;
        var cut = new HeadCutState(new(plane[0], plane[1], plane[2], plane[3]), (HeadCutMode)mode);
        return cut.IsValid ? cut : null;
    }
}
