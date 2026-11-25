namespace Meitou.SentientSands;

/// <summary>
/// The barter table (server <c>chat/offers.py</c> <c>on_table</c>, <c>table_parts</c>): while it is open every chat line carries <c>barter</c>
/// with the player's squad mates near the NPC (their contexts and keys) and the deal on the table: <c>give</c> (the NPC's items to the player),
/// <c>take</c> (the player's items, each with its holder's key), <c>give_cats</c> and <c>take_cats</c>. The server answers each line with
/// <c>CMD: BARTER_TURN: 0|1</c> and the thread's lines, and an agreed deal with an offer popup.
/// </summary>
sealed partial class ChatWindow
{
    const float SquadReach = 600;

    /// <summary>One line of the table: an item, its count and, for the player's side, its holder's key.</summary>
    sealed record DealItem(bool Give, string Name, int Count, string Holder);

    readonly List<DealItem> _deal = [];
    bool _barter;
    long _giveCats, _takeCats;

    void ToggleBarter()
    {
        _barter = !_barter;
        ClearDeal();
    }

    void ClearDeal()
    {
        _deal.Clear();
        (_giveCats, _takeCats) = (0, 0);
    }

    /// <summary>Puts one more of an item on the table (the NPC's: give; the player's: take).</summary>
    void Put(bool give, string name, string holder)
    {
        int i = _deal.FindIndex(d => d.Give == give && d.Name == name && d.Holder == holder);
        if (i >= 0) _deal[i] = _deal[i] with { Count = _deal[i].Count + 1 };
        else _deal.Add(new DealItem(give, name, 1, holder));
    }

    Dictionary<string, object?> BarterWindow(GameFacts facts)
    {
        var npc = facts.Of(_npc);
        var mates = npc is null ? [] : facts.Characters.Values
            .Where(c => c.IsPlayer && System.Numerics.Vector3.Distance(c.Position, npc.Position) <= SquadReach)
            .Select(c => Mate(c, facts)).ToList();
        var deal = new Dictionary<string, object?>
        {
            ["give"] = _deal.Where(d => d.Give).Select(d => new Dictionary<string, object?> { ["name"] = d.Name, ["count"] = d.Count }).ToList(),
            ["take"] = _deal.Where(d => !d.Give)
                .Select(d => new Dictionary<string, object?> { ["name"] = d.Name, ["count"] = d.Count, ["holder"] = d.Holder }).ToList(),
            ["give_cats"] = _giveCats, ["take_cats"] = _takeCats,
        };
        return new Dictionary<string, object?> { ["squad"] = mates, ["deal"] = deal };
    }

    static Dictionary<string, object?> Mate(CharacterFacts c, GameFacts facts)
    {
        var context = c.Context(facts.Day, facts.Hour, facts.Minute);
        context["key"] = ServerMessage.Key(c.Id);
        return context;
    }

    /// <summary><c>CMD: BARTER_TURN: open</c> then the thread's lines: the window shows the lines; a closed action dialogue clears the
    /// table.</summary>
    void BarterTurn(string rest)
    {
        var lines = rest.Split('\n');
        if (lines[0].Trim() == "0") _deal.Clear();

        foreach (string line in lines.Skip(1).Where(l => l.Trim().Length > 0)) Add(line.Trim());
    }
}
