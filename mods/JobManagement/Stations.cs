using Meitou.Data;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Construction;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>
/// A work station of the Stations tab: its site, name, area, group, the job a worker gets there (null: none), the skill that sorts its workers,
/// who has a job on it, how many may work it and what stops the work.
/// </summary>
public sealed record Station(string Site, string Name, string Area, StationCategory Category, TaskType? Job, StatsEnumerated Skill,
    IReadOnlyList<CharacterId> Assigned, int MaxWorkers, string Blockers)
{
    /// <summary>Training benches sort their workers lowest skill first (the original's rule).</summary>
    public bool LowestFirst => Category == StationCategory.Training;
}

/// <summary>Builds the stations from the snapshot's machines (complete player buildings) and the BUILDING records.</summary>
public sealed class StationScanner(GameDatabase db)
{
    readonly Dictionary<string, MachineData?> _data = new(StringComparer.Ordinal);

    /// <summary>The stations of the player's complete buildings, by group, name and area.</summary>
    public List<Station> Scan(WorldSnapshot snapshot)
    {
        var buildings = new Dictionary<string, BuildingSnapshot>(StringComparer.Ordinal);
        foreach (var b in snapshot.Buildings) buildings[b.Id] = b;

        var workers = WorkersBySite(snapshot);
        var stations = new List<Station>();
        foreach (var machine in snapshot.Machines)
        {
            if (machine.IsContainer || Categorise(machine, Data(machine.Record)) is not { } category) continue;

            var building = buildings.GetValueOrDefault(machine.Site);
            stations.Add(Make(machine, building, category, workers.GetValueOrDefault(machine.Site) ?? []));
        }

        return [.. stations.OrderBy(s => s.Category).ThenBy(s => s.Name, StringComparer.CurrentCulture).ThenBy(s => s.Area)];
    }

    Station Make(MachineSnapshot machine, BuildingSnapshot? building, StationCategory category, List<CharacterId> assigned)
    {
        var data = Data(machine.Record);
        string name = building?.Name is { Length: > 0 } n ? n : data?.Name ?? machine.Record;
        string area = building?.TownName ?? "Wilderness";
        return new(machine.Site, name, area, category, JobOf(machine), SkillOf(category, data), assigned, machine.MaxOperators,
            Blockers(machine, building));
    }

    MachineData? Data(string record)
    {
        if (_data.TryGetValue(record, out var known)) return known;
        return _data[record] = db.Find(record) is { } building ? MachineData.From(db, building) : null;
    }

    /// <summary>Who has a job row on each site (player characters, once each).</summary>
    static Dictionary<string, List<CharacterId>> WorkersBySite(WorldSnapshot snapshot)
    {
        var map = new Dictionary<string, List<CharacterId>>(StringComparer.Ordinal);
        foreach (var c in snapshot.Characters)
        {
            if (!c.IsPlayer) continue;

            foreach (var site in c.Jobs.Select(j => j.Site).OfType<string>().Distinct())
            {
                if (!map.TryGetValue(site, out var list)) map[site] = list = [];
                list.Add(c.Id);
            }
        }

        return map;
    }

    /// <summary>
    /// The group, from the machine's function the way the original reads the building class and function: turret, farm, bench, research,
    /// refinery or furnace, mine (a well is hauling), training, storage; any other workable machine is Other; the rest are no station.
    /// </summary>
    static StationCategory? Categorise(MachineSnapshot m, MachineData? data) => m.Function switch
    {
        12 => StationCategory.Defense,
        _ when m.Farm is not null => StationCategory.Farming,
        _ when m.Craft is not null => StationCategory.Crafting,
        MachineData.ResearchFunction => StationCategory.Research,
        MachineData.RefineryFunction or MachineData.ItemFurnaceFunction => StationCategory.Refining,
        MachineData.MineFunction or MachineData.NaturalMineFunction =>
            data?.Resource == MachineData.WaterResource ? StationCategory.Storage : StationCategory.Mining,
        MachineData.TrainingFunction => StationCategory.Training,
        _ when m.IsStorage => StationCategory.Storage,
        _ when m.Operable || m.IsAutomatic => StationCategory.Other,
        _ => null,
    };

    /// <summary>The job a worker gets, as the building's own Shift verbs give it (Man Turret, Training, Auto-Haul, Use, Hauling to).</summary>
    static TaskType? JobOf(MachineSnapshot m) => m switch
    {
        { Function: 12 } => TaskType.UseTurret,
        { Function: MachineData.TrainingFunction } => TaskType.UseTrainingDummy,
        { Operable: true, IsAutomatic: true } => TaskType.OperateAutomaticMachinery,
        { Operable: true } => TaskType.OperateMachinery,
        { IsStorage: true } => TaskType.OperateStorage,
        _ => null,
    };

    /// <summary>The machine's <c>stat used</c>, else the group's skill (the original's: farming, science, labouring, turrets).</summary>
    static StatsEnumerated SkillOf(StationCategory category, MachineData? data) =>
        data?.Stat is > 0 and < StatsInfo.Count ? (StatsEnumerated)data.Stat : category switch
        {
            StationCategory.Farming => StatsEnumerated.Farming,
            StationCategory.Research => StatsEnumerated.Science,
            StationCategory.Mining or StationCategory.Refining => StatsEnumerated.Labouring,
            StationCategory.Defense => StatsEnumerated.Turrets,
            _ => StatsEnumerated.None,
        };

    /// <summary>What stops the work, joined by " / ": dismantling, broken, power off, no (or low) power, no water.</summary>
    static string Blockers(MachineSnapshot m, BuildingSnapshot? building)
    {
        var parts = new List<string>();
        if (building is { Dismantling: true }) parts.Add("DISMANTLING");
        if (m.Broken) parts.Add("BROKEN");
        if (m.Power is { Kind: PowerKind.Consumer, PowerOn: false }) parts.Add("POWER OFF");
        else if (m.Efficiency is { Consumer: true, Power: < 0.99f } e) parts.Add(e.Power <= 0.01f ? "NO POWER" : "LOW POWER");
        if (m.Efficiency is { Starved: true }) parts.Add("NO WATER");
        return string.Join(" / ", parts);
    }
}
