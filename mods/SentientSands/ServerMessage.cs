using System.Globalization;
using Meitou.Simulation;

namespace Meitou.SentientSands;

/// <summary>
/// The server's pipe messages (the prefixes its <c>send_to_pipe</c> uses and the original's plugin dispatches on): <c>NPC_SAY: label: text</c>,
/// <c>NPC_THINK: label: ...</c>, <c>PLAYER_SAY: name: text</c>, <c>NPC_ACTION: label: [ACTION: NAME: args]</c>, <c>NOTIFY: text</c>,
/// <c>SET_CONFIG: name: value</c>, <c>REPORT:</c>, <c>APPLY_TRANSLATION: json</c> and <c>CMD: ...</c> (<c>OFFER</c>, <c>OFFER_GONE</c>,
/// <c>BARTER_TURN</c>). A label is <c>Name|key</c>, optionally <c>&gt;holder key</c>; the port's key is a character id, <c>slot.generation</c>, and
/// its <c>npc_id</c> is <c>h:</c> and the key.
/// </summary>
static class ServerMessage
{
    public static string Key(CharacterId id) => string.Create(CultureInfo.InvariantCulture, $"{id.Slot}.{id.Generation}");

    public static string NpcId(CharacterId id) => "h:" + Key(id);

    public static CharacterId? FromKey(string key)
    {
        if (key.StartsWith("h:", StringComparison.Ordinal)) key = key[2..];

        var parts = key.Trim().Split('.');
        if (parts.Length != 2) return null;

        bool slot = int.TryParse(parts[0], CultureInfo.InvariantCulture, out int s);
        bool generation = int.TryParse(parts[1], CultureInfo.InvariantCulture, out int g);
        return slot && generation ? new CharacterId(s, g) : null;
    }

    /// <summary>The character a label names (the key after the bar, before any <c>&gt;</c>) and the holder after the <c>&gt;</c>.</summary>
    static (CharacterId? Who, CharacterId? Holder) Of(string label)
    {
        int bar = label.IndexOf('|');
        if (bar < 0) return (null, null);

        string key = label[(bar + 1)..];
        int gt = key.IndexOf('>');
        return gt < 0 ? (FromKey(key), null) : (FromKey(key[..gt]), FromKey(key[(gt + 1)..]));
    }

    static (string Label, string Text) Split(string rest)
    {
        int colon = rest.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? (rest.TrimEnd(':'), "") : (rest[..colon], rest[(colon + 2)..]);
    }

    /// <summary>The world command a message asks for (null: none), for the player's <paramref name="player"/>.</summary>
    public static SimCommand? Parse(string message, CharacterId player)
    {
        if (message.StartsWith("NOTIFY:", StringComparison.Ordinal)) return new ChatNotice(message[7..].Trim()) { Tick = 0 };
        if (message.StartsWith("PLAYER_SAY: ", StringComparison.Ordinal)) return new ChatLine(player, Split(message[12..]).Text) { Tick = 0 };

        bool say = message.StartsWith("NPC_SAY: ", StringComparison.Ordinal), think = message.StartsWith("NPC_THINK: ", StringComparison.Ordinal);
        if (say || think)
        {
            var (label, text) = Split(message[(say ? 9 : 11)..]);
            return Of(label).Who is { } who ? new ChatLine(who, text) { Tick = 0 } : null;
        }

        if (!message.StartsWith("NPC_ACTION: ", StringComparison.Ordinal)) return null;

        var (actor, action) = Split(message[12..]);
        var (npc, holder) = Of(actor);
        if (npc is not { } n || Action(action) is not var (name, args)) return null;

        return new ChatAction(n, player, name, args) { Tick = 0, Holder = holder ?? CharacterId.None };
    }

    /// <summary><c>[ACTION: NAME: args]</c> as its name and arguments.</summary>
    static (string Name, string Args)? Action(string text)
    {
        int start = text.IndexOf("[ACTION:", StringComparison.Ordinal);
        int end = start < 0 ? -1 : text.IndexOf(']', start);
        if (end < 0) return null;

        string inner = text[(start + 8)..end].Trim();
        int colon = inner.IndexOf(':');
        return colon < 0 ? (inner, "") : (inner[..colon].Trim(), inner[(colon + 1)..].Trim());
    }

    /// <summary>The chat window's line for a message (null: none shown).</summary>
    public static string? Shown(string message)
    {
        if (message.StartsWith("NOTIFY:", StringComparison.Ordinal)) return message[7..].Trim();

        bool npc = message.StartsWith("NPC_SAY: ", StringComparison.Ordinal);
        bool player = message.StartsWith("PLAYER_SAY: ", StringComparison.Ordinal);
        if (!npc && !player) return null;

        var (label, text) = Split(message[(npc ? 9 : 12)..]);
        return text.Length == 0 ? null : $"{label.Split('|')[0]}: {text}";
    }
}
