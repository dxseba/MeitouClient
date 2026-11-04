using System.Globalization;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;

namespace Meitou.Simulation.Saving;

/// <summary>Normalized limb cuts survive actor unloads without serializing transient detached physics.</summary>
public static class LimbCutSave
{
    public const string Key = "meitou limb cuts";

    public static void Write(FcsRecord state, IReadOnlyList<LimbCutState>? cuts)
    {
        var valid = (cuts ?? []).Where(c => c.IsValid).DistinctBy(c => c.LimbSlot).OrderBy(c => c.LimbSlot).Take(4).ToArray();
        if (valid.Length == 0) { state.Strings.Remove(Key); return; }
        state.Strings[Key] = "L1 " + string.Join(" ", valid.Select(c =>
            FormattableString.Invariant($"{c.LimbSlot} {c.Fraction:R} {c.Slant.X:R} {c.Slant.Y:R}")));
    }

    public static IReadOnlyList<LimbCutState> Read(FcsRecord? state)
    {
        if (state is null || !state.Strings.TryGetValue(Key, out string? text)) return [];
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 5 or > 17 || fields[0] != "L1" || (fields.Length - 1) % 4 != 0) return [];
        var cuts = new List<LimbCutState>(4);
        for (int i = 1; i < fields.Length; i += 4)
        {
            if (!int.TryParse(fields[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
                || !float.TryParse(fields[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fraction)
                || !float.TryParse(fields[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(fields[i + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return [];
            var cut = new LimbCutState(slot, fraction, new(x, y));
            if (!cut.IsValid || cuts.Any(c => c.LimbSlot == slot)) return [];
            cuts.Add(cut);
        }
        return [.. cuts.OrderBy(c => c.LimbSlot)];
    }
}
