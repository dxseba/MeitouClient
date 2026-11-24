using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Player;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>
/// The Squad Jobs tab: every squad as a collapsible group of its members' job lists in priority order, the batch buttons and the pick.
/// </summary>
public sealed partial class JobWindow
{
    int _scroll;

    /// <summary>
    /// The picked jobs, in pick order: a click picks one job (again: none), a Ctrl+click adds or drops one (the original's multi-select).
    /// </summary>
    readonly List<JobPick> _picks = [];

    /// <summary>
    /// Each squad group's collapsed state, by squad id, as the player set it; a squad not in it is open only while it is the game's current squad.
    /// </summary>
    readonly Dictionary<int, bool> _collapsed = [];

    /// <summary>A squad to scroll to on the next layout (a plain click on its selector button); null: none.</summary>
    int? _scrollToSquad;

    /// <summary>
    /// The member whose Clear Queue waits for Yes or No, if any.
    /// </summary>
    CharacterId? _confirmClear;

    /// <summary>Opens with the current squad's group open and the others closed, nothing picked.</summary>
    void OpenSquads()
    {
        _collapsed.Clear();
        _recipients.Clear();
        _pickedSquads.Clear();
        _scroll = 0;
    }

    /// <summary>
    /// The original's Squad Jobs view: every player squad as a collapsible group (its members' lists when open), the batch buttons over it and the
    /// squad selector under it.
    /// </summary>
    UiNode SquadTab(WorldSnapshot snapshot, float width, float height)
    {
        var tab = UiKit.Column("jm-squads", width, height, UiKit.Gap);
        _drops.Clear();
        var squads = snapshot.PlayerSquads;
        if (squads.Count == 0)
        {
            tab.Children.Add(Label("nosquad", "No active squad is available.", width));
            return tab;
        }

        var characters = ById(snapshot);
        CheckPick(characters);
        CheckRecipients(characters);

        tab.Children.Add(SquadBar(width));
        float listHeight = height - 3 * (UiKit.RowHeight + UiKit.Gap);
        var lines = SquadLines(snapshot, characters, width);
        int visible = UiKit.Fit(listHeight);
        var list = UiKit.List("jm-joblist", width, listHeight, _wheelAction, "jobs");
        list.Children.AddRange(UiKit.Page(lines, ref _scroll, visible));
        tab.Children.Add(list);
        tab.Children.Add(ScrollBar("jobs", lines.Count, visible, _scroll, s => _scroll = s, width));
        tab.Children.Add(SquadSelector(snapshot, characters, width));
        return tab;
    }

    /// <summary>Each squad's group header and, while it is open, its members' lines; scrolls to a squad the selector asked for.</summary>
    List<UiNode> SquadLines(WorldSnapshot snapshot, Dictionary<CharacterId, CharacterSnapshot> characters, float width)
    {
        var sites = BuildingNames(snapshot);
        var lines = new List<UiNode>();
        foreach (var squad in snapshot.PlayerSquads)
        {
            if (_scrollToSquad == squad.Id) (_scroll, _scrollToSquad) = (lines.Count, null);

            bool current = squad.Id == snapshot.ActiveSquad, collapsed = Collapsed(squad.Id, current);
            var members = Members(squad, characters);
            lines.Add(SquadHeader(squad, members.Count, current, collapsed, width));
            if (!collapsed) lines.AddRange(JobLines(members, sites, characters, width));
        }

        return lines;
    }

    bool Collapsed(int squad, bool current) => _collapsed.TryGetValue(squad, out bool collapsed) ? collapsed : !current;

    /// <summary>The squad's player members in the snapshot, in squad order.</summary>
    static List<CharacterSnapshot> Members(SquadView squad, Dictionary<CharacterId, CharacterSnapshot> characters) =>
        [.. squad.Members.Select(characters.GetValueOrDefault).OfType<CharacterSnapshot>().Where(c => c.IsPlayer)];

    /// <summary>
    /// The original's group header, "[-]  {squad}  |  Members: {n}" ([+] while closed): a click opens or closes it; the game's current squad is
    /// marked and captioned CURRENT SQUAD.
    /// </summary>
    UiNode SquadHeader(SquadView squad, int members, bool current, bool collapsed, float width)
    {
        int id = squad.Id;
        string name = squad.Name is { Length: > 0 } n ? n : "Squad";
        string text = $"{(collapsed ? "[+]" : "[-]")}  {name}  |  Members: {members.ToString(CultureInfo.InvariantCulture)}";
        var row = UiKit.Row($"jm-squadhead.{id}", UiKit.LineHeight);
        float caption = current ? 130 : 0;
        row.Children.Add(Button($"squadgroup.{id}", text, width - caption - (current ? UiKit.Gap : 0), () => _collapsed[id] = !collapsed,
            selected: current));
        if (current) row.Children.Add(Label($"current.{id}", "CURRENT SQUAD", caption, _styles!.Dim));
        return row;
    }

