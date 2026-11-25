namespace Meitou.QuestsContracts;

/// <summary>
/// The Ledger window's copy of the state (made in the serial step; the interface reads only this): the open contracts, the history, the
/// reputation and the event log.
/// </summary>
sealed record LedgerView(int Day, IReadOnlyList<string> Contracts, IReadOnlyList<string> History, IReadOnlyList<string> Reputation,
    IReadOnlyList<string> Events, IReadOnlyList<string> Bounties, IReadOnlyList<string> Readout)
{
    public static LedgerView Of(LedgerState state, int day, IReadOnlyList<string> readout, IReadOnlyList<string> bounties) => new(day,
        [.. state.Slots.Select(s => Contract(s, day))],
        [.. state.History.AsEnumerable().Reverse().Select(c => $"{c.Outcome}: {c.Type} for {c.Giver} in {c.Town}{Object(c.Target)} - {c.Reward}")],
        [.. state.Rep.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key.Replace(":", ": ")}  {kv.Value}")],
        [.. state.Events.AsEnumerable().Reverse()], bounties, readout);

    /// <summary>The map's quest dots: the locked targets where they stand, the guild's towns and chests (world X, Z and a label).</summary>
    public IReadOnlyList<(System.Numerics.Vector2 At, string Label, bool Person)> Dots { get; init; } = [];

    /// <summary>The locked targets, for the readout's portraits (at most four).</summary>
    public IReadOnlyList<Meitou.Simulation.CharacterId> Faces { get; init; } = [];

    static string Contract(Slot s, int day)
    {
        int left = (s.Days > 0 ? s.Days : LedgerSystem.FailDays) - (day - s.AcceptDay);
        string item = s.Item.Length > 0 ? s.Item : "items";
        string what = s.TargetName.Length > 0 ? $" - {s.TargetName}" : s.WantCount > 0 ? $" - {s.WantCount} {item}" : "";
        string where = s.DestName.Length > 0 ? $" to {s.DestName}" : "";
        string state = s.State switch { 2 => "READY", 3 => "talked down", _ => $"{Math.Max(0, left)} day(s) left" };
        return $"{s.Type} for {s.Giver.Npc} in {s.Giver.Town}{where}{what} ({state})";
    }

    static string Object(string target) => target.Length > 0 ? $" ({target})" : "";
}
