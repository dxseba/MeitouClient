using System.Collections.Frozen;
using System.Text;
using Meitou.Data;
using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.SquadAutonomy;

/// <summary>
/// A squad's plan as the system keeps it (the name finds the squad again after a load). Defaults are the original's: no packages, work
/// hours 0 to 24, the combat switches' defaults.
/// </summary>
sealed record Plan(string SquadName, IReadOnlyList<PlannedPackage> Packages, string? Rest, int StartHour, int EndHour,
    CombatSwitches Switches = CombatSwitches.Default)
{
    /// <summary>
    /// The original's StartWorkTime and EndWorkTime defaults.
    /// </summary>
    public const int DefaultStart = 0, DefaultEnd = 24;

    /// <summary>
    /// A new squad's plan.
    /// </summary>
    public static Plan Default(string name) => new(name, [], null, DefaultStart, DefaultEnd);
}

/// <summary>
/// One player squad as the window shows it.
/// </summary>
sealed record SquadRow(int Id, string Name, bool Autonomous, string? Package, string Home, Plan? Plan);

/// <summary>
/// What the window shows: the squads, the hour, and the building the HUD inspects (its name; null: none).
/// </summary>
sealed record AutonomyView(IReadOnlyList<SquadRow> Squads, int Hour, string? Inspected);

