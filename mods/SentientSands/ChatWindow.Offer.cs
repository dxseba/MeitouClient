namespace Meitou.SentientSands;

/// <summary>
/// The offer popup (server <c>chat/offers.py</c> <c>command</c>): <c>CMD: OFFER: </c> then, one to a line, the offer's id, the NPC's and the
/// speaker's keys, the NPC's name, the popup's text, the <c>CHECK:</c> lines (<c>CATS side n</c>, <c>ITEM holder count name</c>,
/// <c>ROOM holder count name</c>), <c>RESTOCK:</c> and the <c>BOX:</c> lines of a trade (<c>BOX: side text</c>). Accept checks that each side still
/// holds its part and answers <c>/offer</c> with <c>accept</c> (or <c>failed</c>); Decline answers <c>decline</c>; <c>CMD: OFFER_GONE</c> closes it.
/// </summary>
sealed partial class ChatWindow
{
    sealed record OfferView(string Id, string Npc, string Text, IReadOnlyList<string> Checks, IReadOnlyList<string> PlayerBox,
        IReadOnlyList<string> NpcBox);

    OfferView? _offer;

    void Offer(string rest)
    {
        var lines = rest.Split('\n');
        if (lines.Length < 4) return;

        string Value(string prefix, string line) => line[prefix.Length..].Trim();
        var checks = lines.Where(l => l.StartsWith("CHECK: ", StringComparison.Ordinal)).Select(l => Value("CHECK: ", l)).ToList();
        var boxes = lines.Where(l => l.StartsWith("BOX: ", StringComparison.Ordinal)).Select(l => Value("BOX: ", l)).ToList();
        _offer = new OfferView(lines[0].Trim(), lines[2].Trim(), lines[3].Trim(), checks,
            [.. boxes.Where(b => b.StartsWith("player ", StringComparison.Ordinal)).Select(b => b[7..])],
            [.. boxes.Where(b => b.StartsWith("npc ", StringComparison.Ordinal)).Select(b => b[4..])]);
    }

    void Answer(bool accept)
    {
        if (_offer is not { } offer) return;

        string answer = !accept ? "decline" : Holds(offer) ? "accept" : "failed";
        var f = SentientSystem.Facts;
        bridge.Request("/offer", new { id = offer.Id, answer, npc = offer.Npc, day = f.Day, hour = f.Hour, minute = f.Minute }, null);
        if (answer == "failed") Add(context.Tr("The deal fell through: a side no longer holds its part."));

        _offer = null;
    }

    /// <summary>Whether every CHECK line still holds (the player's cats; the items each holder carries; a trader's stock is not checked).</summary>
    bool Holds(OfferView offer)
    {
        var facts = SentientSystem.Facts;
        foreach (string check in offer.Checks)
        {
            var p = check.Split(' ', 4);
            if (p.Length >= 3 && p[0] == "CATS" && p[1] == "player" && facts.Money < SentientSystem.Cats(p[2])) return false;
            if (p.Length == 4 && p[0] == "ITEM" && !Carries(facts, p[1], int.TryParse(p[2], out int n) ? n : 1, p[3])) return false;
        }

        return true;
    }

    bool Carries(GameFacts facts, string holder, int count, string name)
    {
        var who = holder switch { "player" => _player, "npc" => _npc, _ => ServerMessage.FromKey(holder) ?? _player };
        if (facts.Of(who) is not { } c) return true;
        if (holder == "npc" && c.IsTrader) return true;

        return c.Inventory.Where(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)).Sum(i => i.Count) >= count;
    }
}
