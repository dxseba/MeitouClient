using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Player;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>
/// The Squad Jobs tab: one squad's members with their job lists in priority order, the batch buttons and the pick for move and copy.
/// </summary>
public sealed partial class JobWindow
{
    int _squad, _scroll;
    JobPick? _pick;

    /// <summary>
    /// The member whose Clear Queue waits for Yes or No, if any.
    /// </summary>
    CharacterId? _confirmClear;

    /// <summary>Starts on the active squad.</summary>
    void OpenSquads(WorldSnapshot snapshot)
    {
        int active = snapshot.PlayerSquads.ToList().FindIndex(s => s.Id == snapshot.ActiveSquad);
        _squad = Math.Max(0, active);
        _scroll = 0;
    }

    UiNode SquadTab(WorldSnapshot snapshot, float width, float height)
    {
        var tab = UiKit.Column("jm-squads", width, height, UiKit.Gap);
        var squads = snapshot.PlayerSquads;
        if (squads.Count == 0)
        {
            tab.Children.Add(Label("nosquad", "No squads.", width));
            return tab;
        }

        _squad = (_squad % squads.Count + squads.Count) % squads.Count;
        var characters = ById(snapshot);
        var members = squads[_squad].Members.Select(characters.GetValueOrDefault).OfType<CharacterSnapshot>().Where(c => c.IsPlayer).ToList();
        CheckPick(characters);

        tab.Children.Add(SquadBar(squads[_squad], squads.Count, members, width));
        float listHeight = height - 2 * (UiKit.RowHeight + UiKit.Gap);
        var lines = JobLines(members, BuildingNames(snapshot), characters, width);
        int visible = UiKit.Fit(listHeight);
        var list = UiKit.Column("jm-joblist", width, listHeight);
        list.Children.AddRange(UiKit.Page(lines, ref _scroll, visible));
        tab.Children.Add(list);
        tab.Children.Add(ScrollBar("jobs", lines.Count, visible, _scroll, s => _scroll = s, width));
        return tab;
    }

    /// <summary>The squad switch and the batch buttons, which act on the squad's members.</summary>
    UiNode SquadBar(SquadView squad, int count, List<CharacterSnapshot> members, float width)
    {
        var row = UiKit.Row("jm-squadbar");
        row.Children.Add(Button("squad.prev", "<", 28, () => (_squad, _scroll) = (_squad - 1, 0), count > 1));
        row.Children.Add(Label("squad", $"{squad.Name} ({_squad + 1}/{count})", 210));
        row.Children.Add(Button("squad.next", ">", 28, () => (_squad, _scroll) = (_squad + 1, 0), count > 1));

        var ids = members.Select(m => m.Id).ToList();
        row.Children.Add(Button("heal.add", "Add Medic/Robotics", 150, () => AddHealing(ids)));
        row.Children.Add(Button("heal.order", "Prioritize Healing", 140, () => PrioritizeHealing(ids)));
        row.Children.Add(Button("invalid", "Remove Invalid Jobs", 160, () => RemoveInvalid(ids)));

        string pick = _pick is { } p ? $"Picked {JobRules.Name(p.Job.Type)}: Move here or Copy here" : "Click a job to pick it.";
        row.Children.Add(Label("pick", pick, Math.Max(40, width - 716 - 6 * UiKit.Gap), _styles!.Dim));
        return row;
    }

    /// <summary>Each member's header line, then its rows; a companion row is shown under its job and moves with it.</summary>
    List<UiNode> JobLines(List<CharacterSnapshot> members, Dictionary<string, string> sites, Dictionary<CharacterId, CharacterSnapshot> characters,
        float width)
    {
        var invalid = Board().Invalid;
        var lines = new List<UiNode>();
        foreach (var member in members)
        {
            lines.Add(MemberLine(member, width));
            foreach (var unit in JobRules.Units(member.Jobs))
            {
                lines.Add(JobLine(member, unit, sites, characters, invalid, width));
                for (int row = unit.Start + 1; row < unit.End; row++)
                {
                    string text = "        + " + JobText(member.Jobs[row], sites, characters);
                    lines.Add(Label($"row.{Key(member.Id)}.{row}", text, width - 40, _styles!.Dim));
                }
            }
        }

        return lines;
    }

    UiNode MemberLine(CharacterSnapshot member, float width)
    {
        var row = UiKit.Row($"jm-member.{Key(member.Id)}", UiKit.LineHeight);
        string animal = JobRules.IsAnimal(member) ? " (animal)" : "";
        row.Children.Add(Label($"name.{Key(member.Id)}", $"{member.Name}{animal} - {member.Jobs.Count} jobs", 300));
        if (_pick is { } pick && pick.Owner != member.Id)
        {
            row.Children.Add(Button($"move.{Key(member.Id)}", "Move here", 90, () => GiveTo(pick, member.Id, copy: false)));
            row.Children.Add(Button($"copy.{Key(member.Id)}", "Copy here", 90, () => GiveTo(pick, member.Id, copy: true)));
        }

        var id = member.Id;
        bool on = (member.Orders & CharacterOrders.Jobs) != 0;
        row.Children.Add(Button($"jobs.{Key(id)}", on ? "Jobs: ON" : "Jobs: OFF", 80, () => ToggleJobs(id, !on)));
        AddClear(row, member);
        return row;
    }

