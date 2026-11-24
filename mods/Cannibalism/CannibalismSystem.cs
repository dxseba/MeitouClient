using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Kidnap;
using Meitou.Simulation.Player;

namespace Meitou.Cannibalism;

/// <summary>
/// Runs Eat Alive in the serial slow step. An <see cref="EatAliveOrder"/> gives each eater the game's own task 220 Eat target alive
/// (<c>World.GiveOrder</c>, the original's <c>newPlayerTaskSelectedCharacters(EAT_TARGET_ALIVE)</c>): the engine walks it there and eats with its
/// cannibal job (clip, bites, barks, limb loss, death). As the original's <c>Character::gettingEaten</c> hook, every eater whose task-220 meal is
/// biting regains <see cref="CannibalismSettings.HungerPerHour"/> per game hour: the first bite only arms its clock, each step at most
/// <see cref="MaxStepHours"/>, up to the hunger cap.
/// </summary>
sealed class CannibalismSystem(CannibalismSettings settings) : ITickSystem
{
    /// <summary>The original's widened eat trigger for task 220 (its <c>AI::isWithinChoppingRange</c> hook), given as the order's reach.</summary>
    const float EatReach = 50;

    /// <summary>The largest step, in game hours, of the feeding (a long frame or a fast-forward feeds no more than this).</summary>
    const double MaxStepHours = 0.05;

    readonly List<EatAliveOrder> _orders = [];

    /// <summary>Each biting eater's game hours at its last bite (its feeding clock); dropped when it stops.</summary>
    readonly Dictionary<CharacterId, double> _lastBite = [];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is EatAliveOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var order in _orders) Give(world, order);

        _orders.Clear();
        if (settings.Enabled) Feed(world);
    }

    /// <summary>
    /// Gives every eater but the target task 220 on it, starting to eat within <see cref="EatReach"/> as the original; the engine's check decides
    /// (a target no longer edible is refused there).
    /// </summary>
    static void Give(World world, EatAliveOrder order)
    {
        var options = new OrderOptions { Target = order.Target, Reach = EatReach };
        foreach (var eater in order.Eaters)
        {
            if (eater != order.Target) world.GiveOrder(eater, TaskType.EatTargetAlive, options);
        }
    }

    /// <summary>Feeds every eater whose meal (task 220, the engine's eat jobs) is at its bites this tick; the others' clocks are dropped.</summary>
    void Feed(World world)
    {
        if (world.Systems.OfType<CaptiveSystem>().FirstOrDefault() is not { } captive) return;

        var biting = new HashSet<CharacterId>();
        foreach (var job in captive.Jobs)
        {
            bool meal = job.Kind is CaptiveJobKind.Eat or CaptiveJobKind.EatBody && job.Phase == CaptivePhase.Work;
            if (meal && OnTask220(world, job) && biting.Add(job.Worker)) Bite(world, job.Worker);
        }

        foreach (var eater in _lastBite.Keys.Where(e => !biting.Contains(e)).ToList()) _lastBite.Remove(eater);
    }

    /// <summary>
    /// Whether the job's eater is on task 220 (the original feeds only while the eater's goal is 220): its order in hand is a started Eat target
    /// alive on this captive. The cannibal goals' own meals (their goals are the random-eat ones) are not fed.
    /// </summary>
    static bool OnTask220(World world, CaptiveJob job) =>
        world.Characters.TryResolveNext(job.Worker, out int slot)
        && world.Characters.Cold(slot)?.OrderQueue.Front is { Started: true, Row: TaskType.EatTargetAlive } front && front.Target == job.Captive;

    void Bite(World world, CharacterId eater)
    {
        double now = world.GameHours;
        bool armed = _lastBite.TryGetValue(eater, out double last);
        _lastBite[eater] = now;
        if (!armed || !world.Characters.TryResolveNext(eater, out int slot)) return;

        if (world.Characters.Cold(slot)?.Medical is not { Dead: false } medical) return;

        double hours = Math.Clamp(now - last, 0, MaxStepHours);
        medical.Hunger = Math.Min(MedicalState.MaxHunger, medical.Hunger + settings.HungerPerHour * (float)hours);
    }
}