/// <summary>
/// Keeps the squads' plans (saved with the world), puts an autonomous squad's packages on its board by priority (<c>SetSquadPackages</c>; outside
/// the work hours the rest package instead, when one is set), turns the inspected building into a home key, and publishes the window's view and
/// the desire filter's member table (<see cref="AutonomyDesire"/>, registered with the chooser) every 30th tick.
/// </summary>
sealed class AutonomySystem(string modId, PopulationData population, GameDatabase data, Action<SimCommand> enqueue)
    : IChooserOwner, IWorldReady, IModSaveData
{
    /// <summary>
    /// The save format: 3 adds the combat switches, 2 holds the package list with priorities (1: one work package).
    /// </summary>
    const int Version = 3;

    /// <summary>
    /// Ticks before a package change that did not show is sent again.
    /// </summary>
    const int ResendTicks = 300;

    /// <summary>
    /// The plans by squad id.
    /// </summary>
    readonly Dictionary<int, Plan> _plans = [];

    /// <summary>
    /// The tick a package change was last sent, by squad id.
    /// </summary>
    readonly Dictionary<int, long> _sentAt = [];

    /// <summary>
    /// The window's orders since the last slow step.
    /// </summary>
    readonly List<SimCommand> _orders = [];

    /// <summary>
    /// Set by a load: the plans follow their squads by name at the next step.
    /// </summary>
    bool _rebind;

    public string ModId => modId;

    /// <summary>
    /// The window's view; null before the first publish.
    /// </summary>
    public volatile AutonomyView? View;

    /// <summary>
    /// The members of autonomous squads with a plan, for the desire filter; null before the first schedule.
    /// </summary>
    volatile FrozenDictionary<CharacterId, MemberRule>? _members;

    public void Register(ChooserRegistry chooser) => chooser.DesireFilter(new AutonomyDesire(() => _members));

    public void Ready(World world, bool fromSave)
    {
        if (_rebind) Rebind(world);
        _members = Members(world);
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is SquadPlanOrder or SquadHomeOrder or AutonomySwitchOrder) _orders.Add(command);
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
        _members = Members(world);
        View = Publish(world);
    }

    #region Orders

    /// <summary>
    /// One window order.
    /// </summary>
    void Apply(World world, SimCommand order)
    {
        switch (order)
        {
            case SquadPlanOrder plan when world.Squads.Find(plan.SquadId) is { } squad:
                _plans[plan.SquadId] = new(squad.Name, [.. plan.Packages], plan.Rest, Hour(plan.StartHour), Hour(plan.EndHour), plan.Switches);
                _sentAt.Remove(plan.SquadId);
                break;
            case SquadHomeOrder home when world.Squads.Find(home.SquadId) is { } squad:
                Home(world, squad, home.Clear);
                break;
            case AutonomySwitchOrder change when world.Squads.Find(change.SquadId) is { } squad:
                enqueue(new SetSquadAutonomy(squad.Id, change.On, Owner: modId) { Tick = world.Tick + 1 });
                world.Post(squad.Name + (change.On ? ": Autonomy enable" : ": Autonomy disable"));
                _sentAt.Remove(squad.Id);
                break;
        }
    }

    /// <summary>
    /// An hour of the work-hours sliders (0-24).
    /// </summary>
    static int Hour(int hour) => Math.Clamp(hour, 0, 24);

    /// <summary>
    /// Sets the squad's home to the inspected building, or clears it, with the original's messages.
    /// </summary>
    void Home(World world, Squad squad, bool clear)
    {
        if (clear)
        {
            enqueue(new SetSquadHome(squad.Id, null) { Tick = world.Tick + 1 });
            world.Post("Buildings cleared");
            return;
        }

        if (HomeKey(world, world.Player.InspectedBuilding) is not { } key)
        {
            world.Post("Select a building!");
            return;
        }

        enqueue(new SetSquadHome(squad.Id, key) { Tick = world.Tick + 1 });
        world.Post($"{squad.Name} home building: {HomeName(world, key)}");
    }

    #endregion

    #region Schedule

    /// <summary>
    /// The original's off-work test: start equal to end, or the hour outside [start, end) (wrapping past midnight when start is later).
    /// </summary>
    static bool OffWork(Plan plan, int hour)
    {
        if (plan.StartHour == plan.EndHour) return true;
        if (plan.StartHour < plan.EndHour) return hour >= plan.EndHour || hour < plan.StartHour;

        return hour >= plan.EndHour && hour < plan.StartHour;
    }

    /// <summary>
    /// Brings each autonomous squad's board to the packages its plan wants now.
    /// </summary>
    void Schedule(World world)
    {
        int hour = Hour(world);
        foreach (var (id, plan) in _plans.OrderBy(p => p.Key))
        {
            if (world.Squads.Find(id) is not { Autonomous: true } squad) continue;

            var wanted = Wanted(plan, hour);
            if (wanted.Count > 0) Bring(world, squad, wanted);
        }
    }

    /// <summary>
    /// The packages the plan wants at <paramref name="hour"/>, by priority: the rest package off work when one is set, else the plan's.
    /// </summary>
    List<(int Slot, AiPackage Package)> Wanted(Plan plan, int hour)
    {
        IReadOnlyList<PlannedPackage> entries = OffWork(plan, hour) && plan.Rest is not null ? [new PlannedPackage(plan.Rest, 0)] : plan.Packages;
        var wanted = new List<(int Slot, AiPackage Package)>();
        foreach (var entry in entries)
        {
            if (population.Ai.Package(entry.Id) is { } package) wanted.Add((entry.Priority, package));
        }

        return wanted;
    }

    /// <summary>
    /// The board takes the packages by slot (<c>SetSquadPackages</c>, saved with the squad), as the original adds each with
    /// <c>Blackboard::_addPackage(package, priority)</c>; a change that does not show is sent again after <see cref="ResendTicks"/>.
    /// </summary>
    void Bring(World world, Squad squad, List<(int Slot, AiPackage Package)> wanted)
    {
        var packages = wanted
            .Select(p => new SquadPackage(Math.Clamp(p.Slot, 0, SquadPackage.MaxSlot), p.Package.Id))
            .Distinct()
            .ToList();
        if (squad.Ai is { } board && Holds(board, packages)) return;
        if (_sentAt.TryGetValue(squad.Id, out long at) && world.Tick - at < ResendTicks) return;

        enqueue(new SetSquadPackages(squad.Id, packages) { Tick = world.Tick + 1 });
        _sentAt[squad.Id] = world.Tick;
    }

    /// <summary>
    /// Whether the board runs exactly <paramref name="wanted"/>, by slot and in order.
    /// </summary>
    static bool Holds(Blackboard board, List<SquadPackage> wanted) =>
        board.Replacements is { } held && held.Count == wanted.Count
            && held.Zip(wanted).All(x => x.First.Slot == x.Second.Slot && x.First.Package.Id == x.Second.PackageId);

    /// <summary>
    /// The hour of the day.
    /// </summary>
    static int Hour(World world) => (int)(world.GameHours % 24 + 24) % 24;

    #endregion

    #region Members

    /// <summary>
    /// Each member of an autonomous squad with a plan: its squad's switches, whether the hour is off work, whether the leader lies in a bed.
    /// </summary>
    FrozenDictionary<CharacterId, MemberRule> Members(World world)
    {
        int hour = Hour(world);
        var table = world.Characters;
        var rules = new Dictionary<CharacterId, MemberRule>();
        foreach (var (id, plan) in _plans.OrderBy(p => p.Key))
        {
            if (world.Squads.Find(id) is not { Autonomous: true } squad) continue;

            bool leaderInBed = table.TryResolveNext(squad.Leader, out int slot) && table.Cold(slot) is { InSomething: HeldState.Bed };
            var rule = new MemberRule(plan.Switches, OffWork(plan, hour), leaderInBed);
            foreach (var member in squad.Members) rules[member] = rule;
        }

        return rules.ToFrozenDictionary();
    }

    #endregion

    #region Homes

    /// <summary>
    /// The home key of an inspected building: a player-built one's id, a town building's <c>TownBuilding.Key</c>; null for none.
    /// </summary>
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

    /// <summary>
    /// A home key's shown name; None for none.
    /// </summary>
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

    /// <summary>
    /// The window's view of the player's squads.
    /// </summary>
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
        foreach (var (id, plan) in _plans.OrderBy(p => p.Key)) WritePlan(writer, id, plan);
    }

    /// <summary>
    /// One plan: squad id and name, the packages with priorities, the rest package, the hours.
    /// </summary>
    static void WritePlan(BinaryWriter writer, int id, Plan plan)
    {
        writer.Write(id);
        writer.Write(plan.SquadName);
        writer.Write(plan.Packages.Count);
        foreach (var p in plan.Packages)
        {
            writer.Write(p.Id);
            writer.Write(p.Priority);
        }

        writer.Write(plan.Rest ?? "");
        writer.Write(plan.StartHour);
        writer.Write(plan.EndHour);
        writer.Write((int)plan.Switches);
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version is not (1 or 2 or 3)) return;

        _plans.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            int id = reader.ReadInt32();
            _plans[id] = version == 1 ? ReadPlanV1(reader) : ReadPlan(reader, version);
        }

        _rebind = true;
    }

    /// <summary>
    /// A version 2 or 3 plan (2: the default switches).
    /// </summary>
    static Plan ReadPlan(BinaryReader reader, int version)
    {
        string name = reader.ReadString();
        var packages = new List<PlannedPackage>();
        for (int n = reader.ReadInt32(); n > 0; n--) packages.Add(new(reader.ReadString(), reader.ReadInt32()));

        string rest = reader.ReadString();
        int start = reader.ReadInt32(), end = reader.ReadInt32();
        var switches = version >= 3 ? (CombatSwitches)reader.ReadInt32() : CombatSwitches.Default;
        return new(name, packages, rest == "" ? null : rest, start, end, switches);
    }

    /// <summary>
    /// A version 1 plan: its work package becomes the one package (priority 0); its equal hours (then: always work) become 0 to 24.
    /// </summary>
    static Plan ReadPlanV1(BinaryReader reader)
    {
        string name = reader.ReadString(), work = reader.ReadString(), rest = reader.ReadString();
        int start = reader.ReadInt32(), end = reader.ReadInt32();
        if (start == end) (start, end) = (Plan.DefaultStart, Plan.DefaultEnd);

        List<PlannedPackage> packages = work == "" ? [] : [new(work, 0)];
        return new(name, packages, rest == "" ? null : rest, start, end);
    }

    /// <summary>
    /// After a load: a plan whose squad id now names another squad follows its squad by name (squad ids may be given anew).
    /// </summary>
    void Rebind(World world)
    {
        _rebind = false;
        var squads = SquadManagement.PlayerSquads(world);
        var moved = new Dictionary<int, Plan>();
        foreach (var (id, plan) in _plans.OrderBy(p => p.Key))
        {
            var squad = squads.FirstOrDefault(s => s.Id == id && s.Name == plan.SquadName) ?? squads.FirstOrDefault(s => s.Name == plan.SquadName);
            if (squad is not null) moved[squad.Id] = plan;
        }

        _plans.Clear();
        foreach (var (id, plan) in moved.OrderBy(p => p.Key)) _plans[id] = plan;
    }

    #endregion
}
