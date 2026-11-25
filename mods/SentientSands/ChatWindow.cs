using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SentientSands;

/// <summary>
/// The chat (the original's key, <c>\</c> by default): a window on the NPC nearest the selected character, a text box sent to the server's
/// <c>/chat</c> on Enter or Send, the conversation's lines as they come back, the barter table, the offer popup, Trade (the game's trade window)
/// and Close. Each frame the server's messages and answers are taken in, the game events reported and radiant talks started. Interface thread only.
/// </summary>
sealed partial class ChatWindow(ModContext context, ServerBridge bridge, SentientSettings settings) : IUiExtension
{
    const float Reach = 300;

    readonly List<string> _log = [];
    CharacterId _npc = CharacterId.None, _player = CharacterId.None;
    string _npcName = "", _text = "";
    bool _open;

    public string Id => "dxseba.sentientsands.chat";

    #region Opening

    /// <summary>The chat key: opens on the NPC nearest the first selected character (within 300), or closes.</summary>
    public void Toggle(Game.Host.Input.ModKeyContext key)
    {
        if (_open)
        {
            Close();
            return;
        }

        if (key.Selected.Count == 0 || Nearest(key.Snapshot, key.Selected[0]) is not { } npc) return;

        (_open, _npc, _player, _npcName, _text) = (true, npc.Id, key.Selected[0], npc.Name, "");
        _log.Clear();
        _deal.Clear();
        bridge.Listen();
        context.Enqueue(new ChatTalk(_npc, _player, Hold: true, Trade: false) { Tick = key.Tick });
    }

    static CharacterSnapshot? Nearest(WorldSnapshot snapshot, CharacterId from)
    {
        var me = snapshot.Characters.FirstOrDefault(c => c.Id == from);
        if (me is null) return null;

        float Far(CharacterSnapshot c) => System.Numerics.Vector3.Distance(c.Position, me.Position);
        return snapshot.Characters.Where(c => !c.IsPlayer && c.Body is { Dead: false } && Far(c) <= Reach).OrderBy(Far).FirstOrDefault();
    }

    void Close()
    {
        _open = false;
        _barter = false;
        context.Enqueue(new ChatTalk(_npc, _player, Hold: false, Trade: false) { Tick = context.Tick });
    }

    #endregion

    #region Each frame

    /// <summary>Each frame: the server's messages, the answers to requests, renames, and the radiant clock.</summary>
    public void Frame(ModFrame frame)
    {
        bridge.RunReplies();
        while (bridge.Incoming.TryDequeue(out string? message)) Take(message, frame.Tick);

        while (SentientSystem.Renamed.TryDequeue(out var renamed))
        {
            if (SentientSystem.Facts.Context(renamed) is { } ctx) bridge.Post("/squad_rename", System.Text.Json.JsonSerializer.Serialize(ctx));
        }

        Radiant(context.Interface.Paused);
    }

    void Take(string message, long tick)
    {
        if (Command(message)) return;

        if (ServerMessage.Parse(message, _player) is { } command) context.Enqueue(command with { Tick = tick });
        if (ServerMessage.Shown(message) is { } shown) Add(shown);
    }

    /// <summary>The messages the interface answers itself: offers, barter turns, settings and the report.</summary>
    bool Command(string message)
    {
        if (message.StartsWith("CMD: OFFER: ", StringComparison.Ordinal)) Offer(message[12..]);
        else if (message.StartsWith("CMD: OFFER_GONE", StringComparison.Ordinal)) _offer = null;
        else if (message.StartsWith("CMD: BARTER_TURN:", StringComparison.Ordinal)) BarterTurn(message[17..]);
        else if (message.StartsWith("SET_CONFIG: ", StringComparison.Ordinal)) Config(message[12..]);
        else if (message.StartsWith("REPORT:", StringComparison.Ordinal)) Report("/report");
        else return message.StartsWith("CMD:", StringComparison.Ordinal) || message.StartsWith("APPLY_TRANSLATION:", StringComparison.Ordinal);

        return true;
    }

    void Add(string line)
    {
        _log.Add(line);
        if (_log.Count > 60) _log.RemoveAt(0);
    }

    #endregion

    #region Requests

    /// <summary>The game events since the last request (the plugin sends them with every request).</summary>
    static List<Dictionary<string, object?>> Events()
    {
        var events = new List<Dictionary<string, object?>>();
        while (SentientSystem.Events.TryDequeue(out var e)) events.Add(e);
        return events;
    }

    /// <summary>The report's parts every request carries: the player's context, the events and the town lists.</summary>
    Dictionary<string, object?> Report(Dictionary<string, object?> body, string playerKey)
    {
        var facts = SentientSystem.Facts;
        body[playerKey] = facts.Context(facts.Lead);
        body["events"] = Events();
        body["changed_towns"] = facts.ChangedTowns;
        body["unique_states"] = null;
        body["player_towns"] = facts.PlayerTowns;
        return body;
    }

    void Report(string path) => bridge.Request(path, Report([], "player"), null);

    void Send()
    {
        string text = _text.Trim();
        if (text.Length == 0 || _npc.IsNone) return;

        var facts = SentientSystem.Facts;
        string npcLabel = $"{_npcName}|{ServerMessage.Key(_npc)}";
        var body = new Dictionary<string, object?>
        {
            ["npc"] = npcLabel, ["npcs"] = new[] { npcLabel }, ["player"] = facts.Of(_player)?.Name ?? "Drifter", ["message"] = text,
            ["mode"] = "talk", ["context"] = facts.Context(_npc), ["speaker_key"] = ServerMessage.Key(_player),
            ["nearby"] = facts.Characters.Values.Where(c => c.Id != _npc
                && c.Id != _player).Take(12).Select(c => c.Context(facts.Day, facts.Hour, facts.Minute)),
        };
        Report(body, "speaker");
        body["speaker"] = facts.Context(_player);
        if (_barter) body["barter"] = BarterWindow(facts);

        bridge.Request("/chat", body, null);
        _text = "";
    }

    #endregion
}
