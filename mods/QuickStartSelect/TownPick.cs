using Meitou.Data.Gameplay;

namespace Meitou.QuickStartSelect;

/// <summary>
/// The town the player picked for a start with several (the original leaves only that town in the start's list for the game's start): a Harmony
/// prefix on <c>PopulationSystem.StartPlace</c>, which both the host's navmesh load and the player's start call, hands the game the start with that
/// one town. Cleared when the window opens again. The draw is the game's own, so it stays deterministic.
/// </summary>
static class TownPick
{
    /// <summary>The start id and town id picked; null for the game's random draw.</summary>
    static volatile Tuple<string, string>? s_pick;

    /// <summary>Where a failing prefix is logged (once).</summary>
    public static Action<string>? Log;

    static bool s_logged;

    public static void Set(string start, string? town) => s_pick = town is null ? null : Tuple.Create(start, town);

    public static void Clear() => s_pick = null;

    public static string? For(string start) => s_pick is { } p && p.Item1 == start ? p.Item2 : null;

    /// <summary>Harmony prefix: the picked start's town list cut to the picked town (a town not in its list is ignored).</summary>
    public static void Prefix(ref NewGameStart start)
    {
        try
        {
            if (For(start.Id) is not { } town || !start.Towns.Any(t => t.Id == town)) return;

            start = start with { Towns = [.. start.Towns.Where(t => t.Id == town).Take(1)] };
        }
        catch (Exception e)
        {
            // fall back to the game's draw
            if (!s_logged) Log?.Invoke($"town pick failed, the game draws the town: {e.Message}");
            s_logged = true;
        }
    }
}
