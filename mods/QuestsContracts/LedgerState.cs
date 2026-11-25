using System.Text.Json;

namespace Meitou.QuestsContracts;

/// <summary>
/// Who a job or a reputation change is about: the NPC's name, town and faction (the original also keys by squad name; the port has no squad
/// names and leaves that key out).
/// </summary>
sealed record Who(string Npc, string Town, string Faction);

/// <summary>
/// An open contract (the original's job slot): the town code it lives in, its kind and terms, its state (1 open, 2 READY, 3 talked down) and the
/// locked target (by lasting id and name).
/// </summary>
sealed class Slot
{
    public int Town { get; set; }
    public int Kind { get; set; }
    public string Type { get; set; } = "delivery";
    public List<string> Want { get; set; } = [];
    public int WantCount { get; set; }
    public int Pay { get; set; }
    public int AcceptDay { get; set; }
    public int Days { get; set; }
    public int Dest { get; set; }
    public string DestName { get; set; } = "";
    public string Item { get; set; } = "";
    public required Who Giver { get; set; }
    public List<string> Targets { get; set; } = [];
    public int State { get; set; } = 1;
    public ulong Target { get; set; }
    public string TargetName { get; set; } = "";
    public int TargetKind { get; set; }
    public int Route { get; set; }
    public bool Betray { get; set; }
    public int Gift { get; set; }
    public string GiftName { get; set; } = "";
    public bool Scouted { get; set; }
    public int FoundIndex { get; set; }
    public string Found { get; set; } = "";
    public string Stash { get; set; } = "";
    public string NearTown { get; set; } = "";
    public int TalkTries { get; set; }
    public long TalkTick { get; set; }
    public int CellPlatoon { get; set; } = -1;
    public string Cell { get; set; } = "";
}

/// <summary>
/// A wanted poster's tracking (the original's bounty record): the day it was read and where, where and when the wanted one was last seen, and
/// the day he was brought in.
/// </summary>
sealed class PosterTrack
{
    public int ReadDay { get; set; } = -1;
    public string ReadTown { get; set; } = "";
    public int SeenDay { get; set; } = -1;
    public string SeenTown { get; set; } = "";
    public int ClaimedDay { get; set; } = -1;
    public List<ulong> Priced { get; set; } = [];
}

/// <summary>
/// A closed contract for the Ledger's history: kind, type, giver, town, days, outcome and reward.
/// </summary>
sealed record Closed(int Kind, string Type, string Giver, string Town, string Target, int AcceptDay, int CloseDay, string Outcome, string Reward);

/// <summary>
/// What the Ledger keeps per save (the original's <c>ledger-&lt;save&gt;.txt</c>): reputation 0..100 by <c>npc:</c>, <c>town:</c> and
/// <c>faction:</c> keys, the per-town job windows, the delivered and special days, the open contracts, the history, the posters read and the event
/// log. Saved as JSON in the mod's sidecar.
/// </summary>
sealed class LedgerState
{
    public SortedDictionary<string, int> Rep { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> LastJob { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Jobs { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Done { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Special { get; set; } = new(StringComparer.Ordinal);
    public List<Slot> Slots { get; set; } = [];
    public List<Closed> History { get; set; } = [];
    public List<string> Events { get; set; } = [];
    public int FailPending { get; set; }
    public SortedDictionary<string, PosterTrack> Posters { get; set; } = new(StringComparer.Ordinal);
    public int EscortRestDay { get; set; } = -1;
    public double CaravanRestHours { get; set; } = -1;
    public List<int> CaravanVisited { get; set; } = [];
    public ulong Draws { get; set; }

    public static int Get(SortedDictionary<string, int> table, string key, int fallback) => table.TryGetValue(key, out int v) ? v : fallback;

    public void Event(int day, string text)
    {
        Events.Add($"Day {day}: {text}");
        if (Events.Count > 200) Events.RemoveAt(0);
    }

    public void Close(Slot s, int day, string outcome, string reward)
    {
        History.Add(new Closed(s.Kind, s.Type, s.Giver.Npc, s.Giver.Town, s.TargetName.Length > 0 ? s.TargetName : s.Item, s.AcceptDay, day,
            outcome, reward));
        if (History.Count > 100) History.RemoveAt(0);

        Slots.Remove(s);
    }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    /// <summary>Reads a saved state; its tables are made ordinal again (the reader makes them with the culture's order).</summary>
    public static LedgerState Deserialize(Stream stream)
    {
        var s = JsonSerializer.Deserialize<LedgerState>(stream) ?? new LedgerState();
        s.Rep = Ordinal(s.Rep);
        s.LastJob = Ordinal(s.LastJob);
        s.Jobs = Ordinal(s.Jobs);
        s.Done = Ordinal(s.Done);
        s.Special = Ordinal(s.Special);
        s.Posters = new(s.Posters ?? [], StringComparer.Ordinal);
        return s;
    }

    static SortedDictionary<string, int> Ordinal(SortedDictionary<string, int>? table) => new(table ?? [], StringComparer.Ordinal);
}
