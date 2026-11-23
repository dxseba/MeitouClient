using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Dialogue;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Kidnap;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

namespace Meitou.Cannibalism;

/// <summary>
/// Runs the Eat Alive orders in the serial slow step. Each eater gets the player's move order to the victim (<c>World.GiveOrder</c>); within
/// <see cref="EatRange"/> it stops, sheathes, plays the engine's <c>eat</c> clip and bites through <see cref="MedicalState.BeingEaten"/> at the
/// engine's rate, and is fed while it bites. A later order that moves it, the victim's death or escape, or the eater going down ends the meal.
/// </summary>
sealed class CannibalismSystem(CannibalismSettings settings, PopulationData data) : ITickSystem
{
    /// <summary>World units from the victim within which an eater starts eating (the engine's own job: 15, or a cage's use range + 15).</summary>
    const float EatRange = 50;

    /// <summary>Seconds an eater may take to reach the victim before the order is dropped.</summary>
    const float WalkLimitSeconds = 40;

    /// <summary>Speed above which an eating character counts as walked away (a later order).</summary>
    const float MovedSpeed = 2;

    /// <summary>The largest step, in game hours, of the feeding (a long frame or a fast-forward feeds no more than this).</summary>
    const double MaxStepHours = 0.05;

    /// <summary>
    /// The engine's eat job's numbers (<c>CaptiveSystem</c>): seconds between the eater's barks, and the toughness XP amount of a meal still awake.
    /// </summary>
    const float BarkSeconds = 10, AwakeToughnessXp = 10;

    readonly XpService _xp = new(data.Bodies.Constants, data.BodyOptions);

    /// <summary>One eater and its victim: walking until it reaches the victim, then eating.</summary>
    sealed class Meal(CharacterId eater, CharacterId victim, long since)
    {
        public CharacterId Eater { get; } = eater;
        public CharacterId Victim { get; } = victim;
        public long Since { get; } = since;
        public bool Eating { get; set; }
        public long EatingSince { get; set; }

        /// <summary>
        /// The meal's seconds of eating at which the eater barks next.
        /// </summary>
        public float NextBark { get; set; }
    }

    readonly List<EatAliveOrder> _orders = [];
    readonly List<Meal> _meals = [];
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
        foreach (var order in _orders) Start(world, order);
        _orders.Clear();

