using System.Globalization;
using Meitou.Data;
using Meitou.Data.Fcs;

namespace Meitou.Nitoryu;

/// <summary>
/// Which NPCs fight with two blades and at what Dual Wielding and Path (README "NPCs", FCS.md "The NPC opt-in list"): by CHARACTER or FACTION
/// string id, from the field <c>nitoryu dual wielder</c>, the squads <c>11-Nitoryu.mod</c> (opt-in) and <c>23-</c> to <c>26-Nitoryu.mod</c>
/// (Dual Wielding 40 / 60 / 80 / 100; a first value of 2 or more is the exact level, a second the lowest Path), the fields <c>nitoryu dual
/// wielding</c> and <c>nitoryu path</c>, and the ini's <c>[NPC] opt_in</c>, <c>skill</c> and <c>path</c> (the ini wins).
/// </summary>
sealed class NpcRules
{
    static readonly (string Squad, float Level)[] Squads =
        [("11-Nitoryu.mod", 0), ("23-Nitoryu.mod", 40), ("24-Nitoryu.mod", 60), ("25-Nitoryu.mod", 80), ("26-Nitoryu.mod", 100)];

    static readonly string[] SquadLists = ["squad", "squad2", "leader", "choosefrom list", "faction"];

    readonly HashSet<string> _optIn = new(StringComparer.Ordinal);
    readonly Dictionary<string, float> _level = new(StringComparer.Ordinal);
    readonly Dictionary<string, float> _path = new(StringComparer.Ordinal);

    public NpcRules(GameDatabase db, NitoryuConfig config)
    {
        ReadFields(db, FcsRecordType.Character);
        ReadFields(db, FcsRecordType.Faction);
        ReadSquads(db);
        foreach (string id in Split(config.IniRaw("npc.opt_in"))) _optIn.Add(id);
        ReadPairs(config.IniRaw("npc.skill"), _level);
        ReadPairs(config.IniRaw("npc.path"), _path);
    }

    void ReadFields(GameDatabase db, FcsRecordType type)
    {
        foreach (var r in db.OfType(type))
        {
            if (r.GetBool("nitoryu dual wielder")) _optIn.Add(r.StringId);
            if (r.GetFloat("nitoryu dual wielding") is > 0 and var level) _level[r.StringId] = level;
            if (r.GetFloat("nitoryu path") is > 0 and var path) _path[r.StringId] = path;
        }
    }

    void ReadSquads(GameDatabase db)
    {
        foreach (var (squad, level) in Squads)
        {
            if (db.Find(squad) is not { } r) continue;

            foreach (string list in SquadLists)
            {
                foreach (var entry in r.GetReferences(list))
                {
                    _optIn.Add(entry.TargetStringId);
                    float exact = entry.Values.Value0 >= 2 ? entry.Values.Value0 : level;
                    if (exact > 0) _level.TryAdd(entry.TargetStringId, exact);
                    if (entry.Values.Value1 >= 2) _path.TryAdd(entry.TargetStringId, entry.Values.Value1);
                }
            }
        }
    }

    static IEnumerable<string> Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static void ReadPairs(string text, Dictionary<string, float> into)
    {
        foreach (string pair in Split(text))
        {
            int colon = pair.LastIndexOf(':');
            if (colon <= 0 || !float.TryParse(pair[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) continue;

            into[pair[..colon].Trim()] = v;
        }
    }

    /// <summary>Whether the character (its record or its faction) is opted in.</summary>
    public bool OptedIn(string character, string? faction) => _optIn.Contains(character) || faction is not null && _optIn.Contains(faction);

    /// <summary>The Dual Wielding a character starts at by the rules, else null.</summary>
    public float? Level(string character, string? faction) =>
        _level.TryGetValue(character, out float a) ? a : faction is not null && _level.TryGetValue(faction, out float b) ? b : null;

    /// <summary>The lowest Path for a character by the rules, else 0.</summary>
    public float PathFloor(string character, string? faction) =>
        _path.TryGetValue(character, out float a) ? a : faction is not null && _path.TryGetValue(faction, out float b) ? b : 0;
}
