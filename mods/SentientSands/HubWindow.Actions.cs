using System.Text.Json;

namespace Meitou.SentientSands;

/// <summary>The hub's buttons, per tab (the original's window buttons and their routes).</summary>
sealed partial class HubWindow
{
    /// <summary>The buttons of the open tab: caption and what it does.</summary>
    IEnumerable<(string Caption, Action Run)> Buttons() => _tab switch
    {
        0 => [("Favorite", Favorite), ("Bio", ReadBio)],
        1 => [("New entry", AddEntry), ("Save", SaveEntry), ("Delete", DeleteEntry), ("Search", Search)],
        2 => [("Abandon", Abandon)],
        3 => [("Edit rumor", ReadRumor), ("Generate rumor", WriteRumor), ("Keep rumor", KeepRumor), ("Delete rumor", DeleteRumor),
            ("Add event", AddEvent), ("Delete event", DeleteEvent)],
        _ => [],
    };

    bool Chosen => _selected.Length > 0;

    void Done(JsonElement? json, string ok)
    {
        _status = json is { } j && Str(j, "status") == "ok" ? ok : json is { } e ? Str(e, "message") : "The server does not answer.";
        Load();
    }

    void Favorite()
    {
        if (Chosen) bridge.Request("/favorite", new { sid = _selected }, j => Done(j, "Favourites changed."));
    }

    void ReadBio()
    {
        if (!Chosen) return;

        bridge.Request("/read_bio", new { sid = _selected }, j =>
        {
            if (j is { } x && x.TryGetProperty("bio", out var bio) && bio.ValueKind == JsonValueKind.Object)
                _text = Wrap(string.Join("\n\n", bio.EnumerateObject().Select(p => $"{p.Name.ToUpperInvariant()}\n{p.Value}")));
        });
    }

    void AddEntry()
    {
        var f = SentientSystem.Facts;
        bridge.Request("/journal/add", new { campaign = _campaign, day = f.Day, hour = f.Hour, minute = f.Minute }, j => Done(j, "Entry added."));
    }

    void SaveEntry()
    {
        if (Chosen) bridge.Request("/journal/save", new { campaign = _campaign, id = _selected, title = _title, text = _edit }, j => Done(j,
            "Saved."));
    }

    void DeleteEntry()
    {
        if (Chosen) bridge.Request("/journal/delete", new { campaign = _campaign, id = _selected }, j => Done(j, "Deleted."));
    }

    void Search()
    {
        _query = _edit;
        _page = 1;
        Load();
    }

    void Abandon()
    {
        if (Chosen) bridge.Request("/quests/abandon", new { campaign = _campaign, id = _selected }, j => Done(j, "Job abandoned."));
    }

    void ReadRumor()
    {
        if (Chosen) bridge.Request("/read_rumor", new { @event = _selected }, Rumor);
    }

    void WriteRumor()
    {
        if (!Chosen) return;

        _status = "Writing...";
        bridge.Request("/write_rumor", new { @event = _selected, instruction = _edit }, Rumor);
    }

    void Rumor(JsonElement? json)
    {
        if (json is not { } j) return;

        if (Str(j, "status") != "ok")
        {
            _status = Str(j, "message");
            return;
        }

        (_edit, _status) = (Str(j, "text"), "The rumour is in the editor: change it and Keep it.");
        if (Str(j, "campaign") is { Length: > 0 } c) _campaign = c;
    }

    void KeepRumor()
    {
        if (!Chosen) return;

        object? id = _rumorId.Length > 0 ? _rumorId : null;
        bridge.Request("/keep_rumor", new { campaign = _campaign, id, @event = _selected, text = _edit }, j => Done(j, "Rumour kept."));
    }

    void DeleteRumor()
    {
        if (Chosen && _rumorId.Length > 0) bridge.Request("/delete_rumor", new { campaign = _campaign, id = _rumorId }, j => Done(j,
            "Rumour deleted."));
    }

    void AddEvent() => bridge.Request("/add_event", new { campaign = _campaign, rumor = _edit }, j => Done(j, "Event added."));

    void DeleteEvent()
    {
        if (Chosen) bridge.Request("/delete_event", new { campaign = _campaign, id = _selected }, j => Done(j, "Event deleted."));
    }
}