    /// <summary>
    /// The original's per-member Jobs switch (the orders panel's Jobs toggle for that one character).
    /// </summary>
    void ToggleJobs(CharacterId id, bool on) => Run(_ => [new OrdersCommand([id], CharacterOrders.Jobs, on) { Tick = Tick }]);

    /// <summary>
    /// Clear Queue, which asks first as the original's "Confirm Clear Queue" prompt: Yes removes every job, No keeps them.
    /// </summary>
    void AddClear(UiNode row, CharacterSnapshot member)
    {
        var id = member.Id;
        if (_confirmClear != id)
        {
            row.Children.Add(Button($"clear.{Key(id)}", "Clear Queue", 90, () => _confirmClear = id, member.Jobs.Count > 0));
            return;
        }

        row.Children.Add(Label($"clearask.{Key(id)}", $"Remove all {member.Jobs.Count} permanent jobs from {member.Name}?", 330, _styles!.Warn));
        row.Children.Add(Button($"clearyes.{Key(id)}", "Yes", 44, () => ConfirmClear(id)));
        row.Children.Add(Button($"clearno.{Key(id)}", "No", 44, () => _confirmClear = null));
    }

    void ConfirmClear(CharacterId id)
    {
        _confirmClear = null;
        Edit(id, (c, t) => JobPlanner.Clear(id, c.Jobs, t));
    }

    UiNode JobLine(CharacterSnapshot member, JobUnit unit, Dictionary<string, string> sites, Dictionary<CharacterId, CharacterSnapshot> characters,
        IReadOnlySet<(CharacterId, PermaJob)> invalid, float width)
    {
        var id = member.Id;
        int at = unit.Start;
        var job = member.Jobs[at];
        string key = $"{Key(id)}.{at}.{(int)job.Type}";
        bool picked = _pick is { } p && p.Owner == id && p.Row == at;

        var row = UiKit.Row($"jm-job.{key}", UiKit.LineHeight);
        row.Children.Add(Label($"num.{key}", (at + 1).ToString(CultureInfo.InvariantCulture) + ".", 40, _styles!.Dim));
        row.Children.Add(Button($"pick.{key}", JobText(job, sites, characters), 440, () => _pick = picked ? null : new JobPick(id, at, job),
            selected: picked));
        row.Children.Add(Button($"up.{key}", "Up", 44, () => EditRow(id, at, job, (c, t) => JobPlanner.Shift(id, c.Jobs, at, -1, t))));
        row.Children.Add(Button($"down.{key}", "Down", 54, () => EditRow(id, at, job, (c, t) => JobPlanner.Shift(id, c.Jobs, at, 1, t))));
        row.Children.Add(Button($"remove.{key}", "X", 30, () => EditRow(id, at, job, (c, t) => JobPlanner.Remove(id, c.Jobs, at, t))));
        if (invalid.Contains((id, job))) row.Children.Add(Label($"bad.{key}", "target gone", 110, _styles.Warn));
        return row;
    }

    /// <summary>A row's text: the job and its target (a building's name, a followed character's).</summary>
    static string JobText(PermaJob job, Dictionary<string, string> sites, Dictionary<CharacterId, CharacterSnapshot> characters)
    {
        string name = JobRules.Name(job.Type);
        if (job.Site is { } site)
        {
            string target = sites.GetValueOrDefault(site) ?? (ProductionSystem.IsNatural(site) ? "resource node"
                : ProductionSystem.IsTownWork(site) ? "town building" : site);
            return $"{name}: {target}";
        }

        if (!job.Character.IsNone) return $"{name}: {characters.GetValueOrDefault(job.Character)?.Name ?? "(gone)"}";
        return name;
    }

    static Dictionary<string, string> BuildingNames(WorldSnapshot snapshot)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in snapshot.Buildings) names[b.Id] = b.Name is { Length: > 0 } n ? n : b.Record;
        return names;
    }

    static string Key(CharacterId id) => $"{id.Slot}_{id.Generation}";

    /// <summary>Keeps the pick on its row while the list changes under it, or drops it.</summary>
    void CheckPick(Dictionary<CharacterId, CharacterSnapshot> characters)
    {
        if (_pick is not { } pick) return;

        var jobs = characters.GetValueOrDefault(pick.Owner)?.Jobs;
        if (jobs is not null && pick.Row < jobs.Count && jobs[pick.Row] == pick.Job) return;

        int row = jobs?.ToList().IndexOf(pick.Job) ?? -1;
        _pick = row >= 0 ? pick with { Row = row } : null;
    }

    /// <summary>Plans an edit of one character's list on the snapshot of its step.</summary>
    void Edit(CharacterId id, Func<CharacterSnapshot, long, IReadOnlyList<SimCommand>> plan) =>
        Run(s => Find(s, id) is { } c ? plan(c, Tick) : []);

    /// <summary>As <see cref="Edit"/>, only while row <paramref name="row"/> still holds <paramref name="job"/>.</summary>
    void EditRow(CharacterId id, int row, PermaJob job, Func<CharacterSnapshot, long, IReadOnlyList<SimCommand>> plan) =>
        Edit(id, (c, t) => row < c.Jobs.Count && c.Jobs[row] == job ? plan(c, t) : []);
}
