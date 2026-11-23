using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

namespace Meitou.Sparring;

/// <summary>
/// A spar between two of the player's characters: <see cref="A"/> the selected one, <see cref="B"/> the one right-clicked. <see cref="Ended"/> is
/// the tick it ended (-1 while it runs); the orders are each one's Hold and Passive toggles before the spar.
/// </summary>
sealed record Spar(CharacterId A, CharacterId B, long Started, CharacterOrders OrdersA, CharacterOrders OrdersB)
{
    /// <summary>
    /// The tick the spar ended; -1 while it runs.
    /// </summary>
    public long Ended { get; set; } = -1;

    /// <summary>
    /// Ticks in a row either has been out of the fight.
    /// </summary>
    public long OutTicks { get; set; }

    /// <summary>
    /// Ticks in a row <see cref="A"/> has been moving with another target.
    /// </summary>
    public long AwayTicks { get; set; }

    /// <summary>
    /// Whether <paramref name="id"/> is one of the pair.
    /// </summary>
    public bool Has(CharacterId id) => id == A || id == B;

    /// <summary>
    /// Whether <paramref name="x"/> and <paramref name="y"/> are the pair, either way round.
    /// </summary>
    public bool Pairs(CharacterId x, CharacterId y) => x == A && y == B || x == B && y == A;

    /// <summary>
    /// The other one of the pair.
    /// </summary>
    public CharacterId PartnerOf(CharacterId id) => id == A ? B : A;
}

/// <summary>
/// Runs the spars (the original's rules, Sparring.dll decompiled): a <see cref="SparOrder"/> clears both characters' Hold and Passive toggles and
/// gives each an attack order (as the player's pick) on the other; their blows on each other do half damage, and both are healed completely when
/// either is probably dying. The spar ends, both stopped and their toggles restored, when either is down or carried, out of the fight for a
/// while, or when the first walks off at another target. As the original's <c>isEnemy</c> / <c>isAlly</c> hooks, a sparring character is nobody's
/// enemy but its partner's and takes nobody else for one (<see cref="ICombatEngageFilter"/>), unless the player picked the target.
/// </summary>
sealed class SparSystem(SparringSettings settings, PopulationData population) : ICombatHitFilter, ICombatEngageFilter, IStateHashed
{
    /// <summary>
    /// The original's damage multiplier on a blow between the pair.
    /// </summary>
    const float DamageMultiplier = 0.5f;

    /// <summary>
    /// The original's speed (u/s) above which the first character counts as walking off.
    /// </summary>
    const float AwaySpeed = 0.5f;

    /// <summary>
    /// Seconds after the start before the out-of-fight rule, then how long out of the fight ends it; seconds before the walking-off rule, then how
    /// long walking off ends it. The original counts character updates (60, 30, 120, 90); these are this port's times for them.
    /// </summary>
    const float OutGrace = 2, OutSeconds = 1, AwayGrace = 4, AwaySeconds = 1.5f;

    /// <summary>
    /// Seconds an ended pair's blows on each other stay cancelled (a swing already under way; this port's).
    /// </summary>
    const float AfterSeconds = 5;

    /// <summary>
    /// Ticks between checks.
    /// </summary>
    const int CheckTicks = 5;

    /// <summary>
    /// The spars running or ended within <see cref="AfterSeconds"/>, in start order.
    /// </summary>
    readonly List<Spar> _spars = [];

