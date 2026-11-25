using System.Text.Json;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.SentientSands;

/// <summary>
/// The SSR hub (F8, the original's): tabs Characters (the Dialogue Library: <c>/characters</c>, <c>/history</c>, <c>/favorite</c>,
/// <c>/read_bio</c>), Journal (<c>/journal</c>, <c>/journal/read|add|save|delete</c>), Quests (<c>/quests</c>, <c>/quests/abandon</c>), Events
/// (<c>/events</c>, <c>/events/content</c>, the rumour editor <c>/read_rumor</c>, <c>/write_rumor</c>, <c>/keep_rumor</c>, <c>/delete_rumor</c>,
/// <c>/add_event</c>, <c>/delete_event</c>) and Server (the dashboard's address). A list on the left, the chosen row's text on the right, an editor
/// line and the tab's buttons below. Writes send back the campaign the list came with, as the original's windows do. Interface thread only.
/// </summary>
sealed partial class HubWindow(ModContext context, ServerBridge bridge, SentientSettings settings) : IUiExtension
{
    const float Width = 900, Height = 560, Line = 20, ListWidth = 300, Rows = 20;
    static readonly string[] Tabs = ["Characters", "Journal", "Quests", "Events", "Server"];

    readonly record struct Row(string Id, string Label, string Detail);

    readonly List<Row> _rows = [];
    List<string> _text = [];
    string _campaign = "", _selected = "", _title = "", _edit = "", _query = "", _status = "";
    string _rumorId = "";
    int _tab, _page = 1, _pages = 1, _textPage;
    bool _open;

    public string Id => "dxseba.sentientsands.hub";

    public void Toggle()
    {
        _open = !_open;
        _shownTab = -1;
    }

    #region Loading the tabs

    void Load()
    {
        (_selected, _text, _status) = ("", [], "");
        _rows.Clear();
        switch (_tab)
        {
            case 0: bridge.Request("/characters", new { sort = "latest" }, Characters); break;
            case 1: bridge.Request("/journal", new { query = _query, page = _page }, Journal); break;
            case 2: bridge.Request("/quests", new { }, Quests); break;
            case 3: bridge.Request("/events", new { }, Events); break;
            default: _text = [$"Dashboard: {settings.ServerUrl}", "Open it in your browser for the settings, campaigns and LLM providers."]; break;
        }
    }

    void Characters(JsonElement? json) =>
        Fill(json, "characters", c => new Row(Str(c, "sid"), (Bool(c, "fav") ? "* " : "") + Str(c, "name"), ""));

    void Journal(JsonElement? json)
    {
        Fill(json, "entries", e => new Row(Str(e, "id"), Str(e, "label"), ""));
        if (json is { } j) (_page, _pages) = (Int(j, "page", 1), Int(j, "pages", 1));
    }

    void Quests(JsonElement? json) => Fill(json, "quests", q => new Row(Str(q, "id"), Str(q, "label"), Str(q, "details")));

    void Events(JsonElement? json) => Fill(json, "events", e => new Row(Str(e, "id"), Str(e, "title"), Str(e, "rumor")));

    void Fill(JsonElement? json, string list, Func<JsonElement, Row> row)
    {
        _rows.Clear();
        if (json is not { } j)
        {
            _status = "The server does not answer.";
            return;
        }

        _campaign = Str(j, "campaign");
        if (j.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray()) _rows.Add(row(item));
    }

    #endregion

    #region Choosing a row

    void Choose(string id)
    {
        _selected = id;
        _textPage = 0;
        var row = _rows.FirstOrDefault(r => r.Id == id);
        switch (_tab)
        {
            case 0: bridge.Request("/history", new { npc = id }, History); break;
            case 1: bridge.Request("/journal/read", new { id }, ReadEntry); break;
            case 2: _text = Wrap(row.Detail); break;
            case 3:
                _rumorId = row.Detail;
                bridge.Request("/events/content", new { id }, j => _text = Wrap(j is { } x ? Str(x, "text") : "The event is gone."));
                break;
        }
    }

    void History(JsonElement? json)
    {
        if (json is not { } j) return;

        string head = $"{Str(j, "name")} - {Str(j, "race")}, {Str(j, "sex")}, {Str(j, "faction")} (from {Str(j, "origin")})\n"
            + $"Job: {Str(j, "job")}  Location: {Str(j, "location")}  Relation: {Str(j, "relation")}  Bio: {Str(j, "bio")}\n\n";
        _text = Wrap(head + Str(j, "text"));
    }

    void ReadEntry(JsonElement? json)
    {
        if (json is not { } j) return;

        (_title, _edit) = (Str(j, "title"), Str(j, "text"));
        _text = Wrap($"{Str(j, "time")}\n{_title}\n\n{_edit}");
    }

    #endregion

    #region Reading JSON

    internal static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v)
        ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Null ? "" : v.ToString()
        : "";

    static bool Bool(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    static int Int(JsonElement e, string key, int fallback) => e.TryGetProperty(key, out var v) && v.TryGetInt32(out int i) ? i : fallback;

    /// <summary>The text cut into lines of at most 80 characters at spaces.</summary>
    internal static List<string> Wrap(string text)
    {
        var lines = new List<string>();
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string rest = raw;
            while (rest.Length > 80)
            {
                int cut = rest.LastIndexOf(' ', 80);
                if (cut <= 0) cut = 80;
                lines.Add(rest[..cut]);
                rest = rest[cut..].TrimStart();
            }

            lines.Add(rest);
        }

        return lines;
    }

    #endregion
}
