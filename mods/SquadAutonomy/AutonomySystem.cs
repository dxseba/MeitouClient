using System.Text;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace Meitou.SquadAutonomy;

/// <summary>A squad's plan as the system keeps it (the name finds the squad again after a load).</summary>
sealed record Plan(string SquadName, string? Work, string? Rest, int StartHour, int EndHour);

/// <summary>One player squad as the window shows it.</summary>
sealed record SquadRow(int Id, string Name, bool Autonomous, string? Package, string Home, Plan? Plan);

/// <summary>What the window shows: the squads, the hour, and the building the HUD inspects (its name; null: none).</summary>
sealed record AutonomyView(IReadOnlyList<SquadRow> Squads, int Hour, string? Inspected);

/// <summary>
/// Keeps the squads' plans (saved with the world), switches an autonomous squad between its work and rest packages by the hour through the engine's
/// <c>ChangeAi</c>, turns the inspected building into a home key, and publishes the window's view every 30th tick.
/// </summary>
sealed class AutonomySystem(string modId, PopulationData population, GameDatabase data, Action<SimCommand> enqueue) : ITickSystem, IModSaveData
{
    const int Version = 1;

    /// <summary>Ticks before a package change that did not show is sent again.</summary>
    const int ResendTicks = 300;

    readonly Dictionary<int, Plan> _plans = [];
    readonly Dictionary<int, long> _sentAt = [];
    readonly List<SimCommand> _orders = [];
    bool _rebind;

    public string ModId => modId;

    /// <summary>The window's view; null before the first publish.</summary>
    public volatile AutonomyView? View;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is SquadPlanOrder or SquadHomeOrder) _orders.Add(command);
        }
    }

    public void SlowWorld(World world)
    {
        if (_rebind) Rebind(world);
        bool changed = _orders.Count > 0;
        foreach (var order in _orders) Apply(world, order);
        _orders.Clear();

        if (!changed && world.Tick % 30 != 0) return;

        Schedule(world);
        View = Publish(world);
    }

    void Apply(World world, SimCommand order)
    {
        switch (order)
        {
            case SquadPlanOrder plan when world.Squads.Find(plan.SquadId) is { } squad:
                _plans[plan.SquadId] = new(squad.Name, plan.Work, plan.Rest, Math.Clamp(plan.StartHour, 0, 23), Math.Clamp(plan.EndHour, 0, 23));
                _sentAt.Remove(plan.SquadId);
                break;
            case SquadHomeOrder home:
                if (home.Clear) enqueue(new SetSquadHome(home.SquadId, null) { Tick = world.Tick + 1 });
                else if (HomeKey(world, world.Player.InspectedBuilding) is { } key) enqueue(new SetSquadHome(home.SquadId, key) { Tick = world.Tick + 1 });
                else world.Post("Select a building first.");
                break;
        }
    }

    #region Schedule

    /// <summary>Rest outside the work hours (start equal to end: always work).</summary>
    static bool Resting(Plan plan, int hour) =>
        plan.Rest is not null && plan.StartHour != plan.EndHour
        && (plan.StartHour < plan.EndHour ? hour < plan.StartHour || hour >= plan.EndHour : hour < plan.StartHour && hour >= plan.EndHour);

    void Schedule(World world)
    {
        int hour = Hour(world);
        foreach (var (id, plan) in _plans)
        {
            if (world.Squads.Find(id) is not { Autonomous: true } squad) continue;

            string? wanted = Resting(plan, hour) ? plan.Rest : plan.Work;
            if (wanted is null || squad.Ai?.Current?.Id == wanted) continue;
            if (_sentAt.TryGetValue(id, out long at) && world.Tick - at < ResendTicks) continue;

            enqueue(new ChangeAi(id, wanted) { Tick = world.Tick + 1 });
            _sentAt[id] = world.Tick;
        }
    }

    static int Hour(World world) => (int)(world.GameHours % 24 + 24) % 24;

    #endregion

    #region Homes

    /// <summary>The home key of an inspected building: a player-built one's id, a town building's <c>TownBuilding.Key</c>; null for none.</summary>
    string? HomeKey(World world, string? instance)
    {
        if (string.IsNullOrEmpty(instance)) return null;
        if (world.Buildings.Find(instance) is not null) return instance;

        foreach (var site in population.Sites)
        {
            foreach (var b in site.Buildings)
            {
                if (b.InstanceId == instance) return b.Key;
            }
        }

        return null;
    }

    string HomeName(World world, string? key)
    {
        if (key is null) return "None";
        if (world.Buildings.Find(key) is { } built) return data.Find(built.Record)?.Name ?? key;

        foreach (var site in population.Sites)
        {
            foreach (var b in site.Buildings)
            {
                if (b.Key == key) return $"{b.Name} ({site.Record.Name})";
            }
        }

        return key;
    }

    #endregion

    #region View

    AutonomyView Publish(World world)
    {
        var rows = SquadManagement.PlayerSquads(world)
            .Select(s => new SquadRow(s.Id, s.Name, s.Autonomous, s.Ai?.Current?.Id, HomeName(world, s.AssignedHome), _plans.GetValueOrDefault(s.Id)))
            .ToList();
        string? inspected = HomeKey(world, world.Player.InspectedBuilding) is { } key ? HomeName(world, key) : null;
        return new(rows, Hour(world), inspected);
    }

    #endregion

    #region Save

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_plans.Count);
        foreach (var (id, plan) in _plans.OrderBy(p => p.Key))
        {
            writer.Write(id);
            writer.Write(plan.SquadName);
            writer.Write(plan.Work ?? "");
            writer.Write(plan.Rest ?? "");
            writer.Write(plan.StartHour);
            writer.Write(plan.EndHour);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _plans.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            int id = reader.ReadInt32();
            string name = reader.ReadString(), work = reader.ReadString(), rest = reader.ReadString();
            _plans[id] = new(name, work == "" ? null : work, rest == "" ? null : rest, reader.ReadInt32(), reader.ReadInt32());
        }

        _rebind = true;
    }

    /// <summary>After a load: a plan whose squad id now names another squad follows its squad by name (squad ids may be given anew).</summary>
    void Rebind(World world)
    {
        _rebind = false;
        var squads = SquadManagement.PlayerSquads(world);
        var moved = new Dictionary<int, Plan>();
        foreach (var (id, plan) in _plans)
        {
            var squad = squads.FirstOrDefault(s => s.Id == id && s.Name == plan.SquadName) ?? squads.FirstOrDefault(s => s.Name == plan.SquadName);
            if (squad is not null) moved[squad.Id] = plan;
        }

        _plans.Clear();
        foreach (var (id, plan) in moved) _plans[id] = plan;
    }

    #endregion
}