    /// <summary>
    /// Whether <paramref name="id"/> is in a running spar, and with whom.
    /// </summary>
    public bool TryPartner(CharacterId id, out CharacterId partner)
    {
        foreach (var s in _spars)
        {
            if (s.Ended < 0 && s.Has(id))
            {
                partner = s.PartnerOf(id);
                return true;
            }
        }

        partner = CharacterId.None;
        return false;
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is SparOrder order && settings.Enabled) Start(world, order);
        }
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % CheckTicks != 0) return;

        foreach (var spar in _spars.ToArray()) Check(world, spar);
    }

    public void HitLanding(World world, ref PendingHit hit)
    {
        if (hit.Source != CombatHitSource.Melee || Find(hit.Attacker, hit.Victim) is not { } spar) return;

        if (spar.Ended >= 0 || Down(world, hit.Victim))
        {
            hit.Cancelled = true;
            return;
        }

        var p = hit.Packet;
        p = p with { Cut = p.Cut * DamageMultiplier, Blunt = p.Blunt * DamageMultiplier, Pierce = p.Pierce * DamageMultiplier };
        p = p with { Stun = p.Stun * DamageMultiplier };
        if (settings.CutToBlunt) p = p with { Blunt = p.Blunt + p.Cut, Cut = 0 };
        hit.Packet = p;
    }

    /// <summary>
    /// Refuses a fight start or a target choice between a sparring character and anyone but its partner, unless the player picked the target.
    /// Any thread: reads the spars only, which change in serial phases.
    /// </summary>
    public bool Allows(World world, in EngageAsk ask)
    {
        if (!settings.Enabled || ask.Picked) return true;

        return !Outside(ask.Attacker, ask.Target);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_spars.Count);
        foreach (var s in _spars)
        {
            hasher.Add(s.A);
            hasher.Add(s.B);
            hasher.Add(s.Started);
            hasher.Add(s.Ended);
            hasher.Add(s.OutTicks);
            hasher.Add(s.AwayTicks);
        }
    }

    #region Start and end

    /// <summary>
    /// Starts the spar of the order's pair: any spar either is in ends first.
    /// </summary>
    void Start(World world, SparOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Challenger, out int a) || !table.TryResolveNext(order.Partner, out int b) || a == b) return;
        if (table.Cold(a) is not { } ac || table.Cold(b) is not { } bc || !Able(ac) || !Able(bc)) return;

        foreach (var old in _spars.Where(s => s.Ended < 0 && (s.Has(order.Challenger) || s.Has(order.Partner))).ToList()) End(world, old);

        _spars.Add(new Spar(order.Challenger, order.Partner, world.Tick, ClearToggles(ac), ClearToggles(bc)));
        world.Defer(new AttackOrder([order.Challenger], order.Partner) { Tick = world.Tick + 1, Picked = true, Cause = "a spar" });
        world.Defer(new AttackOrder([order.Partner], order.Challenger) { Tick = world.Tick + 1, Picked = true, Cause = "a spar" });
    }

    /// <summary>
    /// A player character awake, alive, able to fight, not carried, carrying or held.
    /// </summary>
    static bool Able(CharacterCold c) =>
        c is { IsPlayer: true, Fighter: not null, Medical.Incapacitated: false, CarriedBy.IsNone: true, Carrying.IsNone: true, IsImprisoned: false };

    /// <summary>
    /// The original sets standing orders 12 (Hold) and 13 (Passive) off for the spar: clears them and answers what they were.
    /// </summary>
    static CharacterOrders ClearToggles(CharacterCold c)
    {
        var kept = c.Orders & (CharacterOrders.Hold | CharacterOrders.Passive);
        c.Orders &= ~(CharacterOrders.Hold | CharacterOrders.Passive);
        if (kept.HasFlag(CharacterOrders.Hold)) c.HoldPoint = null;
        return kept;
    }

    /// <summary>
    /// Ends a spar: both stop and get their toggles back (Hold on again holds where it stands, as standing order 12 does).
    /// </summary>
    void End(World world, Spar spar)
    {
        spar.Ended = world.Tick;
        Restore(world, spar.A, spar.OrdersA);
        Restore(world, spar.B, spar.OrdersB);
        world.Defer(new StopCommand([spar.A, spar.B]) { Tick = world.Tick + 1 });
    }

    /// <summary>
    /// Puts back the Hold and Passive toggles <paramref name="kept"/> of <paramref name="id"/>.
    /// </summary>
    static void Restore(World world, CharacterId id, CharacterOrders kept)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { } c) return;

        c.Orders |= kept;
        if (kept.HasFlag(CharacterOrders.Hold)) c.HoldPoint = table.Next[slot].Position;
    }

    #endregion

    #region Checks

    /// <summary>
    /// Ends a running spar that is over and heals a pair one of which is dying; forgets an ended spar once its grace has passed.
    /// </summary>
    void Check(World world, Spar spar)
    {
        if (spar.Ended >= 0)
        {
            if (world.Tick - spar.Ended > AfterSeconds / world.TickSeconds) _spars.Remove(spar);
            return;
        }

        if (Over(world, spar))
        {
            End(world, spar);
            return;
        }

        if (ProbablyDying(world, spar.A) || ProbablyDying(world, spar.B))
        {
            HealCompletely(world, spar.A);
            HealCompletely(world, spar.B);
        }
    }

    /// <summary>
    /// Whether the spar is over: either is down or carried; after the grace either has been out of the fight for a while; or the first has been
    /// moving faster than 0.5 u/s with another target for a while.
    /// </summary>
    bool Over(World world, Spar spar)
    {
        if (Down(world, spar.A) || Down(world, spar.B)) return true;

        long elapsed = world.Tick - spar.Started;
        var table = world.Characters;
        if (elapsed > OutGrace / world.TickSeconds)
        {
            bool outOfFight = !InFight(table, spar.A, spar.B) || !InFight(table, spar.B, spar.A);
            spar.OutTicks = outOfFight ? spar.OutTicks + CheckTicks : 0;
            if (spar.OutTicks > OutSeconds / world.TickSeconds) return true;
        }

        if (elapsed <= AwayGrace / world.TickSeconds) return false;

        spar.AwayTicks = WalkingOff(table, spar.A, spar.B) ? spar.AwayTicks + CheckTicks : 0;
        return spar.AwayTicks > AwaySeconds / world.TickSeconds;
    }

    /// <summary>
    /// The stand-in for the original's combat mode: in the combat stance, running at a target, or on an attack of <paramref name="other"/>.
    /// </summary>
    static bool InFight(CharacterTable table, CharacterId me, CharacterId other)
    {
        if (!table.TryResolveNext(me, out int slot) || table.Cold(slot) is not { } c) return false;

        return c.InCombat || c.CombatRunning || Attacking(table, slot, c, other);
    }

    /// <summary>
    /// Whether <paramref name="me"/> moves faster than 0.5 u/s while its attack target is not <paramref name="other"/>.
    /// </summary>
    static bool WalkingOff(CharacterTable table, CharacterId me, CharacterId other)
    {
        if (!table.TryResolveNext(me, out int slot) || table.Cold(slot) is not { } c) return false;

        return table.Next[slot].Velocity.Length() > AwaySpeed && !Attacking(table, slot, c, other);
    }

    /// <summary>
    /// Whether the character runs an attack on <paramref name="other"/>.
    /// </summary>
    static bool Attacking(CharacterTable table, int slot, CharacterCold c, CharacterId other) =>
        table.Next[slot].Task == (byte)CharacterTask.Attack && c.Running?.Target == other;

    /// <summary>
    /// Gone, dead, unconscious or carried.
    /// </summary>
    static bool Down(World world, CharacterId id) =>
        !world.Characters.TryResolveNext(id, out int slot)
        || world.Characters.Cold(slot) is not { Medical.Incapacitated: false, CarriedBy.IsNone: true };

    #endregion

    #region Healing

    /// <summary>
    /// The stand-in for the original's <c>isProbablyDying</c>: blood at or under 0, or a vital part's flesh with its bandage under 0.
    /// </summary>
    static bool ProbablyDying(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot)?.Medical is not { Dead: false } medical) return false;

        return medical.Blood <= 0 || medical.Parts.Any(p => p.IsVital && p.Flesh + p.Bandage < 0);
    }

    /// <summary>
    /// The original's <c>healCompletely</c>: every part whole, full blood, no wounds or knock-out (<see cref="MedicalState.Heal"/>).
    /// </summary>
    void HealCompletely(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { Medical: { } medical, Race: { } race } c) return;

        var context = new MedicalContext(population.Bodies.Constants, population.BodyOptions, race)
        {
            Toughness = c.Stats?.ScaledToughness ?? 0,
            Strength = c.Stats?.Strength ?? 50,
            HasStats = c.Stats is not null,
            Seed = world.Seed,
            CharacterKey = Rng.Key(id),
        };
        medical.Heal(context);
    }

    #endregion

    /// <summary>
    /// Whether a fight of <paramref name="attacker"/> on <paramref name="target"/> involves a sparring character and is not the spar itself.
    /// </summary>
    bool Outside(CharacterId attacker, CharacterId target)
    {
        if (TryPartner(target, out var partner) && partner != attacker) return true;

        return TryPartner(attacker, out var own) && own != target;
    }

    /// <summary>
    /// The spar of the pair <paramref name="x"/> and <paramref name="y"/>, running or ended.
    /// </summary>
    Spar? Find(CharacterId x, CharacterId y)
    {
        foreach (var s in _spars)
        {
            if (s.Pairs(x, y)) return s;
        }

        return null;
    }
}