    /// <summary>The batch buttons, which act on the recipients, then the pick's line (or DROP TO REMOVE while a drag is on).</summary>
    UiNode SquadBar(float width)
    {
        var row = UiKit.Row("jm-squadbar");
        row.Children.Add(Button("heal.add", "Add Medic/Robotics", 150, () => ForRecipients(AddHealing)));
        row.Children.Add(Button("heal.order", "Prioritize Healing", 140, () => ForRecipients(PrioritizeHealing)));
        row.Children.Add(Button("invalid", "Remove Invalid Jobs", 160, () => ForRecipients(RemoveInvalid)));

        float rest = Math.Max(40, width - 450 - 3 * UiKit.Gap);
        if (_drag is not null)
        {
            row.Children.Add(DropToRemove(rest));
            return row;
        }

        if (_picks.Count == 0)
        {
            row.Children.Add(Label("pick", RecipientHint(), rest, _styles!.Dim));
            return row;
        }

        string picked = _picks.Count == 1 ? $"Picked {JobRules.Name(_picks[0].Job.Type)}" : $"Picked {_picks.Count} jobs";
        row.Children.Add(Label("pick", $"{picked}: Move here, Copy here or drag", Math.Max(40, rest - 110 - UiKit.Gap), _styles!.Dim));
        row.Children.Add(Button("removepicked", "Remove Job(s)", 110, RemovePicked));
        return row;
    }

    /// <summary>
    /// Each member's header line, then its rows; a companion row is shown under its job and moves with it. Every line is a drop target for its
    /// member (a job line also for its row: a drag within one list puts the job there).
    /// </summary>
    List<UiNode> JobLines(List<CharacterSnapshot> members, Dictionary<string, string> sites, Dictionary<CharacterId, CharacterSnapshot> characters,
        float width)
    {
        var invalid = Board().Invalid;
        var lines = new List<UiNode>();
        foreach (var member in members)
        {
            lines.Add(DropOn(MemberLine(member, width), member.Id, -1));
            foreach (var unit in JobRules.Units(member.Jobs))
            {
                lines.Add(DropOn(JobLine(member, unit, sites, characters, invalid, width), member.Id, unit.Start));
                for (int row = unit.Start + 1; row < unit.End; row++)
                {
                    string text = "        + " + JobText(member.Jobs[row], sites, characters);
                    lines.Add(DropOn(Label($"row.{Key(member.Id)}.{row}", text, width - 40, _styles!.Dim), member.Id, unit.Start));
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// A member's header: its name (a click makes it the recipient, Ctrl+click adds or drops it: the original's portrait click), Move / Copy here
    /// while jobs of others are picked, the Jobs switch and Clear Queue.
    /// </summary>
    UiNode MemberLine(CharacterSnapshot member, float width)
    {
        var id = member.Id;
        var row = UiKit.Row($"jm-member.{Key(id)}", UiKit.LineHeight, pickable: true);
        string animal = JobRules.IsAnimal(member) ? " (animal)" : "";
        string text = $"{member.Name}{animal} - {member.Jobs.Count} jobs";
        row.Children.Add(Button($"name.{Key(id)}", text, 300, () => ClickMember(id), selected: _recipients.Contains(id)));
        if (_picks.Count > 0 && _picks.All(p => p.Owner != id))
        {
            var picks = _picks.ToList();
            row.Children.Add(Button($"move.{Key(id)}", "Move here", 90, () => GiveTo(picks, id, copy: false)));
            row.Children.Add(Button($"copy.{Key(id)}", "Copy here", 90, () => GiveTo(picks, id, copy: true)));
        }

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
        var mine = new JobPick(id, at, job);
        bool picked = _picks.Contains(mine);

        var row = UiKit.Row($"jm-job.{key}", UiKit.LineHeight, pickable: true);
        row.Children.Add(Label($"num.{key}", (at + 1).ToString(CultureInfo.InvariantCulture) + ".", 40, _styles!.Dim));
        float text = 440;
        if (icons.Of(job.Type) is { } icon)
        {
            row.Children.Add(UiKit.Icon($"jm-jobicon.{key}", icon));
            text -= UiKit.LineHeight + UiKit.Gap;
        }

        row.Children.Add(DragButton($"pick.{key}", JobText(job, sites, characters), text, new JobPick(id, at, job),
            () => Pick(mine, picked), picked));
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

    /// <summary>Plans an edit of one character's list on the snapshot of its step.</summary>
    void Edit(CharacterId id, Func<CharacterSnapshot, long, IReadOnlyList<SimCommand>> plan) =>
        Run(s => Find(s, id) is { } c ? plan(c, Tick) : []);

    /// <summary>As <see cref="Edit"/>, only while row <paramref name="row"/> still holds <paramref name="job"/>.</summary>
    void EditRow(CharacterId id, int row, PermaJob job, Func<CharacterSnapshot, long, IReadOnlyList<SimCommand>> plan) =>
        Edit(id, (c, t) => row < c.Jobs.Count && c.Jobs[row] == job ? plan(c, t) : []);
}
