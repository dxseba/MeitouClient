using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Population;
using Meitou.Simulation.Production;
using static Meitou.WorkForCats.WorkForCatsMod;

namespace Meitou.WorkForCats;

/// <summary>
/// An employee's wage period: its workplace, the hours and cats of the unpaid hour, and its walks that ended short of the workplace.
/// </summary>
internal sealed class Employee
{
    public required string Name;
    public required string Site;
    public double Hours;
    public double Cats;
    public int Fails;
    public long RetryTick;
}

/// <summary>
/// Runs the employment in the tick: <see cref="HireOrder"/> / <see cref="QuitOrder"/> add and remove the mod job; every
/// <see cref="Cadence"/> ticks each employee standing at its workplace at the work clip earns its wage, trains the station's skill and adds
/// relation progress for the employer; a full hour is paid into the player's money. Its state is hashed and saved.
/// </summary>
internal sealed partial class WorkSystem : ITickSystem, IStateHashed, IChooserOwner, ITaskOwner
{
    /// <summary>
    /// Ticks between work sweeps and view updates.
    /// </summary>
    const int Cadence = 10;

    readonly WorkSettings _settings;
    readonly PopulationData _data;
    readonly WorkplaceFinder _finder;
    readonly WorkJob _job;
    readonly Dictionary<CharacterId, Employee> _employees = [];
    readonly SortedDictionary<string, double> _relationHours = new(StringComparer.Ordinal);
    readonly AiSystem? _ai;
    WorkRules _rules = WorkRules.Defaults;

    public WorkSystem(WorkSettings settings, PopulationData data, AiSystem? ai)
    {
        _settings = settings;
        _data = data;
        _ai = ai;
        _finder = new WorkplaceFinder(data);
        _job = new WorkJob(this);
        _rules = settings.Rules();
    }

    public void Register(ChooserRegistry chooser) => chooser.Jobs(JobType, _job);

