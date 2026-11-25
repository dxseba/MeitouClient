using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.DesolationSystem;

/// <summary>Where a queued wound goes.</summary>
enum Target : byte
{
    /// <summary>Instant blood loss (Katana Tear).</summary>
    Blood,
    /// <summary>Blunt on the head (Blunt Impact).</summary>
    Head,
    /// <summary>Blunt on the first torso part, the chest (Shockwave, a riposte).</summary>
    Chest,
    /// <summary>Blunt on each arm, full damage per arm (recoil, a riposte).</summary>
    Arms,
    /// <summary>Cut on one part by index, through armour (Universal Mastery's true damage).</summary>
    Part,
    /// <summary>A knock-out of that many seconds (Cybernetic Overlord's machine stun).</summary>
    Ko,
}

/// <summary>
/// The wounds the hooks queue (they may not change characters) and the timed states, dealt and expired in <see cref="SlowWorld"/> of the same tick.
/// </summary>
sealed partial class DesoSystem
{
    readonly List<(CharacterId Who, Target Where, float Amount, int Part)> _queued = [];
    readonly Dictionary<CharacterId, Slow> _slows = [];
    readonly Dictionary<CharacterId, long> _bursts = [];
    readonly Dictionary<CharacterId, List<(CharacterId Foe, long Tick)>> _attackers = [];
    bool _timedDirty;

    /// <summary>Set while the mod deals its own wounds, so Iron Guard leaves them alone.</summary>
    [ThreadStatic]
    internal static bool Dealing;

    void Queue(CharacterId who, Target where, float amount, int part = -1)
    {
        if (amount > 0 && float.IsFinite(amount)) _queued.Add((who, where, amount, part));
    }

    void ApplyQueued(World world)
    {
        Dealing = true;
        try
        {
            foreach (var (who, where, amount, part) in _queued) Deal(world, who, where, amount, part);
        }
        finally
        {
            Dealing = false;
            _queued.Clear();
        }

        Expire(world);
        if (!_timedDirty) return;

        _timedDirty = false;
        PublishTimed();
    }

    void Deal(World world, CharacterId who, Target where, float amount, int part)
    {
        if (!world.Characters.TryResolveNext(who, out int slot) || world.Characters.Cold(slot) is not { Medical: { Dead: false } medical } cold) return;
        if (cold.Race is not { } race) return;

        if (where == Target.Blood)
        {
            medical.Blood -= amount;
            return;
        }

        if (where == Target.Ko)
        {
            medical.KnockOut(amount);
            return;
        }

        var ctx = Context(world, who, cold, race);
        if (where == Target.Part)
        {
            if ((uint)part < (uint)medical.Parts.Count) medical.ApplyHit(part, new HitDamage(amount, 0, 0), ctx);
            return;
        }

        foreach (int index in Parts(medical, where)) medical.ApplyHit(index, new HitDamage(0, amount, 0), ctx);
    }

    MedicalContext Context(World world, CharacterId who, CharacterCold cold, RaceData race) =>
        new(population.Bodies.Constants, population.BodyOptions, race)
        {
            Toughness = cold.Stats?.ScaledToughness ?? 0,
            Strength = cold.Stats?.Strength ?? 50,
            HasStats = cold.Stats is not null,
            Seed = world.Seed,
            CharacterKey = Rng.Key(who),
        };

    static IEnumerable<int> Parts(MedicalState medical, Target where)
    {
        var parts = medical.Parts;
        var type = where switch { Target.Head => BodyPartType.Head, Target.Chest => BodyPartType.Torso, _ => BodyPartType.Arm };
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Template.Type != type) continue;

            yield return i;
            if (where != Target.Arms) yield break;
        }
    }

    void Expire(World world)
    {
        long now = world.Tick;
        foreach (var id in _slows.Where(x => x.Value.Until <= now).Select(x => x.Key).ToList()) Drop(_slows, id);
        foreach (var id in _bursts.Where(x => x.Value <= now).Select(x => x.Key).ToList()) Drop(_bursts, id);
        if (now % 300 != 0) return;

        long since = now - Ticks(world, CounterWindow);
        foreach (var id in _attackers.Where(x => x.Value.All(e => e.Tick < since)).Select(x => x.Key).ToList()) _attackers.Remove(id);
    }

    void Drop<T>(Dictionary<CharacterId, T> map, CharacterId id)
    {
        map.Remove(id);
        _timedDirty = true;
    }

    void HashTimed(ref StateHasher hasher)
    {
        hasher.Add(_queued.Count);
        foreach (var (id, slow) in _slows.OrderBy(x => x.Key.Slot))
        {
            hasher.Add(id.Slot);
            hasher.Add(slow.Amount);
            hasher.Add(slow.Until);
        }

        foreach (var (id, until) in _bursts.OrderBy(x => x.Key.Slot))
        {
            hasher.Add(id.Slot);
            hasher.Add(until);
        }
    }
}