        double hours = StepHours(world);
        _meals.RemoveAll(meal => !Step(world, meal, hours));
    }

    /// <summary>Game hours since the last step, at most <see cref="MaxStepHours"/>; 0 on the first.</summary>
    double StepHours(World world)
    {
        double hours = double.IsNaN(_lastHours) ? 0 : Math.Clamp(world.GameHours - _lastHours, 0, MaxStepHours);
        _lastHours = world.GameHours;
        return hours;
    }

    #region Starting

    void Start(World world, EatAliveOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Target, out int c) || table.Cold(c) is not { } victim) return;
        if (Refusal(victim) is { } why)
        {
            world.Post($"{victim.Name} {why}.");
            return;
        }

        var at = table.Next[c].Position;
        foreach (var eater in order.Eaters)
        {
            if (eater == order.Target || !table.TryResolveNext(eater, out int w) || table.Cold(w) is not { } wc || !CanEat(wc)) continue;

            _meals.RemoveAll(m => m.Eater == eater);
            world.GiveOrder(eater, TaskType.MoveCusOrdered, point: at);
            _meals.Add(new Meal(eater, order.Target, world.Tick));
        }
    }

    /// <summary>Why <paramref name="victim"/> cannot be eaten now; null when it can.</summary>
    static string? Refusal(CharacterCold victim)
    {
        if (victim.Medical is not { Dead: false } medical || victim.Race is null) return "cannot be eaten";
        if (victim.Race.IsRobot) return "is a robot and cannot be eaten";
        if (!victim.CarriedBy.IsNone) return "is being carried";
        if (victim.InSomething == HeldState.Bed) return "is in a bed";
        if (victim.IsImprisoned || victim.InSomething == HeldState.None && medical.Unconscious) return null;

        return "must be unconscious or held in a cage";
    }

    static bool CanEat(CharacterCold eater) =>
        eater is { IsPlayer: true, Medical.Incapacitated: false, InCombat: false } && eater.CarriedBy.IsNone && eater.InSomething == HeldState.None;

    static bool StillEdible(CharacterCold victim) =>
        victim.CarriedBy.IsNone && (victim.IsImprisoned || victim.InSomething == HeldState.None && victim.Medical!.Unconscious);

    #endregion

    #region Eating

    /// <summary>One tick of a meal; false when it is over.</summary>
    bool Step(World world, Meal meal, double hours)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(meal.Eater, out int w) || table.Cold(w) is not { } eater) return false;
        if (!table.TryResolveNext(meal.Victim, out int c) || table.Cold(c) is not { Medical.Dead: false } victim) return Stop(eater, meal);
        if (!StillEdible(victim) || !CanEat(eater)) return Stop(eater, meal);

        bool near = Distance(table.Next[w].Position, table.Next[c].Position) <= EatRange;
        if (!meal.Eating)
        {
            if (near) Begin(world, table, w, eater, meal);
            return near || (world.Tick - meal.Since) * world.TickSeconds < WalkLimitSeconds;
        }

        // A later order gives the eater another task, walks it off or plays another clip: the meal ends (the clip plays only while idle).
        bool settled = world.Tick - meal.EatingSince > 5;
        bool moved = settled && (table.Next[w].Task != (byte)CharacterTask.Idle || table.Next[w].Velocity.Length() > MovedSpeed
            || eater.ActionClip != CaptiveSystem.EatClip);
        if (!near || moved) return Stop(eater, meal);

        Bark(world, meal, victim);
        Bite(world, meal, eater, victim, c, hours);
        return true;
    }

    /// <summary>What the engine's eat job does on arriving: stop, sheathe, the <c>eat</c> clip.</summary>
    static void Begin(World world, CharacterTable table, int w, CharacterCold eater, Meal meal)
    {
        TaskStart.Stand(table, w);
        eater.DrawnWeapon = WeaponKinds.None;
        eater.ActionClip = CaptiveSystem.EatClip;
        meal.Eating = true;
        meal.EatingSince = world.Tick;
    }

    static bool Stop(CharacterCold eater, Meal meal)
    {
        if (meal.Eating && eater.ActionClip == CaptiveSystem.EatClip) eater.ActionClip = null;
        return false;
    }

    /// <summary>
    /// The engine's eat barks every <see cref="BarkSeconds"/> from the start: the eater's eating sounds (event 52) and, for a caged victim, its
    /// torture scream (event 11).
    /// </summary>
    static void Bark(World world, Meal meal, CharacterCold victim)
    {
        float eating = (world.Tick - meal.EatingSince) * world.TickSeconds;
        if (eating < meal.NextBark) return;

        meal.NextBark = eating + BarkSeconds;
        world.Defer(new DialogueEvent(meal.Eater, meal.Victim, CaptiveSystem.EatingSounds) { Tick = world.Tick + 1 });
        if (victim.IsImprisoned) world.Defer(new DialogueEvent(meal.Victim, meal.Eater, CaptiveSystem.ScreamingTorture) { Tick = world.Tick + 1 });
    }

    /// <summary>
    /// One bite, as the engine's eat job bites (<c>CaptiveSystem.Eat</c>): a victim still awake trains toughness, one playing dead gets up, it is
    /// eaten at the engine's rate for its toughness (limbs lost as the dismemberment option allows, never dropped as items; vampiric eaters
    /// drink), and the eater regains hunger.
    /// </summary>
    void Bite(World world, Meal meal, CharacterCold eater, CharacterCold victim, int c, double hours)
    {
        var medical = victim.Medical!;
        if (victim.Stats is { } stats && !medical.Incapacitated)
            _xp.Continuous(stats, victim.Race!, StatsEnumerated.Toughness, world.TickSeconds, AwakeToughnessXp, ExperienceSource.Injury);
        if (medical.PlayingDead) medical.StandUp(Orders.Has(victim, CharacterOrders.Sneak));

        var events = new List<MedicalEvent>();
        float rate = MedicalState.EatRate(victim.Stats?.Toughness ?? 0, victim.Stats?.Scale ?? 1);
        bool limbs = data.BodyOptions.Dismemberment != Dismemberment.Never;
        medical.BeingEaten(rate, world.TickSeconds, Context(world, victim, c), limbs, events, vampiric: eater.Race is { Vampiric: true });
        BodyEvents(world, meal.Victim, c, medical, events);
        if (medical.Dead) Deaths.Report(world, meal.Victim, meal.Eater, DeathSite.Eaten);
        if (medical.Dead && (victim.IsPlayer || world.Player.Faction >= 0 && victim.Faction == world.Player.Faction))
            world.Post($"{victim.Name} has been eaten alive.");

        if (eater.Medical is { Dead: false } own)
            own.Hunger = Math.Min(MedicalState.MaxHunger, own.Hunger + settings.HungerPerHour * (float)hours);
    }

    /// <summary>
    /// The victim's medical context (toughness, strength, the seed and its roll key).
    /// </summary>
    MedicalContext Context(World world, CharacterCold victim, int c) => new(data.Bodies.Constants, data.BodyOptions, victim.Race!)
    {
        Toughness = victim.Stats?.ScaledToughness ?? 0,
        Strength = victim.Stats?.Strength ?? 50,
        HasStats = victim.Stats is not null,
        Seed = world.Seed,
        CharacterKey = Rng.Key(world.Characters.IdOf(c)),
    };

    /// <summary>
    /// What the engine does with a bite's medical events: a death or knock-out stands the victim, a lost limb goes to the renderer (no item drop,
    /// as an eaten limb's) and the victim cries its lost-limb event.
    /// </summary>
    static void BodyEvents(World world, CharacterId victim, int c, MedicalState medical, List<MedicalEvent> events)
    {
        foreach (var e in events)
        {
            if (e.Kind is MedicalEventKind.Died or MedicalEventKind.KnockedOut) TaskStart.Stand(world.Characters, c);
            else if (e.Kind == MedicalEventKind.PartSevered)
            {
                var part = medical.Parts[e.Part].Template;
                world.Characters.LimbsChanged.Enqueue((c, part.LimbSlot, false));
                if (DialogueEvents.LostLimb(part) is { } cry) world.Defer(new DialogueEvent(victim, CharacterId.None, cry) { Tick = world.Tick + 1 });
            }
        }
    }

    static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    #endregion
}
