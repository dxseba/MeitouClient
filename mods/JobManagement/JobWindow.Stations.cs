using System.Globalization;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>The Stations tab: the category filters, the stations by group, and the picked station's workers (assigned and assignable).</summary>
public sealed partial class JobWindow
{
    int _stationScroll, _workerScroll;
    string? _station;

    UiNode StationTab(WorldSnapshot snapshot, float width, float height)
    {
        var tab = UiKit.Column("jm-stations", width, height, UiKit.Gap);
        tab.Children.Add(Filters());

        var stations = scanner.Scan(snapshot).Where(s => settings.Shows(s.Category)).ToList();
        float bodyHeight = height - UiKit.RowHeight - UiKit.Gap, left = MathF.Floor(width * 0.55f);
        var body = UiKit.Row("jm-stationbody", bodyHeight);
        body.Children.Add(StationList(stations, ById(snapshot), left, bodyHeight));
        body.Children.Add(StationDetail(snapshot, stations.FirstOrDefault(s => s.Site == _station), width - left - UiKit.Gap, bodyHeight));
        tab.Children.Add(body);
        return tab;
    }

    UiNode Filters()
    {
        var row = UiKit.Row("jm-filters");
        foreach (var category in Enum.GetValues<StationCategory>())
        {
            bool on = settings.Shows(category);
            row.Children.Add(Button($"filter.{category}", (on ? "[x] " : "[ ] ") + ShortName(category), 104, () => settings.Toggle(category),
                selected: on));
        }

        return row;
    }

    UiNode StationList(List<Station> stations, Dictionary<CharacterId, CharacterSnapshot> characters, float width, float height)
    {
        var column = UiKit.Column("jm-stationcol", width, height, UiKit.Gap);
        var lines = new List<UiNode>();
        foreach (var group in stations.GroupBy(s => s.Category))
        {
            lines.Add(Label($"group.{group.Key}", $"{CategoryName(group.Key)} ({group.Count()})", width, _styles!.Dim));
            foreach (var station in group) lines.Add(StationLine(station, characters, width));
        }

        if (lines.Count == 0) lines.Add(Label("nostations", "No stations in the shown groups.", width));
        float listHeight = height - UiKit.RowHeight - UiKit.Gap;
        int visible = UiKit.Fit(listHeight);
        var list = UiKit.Column("jm-stationlist", width, listHeight);
        list.Children.AddRange(UiKit.Page(lines, ref _stationScroll, visible));
        column.Children.Add(list);
        column.Children.Add(ScrollBar("stations", lines.Count, visible, _stationScroll, s => _stationScroll = s, width));
        return column;
    }

    UiNode StationLine(Station station, Dictionary<CharacterId, CharacterSnapshot> characters, float width)
    {
        int working = station.Assigned.Count(characters.ContainsKey);
        string count = $"{working.ToString(CultureInfo.InvariantCulture)}/{station.MaxWorkers.ToString(CultureInfo.InvariantCulture)}";
        string blockers = station.Blockers.Length > 0 ? " | " + station.Blockers : "";
        string text = $"{station.Name} | {station.Area} | {count}{blockers}";
        string site = station.Site;
        return Button($"station.{site}", text, width, () => (_station, _workerScroll) = (site, 0), selected: site == _station);
    }

    /// <summary>The picked station: its facts, then who works it and who could, by its skill.</summary>
    UiNode StationDetail(WorldSnapshot snapshot, Station? station, float width, float height)
    {
        var column = UiKit.Column("jm-detail", width, height, UiKit.Gap);
        if (station is null)
        {
            column.Children.Add(Label("pickstation", "Click a station to see its workers.", width, _styles!.Dim));
            return column;
        }

        var lines = DetailLines(snapshot, station, width);
        float listHeight = height - UiKit.RowHeight - UiKit.Gap;
        int visible = UiKit.Fit(listHeight);
        var list = UiKit.Column("jm-workerlist", width, listHeight);
        list.Children.AddRange(UiKit.Page(lines, ref _workerScroll, visible));
        column.Children.Add(list);
        column.Children.Add(ScrollBar("workers", lines.Count, visible, _workerScroll, s => _workerScroll = s, width));
        return column;
    }

    List<UiNode> DetailLines(WorldSnapshot snapshot, Station station, float width)
    {
        var board = Board();
        var people = snapshot.Characters.Where(c => c.IsPlayer && !JobRules.IsAnimal(c) && c.Body is not { Dead: true }).ToList();
        var assigned = people.Where(c => station.Assigned.Contains(c.Id)).ToList();
        string skill = station.Skill == StatsEnumerated.None ? "none" : station.Skill.ToString();

        var lines = new List<UiNode>
        {
            Label("st.name", station.Name, width),
            Label("st.facts", $"{CategoryName(station.Category)} | {station.Area} | skill: {skill}", width, _styles!.Dim),
        };
        if (station.Blockers.Length > 0) lines.Add(Label("st.block", station.Blockers, width, _styles.Warn));
        if (station.Job is null) lines.Add(Label("st.nojob", "This station takes no worker job.", width, _styles.Dim));

        lines.Add(Label("st.assigned", $"Assigned ({assigned.Count}/{station.MaxWorkers})", width));
        lines.AddRange(Sorted(assigned, station, board).Select(c => WorkerLine(c, station, board, width, assigned: true)));
        lines.Add(Label("st.available", "Available", width));
        lines.AddRange(Sorted(people.Except(assigned), station, board).Select(c => WorkerLine(c, station, board, width, assigned: false)));
        return lines;
    }

    /// <summary>By the station's skill, highest first (lowest first on a training dummy), then by name.</summary>
    static IEnumerable<CharacterSnapshot> Sorted(IEnumerable<CharacterSnapshot> people, Station station, JobBoard board)
    {
        float Skill(CharacterSnapshot c) => board.Skill(c.Id, station.Skill) ?? 0;
        var sorted = station.LowestFirst ? people.OrderBy(Skill) : people.OrderByDescending(Skill);
        return sorted.ThenBy(c => c.Name, StringComparer.CurrentCulture);
    }

    UiNode WorkerLine(CharacterSnapshot person, Station station, JobBoard board, float width, bool assigned)
    {
        var id = person.Id;
        string key = $"{Key(id)}.{station.Site}";
        string skill = board.Skill(id, station.Skill) is { } value ? ((int)value).ToString(CultureInfo.InvariantCulture) : "-";

        var row = UiKit.Row($"jm-worker.{key}", UiKit.LineHeight);
        row.Children.Add(Label($"wskill.{key}", skill, 40, _styles!.Dim));
        row.Children.Add(Label($"wname.{key}", person.Name, Math.Max(60, width - 40 - 90 - 2 * UiKit.Gap)));
        string site = station.Site;
        if (assigned)
            row.Children.Add(Button($"unassign.{key}", "Remove", 90, () => Edit(id, (c, t) => JobPlanner.RemoveSite(id, c.Jobs, site, t))));
        else if (station.Job is { } job)
            row.Children.Add(Button($"assign.{key}", "Assign", 90, () => Run(_ => [new AddJob([id], job, site) { Tick = Tick }])));
        return row;
    }

    static string ShortName(StationCategory category) => category == StationCategory.Storage ? "Storage" : category.ToString();

    static string CategoryName(StationCategory category) => category switch
    {
        StationCategory.Storage => "Storage / Hauling",
        StationCategory.Other => "Other / Unclassified",
        _ => category.ToString(),
    };
}
