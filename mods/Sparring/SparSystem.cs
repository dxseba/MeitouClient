using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Movement;

namespace Meitou.Sparring;

/// <summary>A spar between two of the player's characters; <see cref="Ended"/> is the tick it ended (-1 while it runs).</summary>
sealed record Spar(CharacterId A, CharacterId B, long Started)
{
    public long Ended { get; set; } = -1;

    public bool Has(CharacterId id) => id == A || id == B;

    public bool Pairs(CharacterId x, CharacterId y) => x == A && y == B || x == B && y == A;

    public CharacterId PartnerOf(CharacterId id) => id == A ? B : A;
}

/// <summary>
/// Runs the spars: a <see cref="SparOrder"/> gives both characters an attack order (as the player's pick) on each other; the spar ends, with both
/// stopped, once either is down or neither has fought the other for a while. As a hit filter it cancels blows between a pair once one is down or the
/// spar has ended, and turns a sparring blow's cut into blunt with <see cref="SparringSettings.CutToBlunt"/>.
/// </summary>
sealed class SparSystem(SparringSettings settings) : ICombatHitFilter, IStateHashed
{
    /// <summary>Seconds a spar may go on without either fighting the other (walking over, a move order) before it ends.</summary>
    const float IdleSeconds = 8;

    /// <summary>Seconds an ended pair's blows on each other stay cancelled (a swing already under way).</summary>
    const float AfterSeconds = 5;

    readonly List<Spar> _spars = [];

    /// <summary>Whether <paramref name="id"/> is in a running spar, and with whom.</summary>
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
        if (world.Tick % 5 != 0) return;

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

        if (settings.CutToBlunt) hit.Packet = hit.Packet with { Blunt = hit.Packet.Blunt + hit.Packet.Cut, Cut = 0 };
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
        }
    }

    void Start(World world, SparOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Challenger, out int a) || !table.TryResolveNext(order.Partner, out int b) || a == b) return;
        if (table.Cold(a) is not { } ac || table.Cold(b) is not { } bc || !Able(ac) || !Able(bc) || ac.Faction != bc.Faction) return;

        _spars.RemoveAll(s => s.Has(order.Challenger) || s.Has(order.Partner));
        _spars.Add(new Spar(order.Challenger, order.Partner, world.Tick));

        world.Defer(new AttackOrder([order.Challenger], order.Partner) { Tick = world.Tick + 1, Picked = true, Cause = "a spar" });
        world.Defer(new AttackOrder([order.Partner], order.Challenger) { Tick = world.Tick + 1, Picked = true, Cause = "a spar" });
        world.Post($"{ac.Name} and {bc.Name} start sparring.");
    }

    /// <summary>A player character awake, alive, able to fight and not carried.</summary>
    static bool Able(CharacterCold c) =>
        c is { IsPlayer: true, Fighter: not null, Medical.Incapacitated: false, CarriedBy.IsNone: true, IsImprisoned: false };

    /// <summary>Ends a running spar that is over, and forgets an ended one once its grace has passed.</summary>
    void Check(World world, Spar spar)
    {
        if (spar.Ended >= 0)
        {
            if (world.Tick - spar.Ended > AfterSeconds / world.TickSeconds) _spars.Remove(spar);
            return;
        }

        if (Over(world, spar) is { } reason) End(world, spar, reason);
    }

    /// <summary>Why the spar is over (a message), or null while it goes on.</summary>
    string? Over(World world, Spar spar)
    {
        var table = world.Characters;
        if (Down(world, spar.A) || Down(world, spar.B)) return $"{Winner(world, spar)} wins the spar.";

        bool idle = !Fighting(table, spar.A, spar.B) && !Fighting(table, spar.B, spar.A);
        if (idle && world.Tick - spar.Started > IdleSeconds / world.TickSeconds)
            return $"{table.NameOf(spar.A)} and {table.NameOf(spar.B)} stop sparring.";

        return null;
    }

    void End(World world, Spar spar, string reason)
    {
        spar.Ended = world.Tick;
        world.Defer(new StopCommand([spar.A, spar.B]) { Tick = world.Tick + 1 });
        world.Post(reason);
    }

    /// <summary>The one still standing; an empty name when both are down.</summary>
    static string Winner(World world, Spar spar)
    {
        var table = world.Characters;
        if (!Down(world, spar.A)) return table.NameOf(spar.A);

        return Down(world, spar.B) ? "Nobody" : table.NameOf(spar.B);
    }

    /// <summary>Whether <paramref name="me"/> runs an attack on <paramref name="other"/>.</summary>
    static bool Fighting(CharacterTable table, CharacterId me, CharacterId other) =>
        table.TryResolveNext(me, out int slot) && table.Next[slot].Task == (byte)CharacterTask.Attack && table.Cold(slot)?.Running?.Target == other;

    /// <summary>Gone, dead or unconscious.</summary>
    static bool Down(World world, CharacterId id) =>
        !world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { Medical.Incapacitated: false };

    Spar? Find(CharacterId x, CharacterId y)
    {
        foreach (var s in _spars)
        {
            if (s.Pairs(x, y)) return s;
        }

        return null;
    }
}