    public void RegisterTasks(TaskHandlers tasks) => tasks.Register(JobType, _job);

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _rules = _settings.Rules();
        foreach (var command in commands)
        {
            if (command is HireOrder hire) Hire(world, hire);
            else if (command is QuitOrder quit) Quit(world, quit.Actor);
        }
    }

    public void SlowWorld(World world)
    {
        if (_loaded is not null) Restore(world);
        if (world.Tick % Cadence != 0) return;

        Sweep(world);
        Publish(world);
    }

    /// <summary>
    /// The workplace of <paramref name="site"/> and why it offers no job; a new hire also needs one of its operator slots free (the AI's
    /// <c>WorkSlots</c>: the original's employee holds one, so a full station takes nobody).
    /// </summary>
    Lookup Find(World world, string? site)
    {
        var lookup = _finder.Find(world, site, _rules);
        if (lookup.Eligible is null || site is null || _ai?.WorkSlots(site) is not { Free: false }) return lookup;

        return lookup with { Refusal = "Every station here is taken." };
    }

    /// <summary>
    /// The eligible workplace of a site now, null when it no longer employs.
    /// </summary>
    internal Workplace? Eligible(World world, string? site) => _finder.Find(world, site, _rules).Eligible;

    /// <summary>
    /// The employee record of a character on the job at <paramref name="site"/> (made when missing).
    /// </summary>
    internal Employee EmployeeOf(World world, int slot, string site)
    {
        var id = world.Characters.IdOf(slot);
        if (_employees.TryGetValue(id, out var e) && e.Site == site) return e;

        return _employees[id] = new Employee { Name = world.Characters.Cold(slot)!.Name, Site = site };
    }

    /// <summary>
    /// Whether the character's job waits after walks that ended short of the workplace.
    /// </summary>
    internal bool Waiting(World world, int slot) =>
        _employees.TryGetValue(world.Characters.IdOf(slot), out var e) && e.RetryTick > world.Tick;

    /// <summary>
    /// The workplace of the character's mod job, null when it has none.
    /// </summary>
    internal static string? JobSite(CharacterCold cold)
    {
        foreach (var job in cold.Jobs)
        {
            if (job.Type == JobType) return job.Site;
        }

        return null;
    }

    #region Hiring

    void Hire(World world, HireOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Actor, out int slot) || table.Cold(slot) is not { IsPlayer: true, Medical.Dead: false } cold) return;

        var lookup = Find(world, order.Site);
        if (lookup.Eligible is not { } place)
        {
            world.Post(lookup.Refusal ?? "This building offers no employment.");
            return;
        }

        RemoveJob(world, slot, cold);
        cold.Jobs.Insert(0, new PermaJob(JobType, order.Site));
        cold.Orders |= Simulation.Player.CharacterOrders.Jobs;
        _employees[order.Actor] = new Employee { Name = cold.Name, Site = order.Site };
        DecisionSystem.Request(cold);
        world.Post($"{cold.Name} got a job at {place.Name}.");
    }

    void Quit(World world, CharacterId actor)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(actor, out int slot) || table.Cold(slot) is not { } cold || JobSite(cold) is null) return;

        RemoveJob(world, slot, cold);
        _employees.Remove(actor);
        DecisionSystem.Request(cold);
        world.Post($"{cold.Name} quit the job.");
    }

    /// <summary>
    /// Takes the mod job off the list; a job's task running now ends first, as the list's indices move.
    /// </summary>
    static void RemoveJob(World world, int slot, CharacterCold cold)
    {
        var task = (CharacterTask)world.Characters.Next[slot].Task;
        if (cold.Running is { Source: TaskSource.Job, Implicit: false } run && run.Runs(task)) TaskStart.End(world, slot, TaskEnd.Stopped);

        cold.Jobs.RemoveAll(j => j.Type == JobType);
    }

    #endregion

    #region Working

    /// <summary>
    /// Every player character with the mod job: its record follows the job (a job taken off the jobs panel quits), and one working earns.
    /// </summary>
    void Sweep(World world)
    {
        var table = world.Characters;
        var working = new Dictionary<CharacterId, float>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold) continue;

            var id = table.IdOf(slot);
            if (JobSite(cold) is not { } site)
            {
                _employees.Remove(id);
                continue;
            }

            var employee = EmployeeOf(world, slot, site);
            employee.Name = cold.Name;
            if (Working(world, slot, cold, employee) is not { } place) continue;

            Earn(world, cold, employee, place);
            working[id] = place.HungerRate;
        }

        foreach (var id in _employees.Keys.Where(id => !table.TryResolveNext(id, out _)).ToList()) _employees.Remove(id);
        PublishHunger(working);
    }

    /// <summary>
    /// The workplace when the character is at it on the job's standing task at the work clip, conscious and out of combat; null otherwise
    /// (walking, waiting, on another order, Jobs off, fighting, down).
    /// </summary>
    Workplace? Working(World world, int slot, CharacterCold cold, Employee employee)
    {
        ref var n = ref world.Characters.Next[slot];
        if (n.Task != (byte)CharacterTask.Idle || cold.InCombat || cold.Medical is { Incapacitated: true }) return null;
        if (cold.Running is not { Row: JobType, Task: CharacterTask.Idle } || cold.ActionClip is null) return null;
        if (Eligible(world, employee.Site) is not { } place || cold.ActionClip != place.Clip) return null;

        return WorkJob.AtWork(in n, place) ? place : null;
    }

    void Earn(World world, CharacterCold cold, Employee employee, Workplace place)
    {
        float seconds = Cadence * world.TickSeconds;
        double hours = seconds / Wages.HourSeconds;
        employee.Hours += hours;
        employee.Cats += RateOf(cold, place) * hours;

        Train(world, cold, place.Stat, seconds);
        if (!place.Own) Befriend(world, place.Owner, hours);
        if (employee.Hours >= 1) Pay(world, cold, employee, place, RateOf(cold, place));
    }

    /// <summary>
    /// The character's hourly wage at the workplace now.
    /// </summary>
    internal float RateOf(CharacterCold cold, Workplace place) =>
        Wages.Hourly(_rules, cold.Stats?[place.Stat] ?? 0, RelationOf(place.Owner), place.Own);

    float RelationOf(int faction)
    {
        var relations = _data.Relations;
        int player = relations.PlayerFaction;
        return player >= 0 && relations.Contains(faction) ? relations.Get(faction, player) : 0;
    }

    /// <summary>
    /// The full hour's whole cats go to the player's money (the original's <c>WorkClock</c>): the cats earned less the overshoot past the hour at the
    /// current rate, floored; the rest (the overshoot's cats and the fraction of a cat) stays for the next hour.
    /// </summary>
    static void Pay(World world, CharacterCold cold, Employee employee, Workplace place, float rate)
    {
        double overshoot = employee.Hours - Math.Floor(employee.Hours);
        long cats = Math.Max(0, (long)Math.Floor(employee.Cats - overshoot * rate + 1e-8));
        employee.Cats -= cats;
        employee.Hours = overshoot;
        world.Player.Money += cats;
        world.Post($"{cold.Name} earned {cats} cats working at {place.Name}.");
    }

    /// <summary>
    /// The station's skill trains through the continuous XP entry at multiplier 1 (the original calls <c>CharStats::xpGeneral(dt, 1, stat)</c> in
    /// place of the native operating XP).
    /// </summary>
    static void Train(World world, CharacterCold cold, StatsEnumerated stat, float seconds)
    {
        if (cold.Stats is not { } stats || cold.Race is not { } race) return;
        if (world.Systems.OfType<CrimeSystem>().FirstOrDefault()?.Xp is not { } xp) return;

        cold.UseSkill(stat, world.Tick);
        xp.Continuous(stats, race, stat, seconds, 1f);
    }

    #endregion

    #region Relations

    /// <summary>
    /// Adds the hours to the employer's progress; every <see cref="WorkRules.RelationHours"/> combined hours raise its relation.
    /// </summary>
    void Befriend(World world, int faction, double hours)
    {
        var relations = _data.Relations;
        if (!_rules.Relations || !relations.Contains(faction) || _rules.RelationHours <= 0) return;

        string id = relations.Factions[faction].Id;
        double progress = _relationHours.GetValueOrDefault(id) + hours;
        while (progress >= _rules.RelationHours)
        {
            progress -= _rules.RelationHours;
            Raise(world, faction);
        }

        _relationHours[id] = progress;
    }

    void Raise(World world, int faction)
    {
        var relations = _data.Relations;
        int player = relations.PlayerFaction;
        if (player < 0 || faction == player || _rules.RelationPoints <= 0) return;

        float now = relations.Get(faction, player);
        if (now >= Wages.TopRelation) return;

        float delta = Math.Min(_rules.RelationPoints, Wages.TopRelation - now);
        PlayerRelations.Add(world, relations, faction, player, delta, new RelationCause(RelationCauseType.Trade), "paid work");
    }

    #endregion
}
