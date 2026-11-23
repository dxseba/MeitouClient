using System.Collections.Frozen;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.SleepFix;

/// <summary>
/// Once a second in the serial slow step, for each living player character in slot order, the original's per-frame rules (SleepFix.dll's
/// <c>AI::update4Frame</c> hook): release a bed order that has done its work (healed, never reached, no healing), then send a wounded character
/// to bed. Its timers count game seconds where the original counts real milliseconds. The tracking is runtime state, not saved: after a load the
/// rules pick each character up again from what it is doing. Each pass (and when the world is ready) also publishes what the bed desire filter
/// (<see cref="BedDesire"/>, registered with the chooser) reads.
/// </summary>
sealed class SleepSystem(SleepSettings settings, PopulationData population) : IChooserOwner, IWorldReady
{
    /// <summary>
    /// Ticks between passes.
    /// </summary>
    const int PassTicks = 30;

    /// <summary>
    /// The rise in the rating that counts as healing for <c>stuckRelease</c> (the original's 0.005).
    /// </summary>
    const float HealingStep = 0.005f;

    /// <summary>
    /// What the rules remember per character.
    /// </summary>
    readonly Dictionary<CharacterId, Watch> _watches = [];

    /// <summary>
    /// What the desire filter reads; null before the first pass.
    /// </summary>
    volatile BedTable? _table;

    public void Register(ChooserRegistry chooser) => chooser.DesireFilter(new BedDesire(() => _table));

    public void Ready(World world, bool fromSave) => Publish(world, RepairBeds(world));

