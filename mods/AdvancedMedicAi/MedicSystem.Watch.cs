using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// What the pulse remembers (the original's lists, world-local): when each player character last got an order the mod did not give, the
/// cooldowns of medics and patients, patients blocked after a failed approach, and the orders waiting for their approach check.
/// </summary>
internal sealed partial class MedicSystem
{
    /// <summary>
    /// An order the mod gave: its patient, when, and the squared distance then.
    /// </summary>
    internal readonly record struct Pending(CharacterId Patient, double At, float StartSq);

    readonly Dictionary<CharacterId, double> _manual = [];
    readonly Dictionary<CharacterId, double> _targetCooldown = [];
    readonly Dictionary<CharacterId, double> _actorCooldown = [];
    readonly Dictionary<CharacterId, double> _blockedUntil = [];
    readonly Dictionary<CharacterId, Pending> _pending = [];
    bool _ordering;

    /// <summary>
    /// The door rows of the original's door test: an order of one of these is no manual order, but one given to a medic soon after its medical
    /// order means its path went through a door, and the patient is blocked.
    /// </summary>
    static readonly HashSet<int> DoorRows =
    [
        0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x7b, 0x8c, 0x9b, 0xa2, 0xa3, 0xc3, 0xc5, 0xc6, 199,
        0xc9, 0xe2, 0x11d, 0x11e, 0x11f, 0x122,
    ];

    /// <summary>
    /// An order was queued for a character (the Harmony postfix on the player's order queue): a player character's order not given by the mod
    /// is a manual order; a door order soon after the mod's order blocks that order's patient.
    /// </summary>
    internal void OrderGiven(World world, int slot, TaskType row)
    {
        if (_ordering || world.Characters.Cold(slot) is not { IsPlayer: true }) return;

        var id = world.Characters.IdOf(slot);
        double now = Now(world);
        if (!DoorRows.Contains((int)row))
        {
            _manual[id] = now;
            return;
        }

        if (_tuning.DoorBlocking && _pending.TryGetValue(id, out var pending) && now - pending.At <= _tuning.DoorWindowSeconds)
            Block(pending.Patient, now, _tuning.DoorBlockSeconds);
    }

    void Block(CharacterId patient, double now, float seconds) => _blockedUntil[patient] = now + seconds;

    /// <summary>
    /// Drops the times no rule reads any more (the longest grace is the settings' 300 s).
    /// </summary>
    void Prune(double now)
    {
        Drop(_manual, at => now - at >= 300);
        Drop(_targetCooldown, at => now - at >= _tuning.TargetCooldownSeconds);
        Drop(_actorCooldown, at => now - at >= _tuning.ActorCooldownSeconds);
        Drop(_blockedUntil, until => now >= until);
    }

    static void Drop(Dictionary<CharacterId, double> times, Func<double, bool> stale)
    {
        foreach (var id in times.Where(t => stale(t.Value)).Select(t => t.Key).ToList()) times.Remove(id);
    }

    bool Blocked(CharacterId patient, double now) => _blockedUntil.TryGetValue(patient, out double until) && now < until;

    /// <summary>
    /// The approach check <see cref="MedicTuning.StuckCheckSeconds"/> after each order: a medic that started far enough and came no closer by
    /// either measure has its patient blocked.
    /// </summary>
    void CheckPending(World world, double now)
    {
        if (_pending.Count == 0) return;

        foreach (var (medic, pending) in _pending.OrderBy(p => p.Key.Slot).ToList())
        {
            if (now - pending.At < _tuning.StuckCheckSeconds) continue;

            _pending.Remove(medic);
            if (_tuning.StuckBlocking && Stuck(world, medic, pending)) Block(pending.Patient, now, _tuning.StuckBlockSeconds);
        }
    }

    bool Stuck(World world, CharacterId medic, in Pending pending)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(medic, out int m) || !table.TryResolveNext(pending.Patient, out int p)) return false;
        if (pending.StartSq < _tuning.MinStuckDistanceSq) return false;

        float nowSq = System.Numerics.Vector3.DistanceSquared(table.Next[m].Position, table.Next[p].Position);
        float progress = pending.StartSq - nowSq;
        return progress < _tuning.MinProgressSq && progress / pending.StartSq < _tuning.MinProgressRatio;
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_nextPulse);
        hasher.Add(_quietUntil);
        Add(ref hasher, _manual);
        Add(ref hasher, _targetCooldown);
        Add(ref hasher, _actorCooldown);
        Add(ref hasher, _blockedUntil);

        hasher.Add(_pending.Count);
        foreach (var (medic, pending) in _pending.OrderBy(p => p.Key.Slot))
        {
            hasher.Add(medic);
            hasher.Add(pending.Patient);
            hasher.Add(pending.At);
            hasher.Add(pending.StartSq);
        }
    }

    static void Add(ref StateHasher hasher, Dictionary<CharacterId, double> times)
    {
        hasher.Add(times.Count);
        foreach (var (id, at) in times.OrderBy(t => t.Key.Slot).ThenBy(t => t.Key.Generation))
        {
            hasher.Add(id);
            hasher.Add(at);
        }
    }
}
