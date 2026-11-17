using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Kidnap;
using Meitou.Simulation.Locks;

namespace Meitou.Cannibalism;

/// <summary>
/// Runs the Eat Alive orders: each eater gets the engine's eat job (<see cref="CaptiveJobKind.Eat"/> at the victim's cage, else
/// <see cref="CaptiveJobKind.EatBody"/> where it lies) as a player-side job, which a later order of the player ends. The job's walk is the
/// engine's; this system starts the eating once an eater is within <see cref="EatRange"/>, and feeds the eaters while they bite.
/// </summary>
sealed class CannibalismSystem(CannibalismSettings settings, CaptiveJobs jobs) : ITickSystem
{
    /// <summary>
    /// World units from the victim within which an eater of this mod starts eating (the engine's own job: 15, or a cage's use range + 15).
    /// </summary>
    const float EatRange = 50;

    /// <summary>The largest step, in game hours, of the feeding (a long frame or a fast-forward feeds no more than this).</summary>
    const double MaxStepHours = 0.05;

    readonly List<EatAliveOrder> _orders = [];
    readonly HashSet<CharacterId> _eaters = [];
    double _lastHours = double.NaN;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is EatAliveOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        if (CaptiveJobs.System(world) is not { } captives)
        {
            _orders.Clear();
            return;
        }

        foreach (var order in _orders) Start(world, captives, order);
        _orders.Clear();

        double step = StepHours(world);
        foreach (var job in captives.Jobs.Where(j => _eaters.Contains(j.Worker) && IsEating(j)).ToArray()) Step(world, job, step);
        _eaters.RemoveWhere(id => !captives.Jobs.Any(j => j.Worker == id && IsEating(j)));
    }

    static bool IsEating(CaptiveJob job) => job.Kind is CaptiveJobKind.Eat or CaptiveJobKind.EatBody;

    /// <summary>Game hours since the last step, at most <see cref="MaxStepHours"/>; 0 on the first.</summary>
    double StepHours(World world)
    {
        double hours = double.IsNaN(_lastHours) ? 0 : Math.Clamp(world.GameHours - _lastHours, 0, MaxStepHours);
        _lastHours = world.GameHours;
        return hours;
    }

    #region Starting

    void Start(World world, CaptiveSystem captives, EatAliveOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Target, out int c) || table.Cold(c) is not { } victim) return;
        if (Refusal(victim) is { } why)
        {
            world.Post($"{victim.Name} {why}.");
            return;
        }

        foreach (var eater in order.Eaters)
        {
            if (eater != order.Target && table.TryResolveNext(eater, out int w) && table.Cold(w) is { } wc && CanEat(wc))
                Give(world, captives, w, wc, victim, order.Target);
        }
    }

    /// <summary>Why <paramref name="victim"/> cannot be eaten now; null when it can.</summary>
    static string? Refusal(CharacterCold victim)
    {
        if (victim.Medical is not { Dead: false } medical || victim.Race is null) return "cannot be eaten";
        if (victim.Race.IsRobot) return "is a robot and cannot be eaten";
        if (!victim.CarriedBy.IsNone) return "is being carried";
        if (victim.InSomething == HeldState.Bed) return "is in a bed";
        if (Caged(victim) || victim.InSomething == HeldState.None && medical.Unconscious) return null;

        return "must be unconscious or held in a cage";
    }

    static bool Caged(CharacterCold victim) => victim is { IsImprisoned: true, InWhat: not null };

    static bool CanEat(CharacterCold eater) =>
        eater is { IsPlayer: true, Medical.Incapacitated: false, InCombat: false } && eater.CarriedBy.IsNone && eater.InSomething == HeldState.None;

    /// <summary>The eater's job, as a player animal's race goal makes one: player-side, so a later order of the player ends it.</summary>
    void Give(World world, CaptiveSystem captives, int w, CharacterCold eater, CharacterCold victim, CharacterId target)
    {
        var table = world.Characters;
        var job = new CaptiveJob
        {
            Kind = Caged(victim) ? CaptiveJobKind.Eat : CaptiveJobKind.EatBody,
            Worker = table.IdOf(w),
            Captive = target,
            Cage = Caged(victim) ? victim.InWhat! : "",
            Faction = eater.Faction,
            PlayerSide = true,
        };

        (job.LastTask, job.LastGoal) = (table.Next[w].Task, table.Next[w].Goal);
        jobs.Add(captives, job);
        eater.Engaged = true;
        _eaters.Add(job.Worker);
    }

    #endregion

    #region Eating

    /// <summary>An eater's step: within <see cref="EatRange"/> of the victim the walk ends and the eating starts; eating, it is fed.</summary>
    void Step(World world, CaptiveJob job, double hours)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(job.Worker, out int w) || table.Cold(w) is not { } eater || !table.TryResolveNext(job.Captive, out int c)) return;

        bool near = Distance(table.Next[w].Position, table.Next[c].Position) <= EatRange;
        if (job.Phase == CaptivePhase.Walk && near) BeginEating(table, job, w, eater);
        if (job.Phase == CaptivePhase.Work) Feed(eater, hours);
    }

    /// <summary>What the engine's job does on arriving (stop, sheathe, the <c>eat</c> clip), so its next step bites at once.</summary>
    static void BeginEating(CharacterTable table, CaptiveJob job, int w, CharacterCold eater)
    {
        TaskStart.Stand(table, w);
        job.Phase = CaptivePhase.Work;
        job.Time = 0;
        eater.DrawnWeapon = WeaponKinds.None;
        eater.ActionClip = CaptiveSystem.EatClip;

        // The stop is ours, not the player's: the job's override test compares against the task it last saw.
        (job.LastTask, job.LastGoal) = (table.Next[w].Task, table.Next[w].Goal);
    }

    void Feed(CharacterCold eater, double hours)
    {
        if (eater.Medical is not { Dead: false } medical) return;

        medical.Hunger = Math.Min(MedicalState.MaxHunger, medical.Hunger + settings.HungerPerHour * (float)hours);
    }

    static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    #endregion
}