    public void SlowWorld(World world)
    {
        if (world.Tick % PassTicks != 0) return;

        float seconds = PassTicks * world.TickSeconds;
        var repairBeds = RepairBeds(world);
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Medical: { Dead: false } } cold) continue;

            var id = table.IdOf(slot);
            var watch = _watches.TryGetValue(id, out var w) ? w : _watches[id] = new Watch();
            Step(world, slot, id, cold, watch, seconds, repairBeds);
        }

        Forget(table);
        Publish(world, repairBeds);
    }

    #region Desire table

    /// <summary>
    /// Publishes the settings and each living player character's state for <see cref="BedDesire"/>.
    /// </summary>
    void Publish(World world, IReadOnlyList<Fixture> repairBeds)
    {
        var rules = new BedRules(settings.BedMult, settings.ForceBed, settings.GetUpMult, settings.StayInBed, settings.OwnBedsOnly);
        var states = new Dictionary<CharacterId, BedState>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Medical: { Dead: false } } cold) continue;

            states[table.IdOf(slot)] = StateOf(world, slot, cold, repairBeds, rules);
        }

        _table = new(rules, states.ToFrozenDictionary());
    }

    /// <summary>
    /// One character's state: eligible as the original's hooks test it (not carried; not in combat with <c>skipCombat</c>), healed, an
    /// unbandaged cut (<c>waitForTreatment</c>), a free bed of its faction about (searched only when the bed goal's floor can apply), in a bed.
    /// </summary>
    BedState StateOf(World world, int slot, CharacterCold cold, IReadOnlyList<Fixture> repairBeds, BedRules rules)
    {
        var medical = cold.Medical!;
        bool hasRepairBed = repairBeds.Any(f => f.Owner == cold.Faction);
        bool healed = !BodyRating.Wounded(BodyRating.Rating(medical, cold.Race, settings, hasRepairBed), settings);
        bool eligible = cold.CarriedBy.IsNone && !(settings.SkipCombat && cold.InCombat);
        bool unbandaged = settings.WaitForTreatment && BodyRating.UnbandagedCuts(medical, cold.Race);
        bool searched = eligible && !healed && !unbandaged && rules.OwnBedsOnly && rules.ForceBed > 0;
        bool ownBed = searched && world.Fixtures.FindBed(world, slot, population.Relations, f => f.Owner != cold.Faction) is not null;
        return new(eligible, healed, unbandaged, ownBed, cold.InSomething == HeldState.Bed);
    }

    #endregion

    /// <summary>
    /// One character's pass: the releases, then the send.
    /// </summary>
    void Step(World world, int slot, CharacterId id, CharacterCold cold, Watch watch, float seconds, IReadOnlyList<Fixture> repairBeds)
    {
        var medical = cold.Medical!;
        bool hasRepairBed = repairBeds.Any(f => f.Owner == cold.Faction);
        float rating = BodyRating.Rating(medical, cold.Race, settings, hasRepairBed);
        bool wounded = BodyRating.Wounded(rating, settings);
        var order = BedOrder(cold);

        if (order is not null && Release(world, cold, watch, rating, seconds, order, hasRepairBed))
        {
            Get(world, slot, id);
            return;
        }

        if (order is null) watch.Reset();
        if (settings.AutoBedOrder && wounded && order is null && MaySend(world, id, cold, watch))
            Send(world, slot, id, cold, watch, repairBeds);
    }

    /// <summary>
    /// Whether the bed order <paramref name="order"/> is to be released: to switch a robot due for repair out of a normal bed, after
    /// <c>stuckRelease</c> seconds without healing, after <c>bedReachTimeout</c> seconds without lying down, or once healed when it was held while
    /// wounded (<c>clearBedOrder</c>, whoever gave it).
    /// </summary>
    bool Release(World world, CharacterCold cold, Watch watch, float rating, float seconds, string order, bool hasRepairBed)
    {
        bool wounded = BodyRating.Wounded(rating, settings);
        if (wounded) watch.HeldWounded = true;

        if (wounded && hasRepairBed && !watch.Switched && IsNormalBed(world, order)
                && BodyRating.NeedsRepair(cold.Medical!, cold.Race, settings))
        {
            watch.Switched = true;
            return true;
        }

        if (wounded && Stuck(watch, rating, seconds)) return true;
        if (Unreached(cold, watch, seconds, order)) return true;

        return !wounded && settings.ClearBedOrder && watch.HeldWounded;
    }

    /// <summary>
    /// <c>stuckRelease</c>: the rating has not risen by 0.005 for that many seconds of the bed order.
    /// </summary>
    bool Stuck(Watch watch, float rating, float seconds)
    {
        if (settings.StuckRelease <= 0) return false;

        if (watch.Best < 0 || rating > watch.Best + HealingStep)
        {
            watch.Best = rating;
            watch.Unhealed = 0;
            return false;
        }

        watch.Unhealed += seconds;
        return watch.Unhealed > settings.StuckRelease;
    }

    /// <summary>
    /// <c>bedReachTimeout</c>: the bed order has run that many seconds without the character lying in a bed; the bed is passed over next time.
    /// </summary>
    bool Unreached(CharacterCold cold, Watch watch, float seconds, string order)
    {
        if (settings.BedReachTimeout <= 0 || cold.InSomething == HeldState.Bed)
        {
            watch.Walking = 0;
            return false;
        }

        watch.Walking += seconds;
        if (watch.Walking <= settings.BedReachTimeout) return false;

        watch.GaveUp = order;
        return true;
    }

    /// <summary>
    /// The order queue's bed order's bed, or null when it holds none.
    /// </summary>
    static string? BedOrder(CharacterCold cold)
    {
        foreach (var entry in cold.OrderQueue)
        {
            if (entry.Row == TaskType.UseBed) return entry.Site ?? "";
        }

        return null;
    }

    /// <summary>
    /// Whether the bed <paramref name="id"/> is a normal bed (BF_BED).
    /// </summary>
    static bool IsNormalBed(World world, string id) => world.Fixtures.Find(id) is { Kind: FixtureKind.Bed } f && f.Bed.Function == TownBuildings.BedFunction;

    /// <summary>
    /// The release: a move order to where the character is, so a sleeper gets up and a walker stops; its jobs take it back.
    /// </summary>
    static void Get(World world, int slot, CharacterId id) =>
        world.GiveOrder(id, TaskType.MoveCusOrdered, point: world.Characters.Next[slot].Position);

    /// <summary>
    /// Whether the character may be sent: not selected, carried, held, out cold or (with <c>skipCombat</c>) fighting, no move order of the player's
    /// running (<c>respectMoveOrder</c>), no unbandaged cut (<c>waitForTreatment</c>), and the cooldown passed since its last order.
    /// </summary>
    bool MaySend(World world, CharacterId id, CharacterCold cold, Watch watch)
    {
        if (world.Player.Selection.Contains(id)) return false;
        if (!cold.CarriedBy.IsNone || cold.InSomething != HeldState.None || cold.Medical!.Incapacitated) return false;
        if (settings.SkipCombat && cold.InCombat) return false;
        if (settings.RespectMoveOrder && cold.OrderQueue.Any(o => o.Row == TaskType.MoveCusOrdered)) return false;
        if (settings.WaitForTreatment && BodyRating.UnbandagedCuts(cold.Medical!, cold.Race)) return false;

        long cooldown = (long)MathF.Ceiling(settings.BedOrderCooldownMs / 1000f / world.TickSeconds);
        return watch.LastOrder < 0 || world.Tick - watch.LastOrder >= cooldown;
    }

    /// <summary>
    /// The bed order to the bed the rules pick; nothing when none is free.
    /// </summary>
    void Send(World world, int slot, CharacterId id, CharacterCold cold, Watch watch, IReadOnlyList<Fixture> repairBeds)
    {
        var bed = BodyRating.NeedsRepair(cold.Medical!, cold.Race, settings)
            ? NearestRepairBed(world, slot, cold, repairBeds)
            : world.Fixtures.FindBed(world, slot, population.Relations, f => PassedOver(world, cold, watch, f));
        if (bed is null) return;

        world.GiveOrder(id, TaskType.UseBed, site: bed.Id);
        watch.LastOrder = world.Tick;
        watch.Target = bed.Id;
    }

    /// <summary>
    /// A bed the search leaves out: another faction's (<c>ownBedsOnly</c>), one another watched character is sent to, or the one this character
    /// gave up on.
    /// </summary>
    bool PassedOver(World world, CharacterCold cold, Watch watch, Fixture bed)
    {
        if (settings.OwnBedsOnly && bed.Owner != cold.Faction) return true;
        if (bed.Id == watch.GaveUp) return true;

        return _watches.Values.Any(w => w != watch && w.Target == bed.Id);
    }

    /// <summary>
    /// The nearest free Skeleton Repair Bed of the character's faction.
    /// </summary>
    static Fixture? NearestRepairBed(World world, int slot, CharacterCold cold, IReadOnlyList<Fixture> repairBeds)
    {
        var at = world.Characters.Next[slot].Position;
        return repairBeds
            .Where(f => f.Owner == cold.Faction && FixtureRegistry.IsFree(world.Characters, f))
            .OrderBy(f => System.Numerics.Vector3.DistanceSquared(f.Position, at))
            .FirstOrDefault();
    }

    /// <summary>
    /// The Skeleton Repair Beds (BF_SKELETON_BED) of the world, in registry order.
    /// </summary>
    static List<Fixture> RepairBeds(World world) =>
        [.. world.Fixtures.All.Where(f => f.Kind == FixtureKind.Bed && f.Owner >= 0 && f.Bed.Function == TownBuildings.SkeletonBedFunction)];

    /// <summary>
    /// Drops the watches of characters no longer alive.
    /// </summary>
    void Forget(CharacterTable table)
    {
        foreach (var id in _watches.Keys.Where(id => !table.IsAlive(id)).ToList()) _watches.Remove(id);
    }

    /// <summary>
    /// What the rules remember of one character.
    /// </summary>
    sealed class Watch
    {
        /// <summary>
        /// The tick of the mod's last bed order to it; -1 for none.
        /// </summary>
        public long LastOrder { get; set; } = -1;

        /// <summary>
        /// The bed it was last sent to.
        /// </summary>
        public string? Target { get; set; }

        /// <summary>
        /// The bed it last gave up reaching.
        /// </summary>
        public string? GaveUp { get; set; }

        /// <summary>
        /// Whether the current bed order was held while it was wounded.
        /// </summary>
        public bool HeldWounded { get; set; }

        /// <summary>
        /// Whether a robot's normal-bed order was already released for a repair bed.
        /// </summary>
        public bool Switched { get; set; }

        /// <summary>
        /// The best rating seen during the bed order; -1 before the first.
        /// </summary>
        public float Best { get; set; } = -1;

        /// <summary>
        /// Seconds since the rating last rose.
        /// </summary>
        public float Unhealed { get; set; }

        /// <summary>
        /// Seconds the bed order has run without the character lying down.
        /// </summary>
        public float Walking { get; set; }

        /// <summary>
        /// Clears what a bed order tracks once none is held.
        /// </summary>
        public void Reset()
        {
            HeldWounded = false;
            Switched = false;
            Best = -1;
            Unhealed = 0;
            Walking = 0;
            Target = null;
        }
    }
}
