using System.Globalization;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SpeedMenu;

/// <summary>
/// One row of the panel: the character (its portrait), its name and its "current mph (max max)" line.
/// </summary>
sealed record SpeedRow(CharacterId Id, string Name, string Speeds);

/// <summary>
/// Sorts the selected characters' published speeds once a frame (<c>ModContext.AddFrame</c>, on the host's thread) for the panel. The order is
/// the original's: the shown maximum speed (mph to a tenth), slowest first; a tie keeps the rows' previous order, then goes by name.
/// </summary>
static class SpeedRows
{
    /// <summary>
    /// The original's conversion: world units per second per mph.
    /// </summary>
    const float UnitsPerMph = 4.4704f;

    /// <summary>
    /// The rows as last sorted; null before the first frame of a world.
    /// </summary>
    public static volatile IReadOnlyList<SpeedRow>? Rows;

    /// <summary>
    /// The rows' order last frame (the tie-break).
    /// </summary>
    static List<CharacterId> s_lastOrder = [];

    /// <summary>
    /// Sorts the frame's selection by the running system's speeds.
    /// </summary>
    public static void Frame(ModFrame frame)
    {
        if (SpeedMenuMod.Speeds is not { } speeds)
        {
            Rows = null;
            return;
        }

        var rows = Sorted(frame.Selected, speeds.Published);
        s_lastOrder = [.. rows.Select(r => r.Id)];
        Rows = rows;
    }

    /// <summary>
    /// The selected characters with published speeds, in the original's order.
    /// </summary>
    static List<SpeedRow> Sorted(IReadOnlyList<CharacterId> selected, IReadOnlyDictionary<CharacterId, Runner> published)
    {
        var previous = s_lastOrder;
        return [.. selected
            .Where(published.ContainsKey)
            .Select(id => (Id: id, Runner: published[id]))
            .OrderBy(r => Tenths(r.Runner.MaxSpeed))
            .ThenBy(r => previous.IndexOf(r.Id) is var at and >= 0 ? at : int.MaxValue)
            .ThenBy(r => r.Runner.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Id.Slot)
            .Select(r => Row(r.Id, r.Runner))];
    }

    /// <summary>
    /// A character's row texts.
    /// </summary>
    static SpeedRow Row(CharacterId id, Runner runner)
    {
        float current = Mph(runner.Speed), max = Mph(runner.MaxSpeed);
        return new(id, runner.Name, string.Format(CultureInfo.InvariantCulture, "{0:0.0} mph ({1:0.0} max)", current, max));
    }

    /// <summary>
    /// A speed in mph; 0 for one that is not finite, negative or 10000 and over (the original's guard).
    /// </summary>
    static float Mph(float unitsPerSecond)
    {
        float mph = unitsPerSecond / UnitsPerMph;
        return float.IsFinite(mph) && mph >= 0 && mph < 10000 ? mph : 0;
    }

    /// <summary>
    /// The sort key: the maximum speed in tenths of an mph, rounded.
    /// </summary>
    static int Tenths(float unitsPerSecond) => (int)MathF.Floor(Mph(unitsPerSecond) * 10 + 0.5f);
}
