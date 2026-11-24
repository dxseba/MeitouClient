using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>
/// The original's recipients: the members the batch buttons act on. Picking jobs makes their owners the recipients; a member's name (the
/// original's portrait) or a squad's selector button picks them directly, Ctrl+click for more than one.
/// </summary>
public sealed partial class JobWindow
{
    /// <summary>A squad selector button's width.</summary>
    const float SelectorButton = 120;

    readonly List<CharacterId> _recipients = [];

    /// <summary>The squads picked on the selector, in pick order; their groups are the open ones.</summary>
    readonly List<int> _pickedSquads = [];

    int _selectorStart;

    void ClearSelection()
    {
        _picks.Clear();
        _recipients.Clear();
        _pickedSquads.Clear();
        _drag = null;
    }

    /// <summary>A click on a job: alone it picks just that job (or none, when it was the only one); with Ctrl it adds or drops it.</summary>
    void Pick(JobPick job, bool picked)
    {
        if (_ctrl)
        {
            if (!_picks.Remove(job)) _picks.Add(job);
        }
        else
        {
            bool only = picked && _picks.Count == 1;
            _picks.Clear();
            if (!only) _picks.Add(job);
        }

        RecipientsFromPicks();
    }

    /// <summary>The picked jobs' owners become the recipients, as the original's.</summary>
    void RecipientsFromPicks()
    {
        _recipients.Clear();
        _recipients.AddRange(_picks.Select(p => p.Owner).Distinct());
    }

    /// <summary>Keeps each pick on its row while the list changes under it, or drops it.</summary>
    void CheckPick(Dictionary<CharacterId, CharacterSnapshot> characters)
    {
        for (int i = _picks.Count - 1; i >= 0; i--)
        {
            var pick = _picks[i];
            var jobs = characters.GetValueOrDefault(pick.Owner)?.Jobs;
            if (jobs is not null && pick.Row < jobs.Count && jobs[pick.Row] == pick.Job) continue;

            int row = jobs?.ToList().IndexOf(pick.Job) ?? -1;
            if (row >= 0) _picks[i] = pick with { Row = row };
            else _picks.RemoveAt(i);
        }
    }

    /// <summary>Drops recipients that are gone or no longer the player's.</summary>
    void CheckRecipients(Dictionary<CharacterId, CharacterSnapshot> characters) =>
        _recipients.RemoveAll(id => characters.GetValueOrDefault(id) is not { IsPlayer: true });

    /// <summary>A member's name: alone it makes the member the only recipient; Ctrl adds or drops it. Picked jobs and squads are let go.</summary>
    void ClickMember(CharacterId id)
    {
        _picks.Clear();
        _pickedSquads.Clear();
        if (_ctrl)
        {
            if (!_recipients.Remove(id)) _recipients.Add(id);
            return;
        }

        _recipients.Clear();
        _recipients.Add(id);
    }

    /// <summary>Runs a batch button on the recipients, or says to pick some first (the original's message).</summary>
    void ForRecipients(Action<IReadOnlyList<CharacterId>> batch)
    {
        if (_recipients.Count == 0)
        {
            Status = "Select at least one recipient first.";
            return;
        }

        batch(_recipients.ToList());
    }

    string RecipientHint() => _recipients.Count switch
    {
        0 => "Pick recipients: a name or a squad below (Ctrl+click: more), or jobs.",
        1 => "1 recipient picked.",
        int n => $"{n} recipients picked.",
    };

    /// <summary>The original's squad selector under the list: a button per squad, picked ones marked; the arrows page when they do not fit.</summary>
    UiNode SquadSelector(WorldSnapshot snapshot, Dictionary<CharacterId, CharacterSnapshot> characters, float width)
    {
        var squads = snapshot.PlayerSquads;
        int fit = Math.Max(1, (int)((width - 70 - 2 * 28 - 3 * UiKit.Gap) / (SelectorButton + UiKit.Gap)));
        _selectorStart = Math.Clamp(_selectorStart, 0, Math.Max(0, squads.Count - fit));

        var row = UiKit.Row("jm-selector");
        row.Children.Add(Label("selector.label", "SQUADS", 70, _styles!.Dim));
        row.Children.Add(Button("selector.prev", "<", 28, () => _selectorStart -= fit, _selectorStart > 0));
        foreach (var squad in squads.Skip(_selectorStart).Take(fit))
        {
            var members = Members(squad, characters).Select(c => c.Id).ToList();
            int id = squad.Id, active = snapshot.ActiveSquad;
            string name = squad.Name is { Length: > 0 } n ? n : "Squad";
            row.Children.Add(Button($"selector.{id}", name, SelectorButton, () => ClickSquad(id, members, active),
                selected: _pickedSquads.Contains(id)));
        }

        row.Children.Add(Button("selector.next", ">", 28, () => _selectorStart += fit, _selectorStart + fit < squads.Count));
        return row;
    }

    /// <summary>
    /// A squad's selector button: alone its members become the recipients and it the only picked squad (scrolled to); with Ctrl it is added (all
    /// its members) or, when every member already is a recipient, dropped. The picked squads' groups open and the others close.
    /// </summary>
    void ClickSquad(int squad, List<CharacterId> members, int active)
    {
        _picks.Clear();
        if (_ctrl) ToggleSquad(squad, members);
        else
        {
            _recipients.Clear();
            _recipients.AddRange(members);
            _pickedSquads.Clear();
            _pickedSquads.Add(squad);
            _scrollToSquad = squad;
        }

        OpenPickedSquads(active);
    }

    void ToggleSquad(int squad, List<CharacterId> members)
    {
        bool all = members.Count > 0 && members.All(_recipients.Contains);
        if (all)
        {
            _recipients.RemoveAll(members.Contains);
            _pickedSquads.Remove(squad);
            return;
        }

        _recipients.AddRange(members.Where(m => !_recipients.Contains(m)));
        if (!_pickedSquads.Contains(squad)) _pickedSquads.Add(squad);
    }

    /// <summary>The picked squads' groups open, the others close; none picked: back to the current squad's alone.</summary>
    void OpenPickedSquads(int active)
    {
        if (_snapshot is not { } snapshot) return;

        foreach (var squad in snapshot.PlayerSquads)
            _collapsed[squad.Id] = _pickedSquads.Count > 0 ? !_pickedSquads.Contains(squad.Id) : squad.Id != active;
    }
}
