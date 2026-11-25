namespace Meitou.QuestsContracts;

/// <summary>
/// One signal line of <c>ledger-signals.txt</c> (<c>what=line sid|key=value|...</c>): what the line means when it is said.
/// </summary>
sealed class Signal
{
    public required string What { get; init; }
    public required string Line { get; init; }
    public int Kind { get; set; }
    public int Count { get; set; }
    public int Pay { get; set; }
    public string Type { get; set; } = "";
    public bool Special { get; set; }
    public bool Half { get; set; }
    public int Town { get; set; }
    public int Route { get; set; }
    public int Dest { get; set; }
    public string DestName { get; set; } = "";
    public int Origin { get; set; }
    public int Days { get; set; }
    public string House { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Promo { get; set; } = "";
    public IReadOnlyList<string> Want { get; set; } = [];
    public IReadOnlyList<string> Targets { get; set; } = [];
    public IReadOnlyList<string> Squads { get; set; } = [];
    public bool Betray { get; set; }
    public int Dist { get; set; }
    public IReadOnlyList<int> Gifts { get; set; } = [];
    public IReadOnlyList<string> GiftNames { get; set; } = [];
    public string Camp { get; set; } = "";
    public string Stash { get; set; } = "";

    public bool Is(string prefix) => What.StartsWith(prefix, StringComparison.Ordinal);
}

/// <summary>
/// A wanted poster of <c>ledger-bounties.txt</c>: the poster item and the wanted characters.
/// </summary>
sealed record Bounty(string Poster, string Name, string Faction, int Reward, IReadOnlyList<string> Targets, string TargetNames, string Blurb,
    bool Hunt);

/// <summary>
/// The original's lists, read from its folder: the variables (Extra Extensions variable records, by name), the signal lines, the givers and the
/// posters.
/// </summary>
sealed class LedgerTables
{
    public List<(string Name, string Record)> Variables { get; } = [];
    public List<Signal> Signals { get; } = [];
    public Dictionary<string, int> Givers { get; } = new(StringComparer.Ordinal);
    public List<Bounty> Bounties { get; } = [];
    public float OverlayRadius { get; private set; } = 2500;
    public string MarkGiver { get; private set; } = "!";
    public string MarkTarget { get; private set; } = "!!";
    public string MarkTalker { get; private set; } = "...";

    public static LedgerTables Read(string folder)
    {
        var t = new LedgerTables();
        foreach (var (key, value) in Pairs(Path.Combine(folder, "ledger-vars.txt"))) t.Variables.Add((key, value));
        foreach (var (key, value) in Pairs(Path.Combine(folder, "ledger-signals.txt"))) t.Signals.Add(ParseSignal(key, value));
        foreach (string line in Lines(Path.Combine(folder, "ledger-givers.txt"))) t.AddGiver(line);
        foreach (string line in Lines(Path.Combine(folder, "ledger-bounties.txt"))) t.AddBounty(line);
        t.ReadSettings(Path.Combine(folder, "Ledger.json"));
        return t;
    }

    public int VariableIndex(string name) => Variables.FindIndex(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    static IEnumerable<string> Lines(string path) =>
        File.Exists(path) ? File.ReadLines(path).Select(l => l.TrimEnd('\r', '\n')).Where(l => l.Length > 0 && l[0] != '#') : [];

    static IEnumerable<(string Key, string Value)> Pairs(string path)
    {
        foreach (string line in Lines(path))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) yield return (line[..eq], line[(eq + 1)..]);
        }
    }

    static Signal ParseSignal(string what, string rest)
    {
        var parts = rest.Split('|');
        var s = new Signal { What = what, Line = parts[0] };
        foreach (string part in parts.Skip(1)) Field(s, part);
        return s;
    }

    static void Field(Signal s, string part)
    {
        int eq = part.IndexOf('=');
        if (eq <= 0) return;

        string key = part[..eq], v = part[(eq + 1)..];
        switch (key)
        {
            case "kind": s.Kind = Int(v); break;
            case "count": s.Count = Int(v); break;
            case "pay": s.Pay = Int(v); break;
            case "type": s.Type = v; break;
            case "special": s.Special = Int(v) != 0; break;
            case "half": s.Half = Int(v) != 0; break;
            case "town": s.Town = Int(v); break;
            case "route": s.Route = Int(v); break;
            case "dest": s.Dest = Int(v); break;
            case "destname": s.DestName = v; break;
            case "origin": s.Origin = Int(v); break;
            case "days": s.Days = Int(v); break;
            case "house": s.House = v; break;
            case "poster": s.Poster = v; break;
            case "promo": s.Promo = v; break;
            case "want": s.Want = List(v); break;
            case "targets": s.Targets = List(v); break;
            case "squads": s.Squads = List(v); break;
            case "betray": s.Betray = Int(v) != 0; break;
            case "dist": s.Dist = Int(v); break;
            case "gifts": s.Gifts = [.. List(v).Select(Int)]; break;
            case "giftnames": s.GiftNames = List(v); break;
            case "camp": s.Camp = v; break;
            case "stash": s.Stash = v; break;
        }
    }

    /// <summary>The original's <c>Ledger.json</c>: the overlay's radius and marks (its keys are bound on the Controls page instead).</summary>
    void ReadSettings(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("OverlayRadius", out var r) && r.TryGetSingle(out float radius) && radius > 0) OverlayRadius = radius;
            if (root.TryGetProperty("MarkGiver", out var g) && g.GetString() is { Length: > 0 } giver) MarkGiver = giver;
            if (root.TryGetProperty("MarkTarget", out var t) && t.GetString() is { Length: > 0 } target) MarkTarget = target;
            if (root.TryGetProperty("MarkTalker", out var k) && k.GetString() is { Length: > 0 } talker) MarkTalker = talker;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or InvalidOperationException)
        {
        }
    }

    void AddGiver(string line)
    {
        var parts = line.Split('|');
        Givers[parts[0]] = parts.Length > 1 ? Int(parts[1]) : 0;
    }

    void AddBounty(string line)
    {
        var p = line.Split('|');
        if (p.Length >= 5) Bounties.Add(new Bounty(p[0], p[1], p[2], Int(p[3]), List(p[4]), At(p, 5), At(p, 6), At(p, 7) == "1"));
    }

    static string At(string[] p, int i) => i < p.Length ? p[i] : "";

    static IReadOnlyList<string> List(string v) => v.Split(';', StringSplitOptions.RemoveEmptyEntries);

    static int Int(string v) => int.TryParse(v, out int i) ? i : 0;
}
