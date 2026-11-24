using System.Globalization;
using Meitou.Game.Host.Input;
using InputKey = Meitou.Game.Host.Input.Key;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>
/// The window's keys while it is open (<see cref="UiRegistration.AddKeyInput"/>; closed, every key goes to the game): Escape closes it (a
/// pending Clear Queue prompt first), Tab steps to the next squad, Ctrl+C copies the picked jobs and Ctrl+V gives copies to the recipients
/// (<b>Verified</b>, the fork's README and decompiled DLL: its Escape, Ctrl+C / Ctrl+V polls and their messages).
/// </summary>
public sealed partial class JobWindow
{
    /// <summary>The jobs Ctrl+C copied, as they were picked.</summary>
    List<JobPick> _clipboard = [];

    /// <summary>The squad Tab stepped to (null: none yet, the current squad); and the groups Tab opened, closed again when it moves on.</summary>
    int? _tabSquad;
    readonly HashSet<int> _tabOpened = [];

    bool KeyInput(UiKeyInput input)
    {
        if (!_open || input.Kind != UiKeyInputKind.Down) return false;

        if (input.Cancel)
        {
            Escape();
            return true;
        }

        if (input.Key == InputKey.Tab && input.Modifiers == KeyModifiers.None)
        {
            NextSquad();
            return true;
        }

        if (input.Modifiers != KeyModifiers.Ctrl || input.Key is not (InputKey.C or InputKey.V)) return false;

        if (input.Key == InputKey.C) CopyPicks();
        else Paste();
        return true;
    }

    void Escape()
    {
        if (_confirmClear is not null) _confirmClear = null;
        else Close();
    }

    /// <summary>
    /// The fork's Tab: the next squad's group opens and the list goes to it; a group Tab opened closes again when it moves on. (The fork follows
    /// Kenshi's own Tab, which also makes it the game's current squad; no mod call selects a squad here, so the window steps on its own.)
    /// </summary>
    void NextSquad()
    {
        if (_snapshot is not { PlayerSquads.Count: > 0 } snapshot) return;

        var squads = snapshot.PlayerSquads;
        int from = _tabSquad ?? snapshot.ActiveSquad;
        int at = Enumerable.Range(0, squads.Count).FirstOrDefault(i => squads[i].Id == from, -1);
        int next = squads[(at + 1) % squads.Count].Id;

        if (_tabOpened.Remove(from)) _collapsed[from] = true;
        if (Collapsed(next, next == snapshot.ActiveSquad) && _tabOpened.Add(next)) _collapsed[next] = false;

        _tab = Tab.Squads;
        _tabSquad = next;
        _scrollToSquad = next;
    }

    /// <summary>Ctrl+C: the picked jobs into the window's clipboard (the fork's messages).</summary>
    void CopyPicks()
    {
        if (_picks.Count == 0)
        {
            Status = Tr("Select one or more jobs before copying.");
            return;
        }

        _clipboard = [.. _picks];
        Status = Fill(Tr("Copied jobs: {count}. Select recipients, then press Ctrl+V."), ("count", _clipboard.Count));
    }

    /// <summary>Ctrl+V: copies of the clipboard's jobs to every recipient, one that already holds such a job skipped (a duplicate).</summary>
    void Paste()
    {
        if (_clipboard.Count == 0)
        {
            Status = Tr("Copy one or more jobs before pasting.");
            return;
        }

        if (_recipients.Count == 0)
        {
            Status = Tr("Select at least one recipient before pasting.");
            return;
        }

        var clip = _clipboard.ToList();
        var recipients = _recipients.ToList();
        Run(s => PasteStep(s, clip, recipients));
    }

    IReadOnlyList<SimCommand> PasteStep(WorldSnapshot snapshot, List<JobPick> clip, List<CharacterId> recipients)
    {
        var adds = new List<SimCommand>();
        int skipped = 0;
        foreach (var receiver in recipients.Select(id => Find(snapshot, id)).OfType<CharacterSnapshot>())
        {
            var given = new List<JobPick>();
            foreach (var pick in clip.Where(p => p.Owner != receiver.Id))
            {
                var source = Find(snapshot, pick.Owner);
                int row = source?.Jobs.ToList().IndexOf(pick.Job) ?? -1;
                var add = row < 0 ? null : (Twice(given, pick, receiver) ?? JobPlanner.Give(source!, row, receiver, Tick)).Add;
                if (add is null)
                {
                    skipped++;
                    continue;
                }

                adds.Add(add);
                given.Add(pick);
            }
        }

        string skips = skipped > 0 ? " " + Fill(Tr("Skipped duplicate bundles: {count}."), ("count", skipped)) : "";
        Status = adds.Count > 0 ? Tr("Jobs pasted.") + skips : Tr("No jobs needed changes.") + skips;
        return adds;
    }

    /// <summary>A text through the fork's catalogues (the game's language), else as written.</summary>
    string Tr(string english) => tr(english);

    /// <summary>The fork's named placeholders (<c>{count}</c>) filled in a translated template.</summary>
    static string Fill(string template, params (string Name, object Value)[] values)
    {
        foreach (var (name, value) in values)
            template = template.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return template;
    }
}
