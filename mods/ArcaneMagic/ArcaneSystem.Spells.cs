using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.ArcaneMagic;

/// <summary>What the renderer draws of a spell under way (README "Facts"): its kind, where, which way, and its age in its shown part.</summary>
public enum ShapeKind
{
    Spike,
    Whirlwind,
    BombFlying,
    Burst,
}

/// <summary>One spell model to draw this frame (display only; published each tick).</summary>
public readonly record struct SpellShape(ShapeKind Kind, Vector3 At, Vector3 Heading, float Age, int Count = 1);

/// <summary>
/// The spells under way (README "Facts"): Earth Spikes from the hand contact to their fall, whirlwinds moving and striking, bombs thrown, flying
/// and bursting. Their blows are queued and go through the bolt's hit in the next combat schedule (<see cref="ApplyHits"/>).
/// </summary>
sealed partial class ArcaneSystem
{
    /// <summary>The first bolt number the spells' blows take (far above the game's own count), so the hit filter knows them.</summary>
    const int FirstSpellBolt = 0x40000000;

    sealed record CastInfo(CharacterId Caster, int Faction, int Squad, bool Player, float Skill);

    sealed class Spikes(CastInfo cast, CharacterId target, float min, float max, int count)
    {
        public readonly CastInfo Cast = cast;
        public readonly CharacterId Target = target;
        public readonly float Min = min, Max = max;
        public readonly int Count = count;
        public float Age;
        public Vector3 At;
        public bool Risen, Struck;
    }

    sealed class Whirlwind(CastInfo cast, Vector3 heading, float min, float max, float life)
    {
        public readonly CastInfo Cast = cast;
        public readonly Vector3 Heading = heading;
        public readonly float Min = min, Max = max, Life = life;
        public Vector3 At;
        public float Age = -ArcaneRules.WhirlwindSlap, Struck;
    }

    sealed class Bomb(CastInfo cast, Vector3 aim, float damage)
    {
        public readonly CastInfo Cast = cast;
        public readonly Vector3 Aim = aim;
        public readonly float Damage = damage;
        public Vector3 At;
        public float Age, Flown, Burst = -1;
    }

    readonly List<Spikes> _spikes = [];
    readonly List<Whirlwind> _whirlwinds = [];
    readonly List<Bomb> _bombs = [];

    /// <summary>Blows queued for the next combat schedule, and the packets the hit filter gives them by bolt number while they land.</summary>
    readonly List<(CharacterId Victim, SpellHit Hit)> _hits = [];
    readonly Dictionary<int, DamagePacket> _packets = [];
    int _nextBolt = FirstSpellBolt;

    /// <summary>The spell models to draw, published each tick.</summary>
    public volatile SpellShape[] Shapes = [];

    static CastInfo Info(CharacterId id, CharacterCold cold, float level) => new(id, cold.Faction, cold.SquadId, cold.IsPlayer, level);

    void StartSpikes(CharacterId id, CharacterCold cold, CharacterId target, float min, float max, float level) =>
        _spikes.Add(new Spikes(Info(id, cold, level), target, min, max, ArcaneRules.Volley(level)));

    void StartWhirlwind(CharacterId id, CharacterCold cold, Vector3 start, Vector3 to, float min, float max, float level)
    {
        var heading = Vector3.Normalize(Flat(to - start));
        _whirlwinds.Add(new Whirlwind(Info(id, cold, level), heading, min * ArcaneRules.WhirlwindShare, max * ArcaneRules.WhirlwindShare,
            ArcaneRules.WhirlwindLife(level)) { At = start });
    }

    void StartBomb(CharacterId id, CharacterCold cold, Vector3 aim, float damage, float level) =>
        _bombs.Add(new Bomb(Info(id, cold, level), aim, damage));

    /// <summary>One serial step of every spell under way, in the order they were cast.</summary>
    void StepSpells(World world, float dt)
    {
        StepSpikes(world, dt);
        StepWhirlwinds(world, dt);
        StepBombs(world, dt);
        ExpireSummons(world, dt);
        Shapes = PublishShapes();
    }

    /// <summary>
    /// Earth Spikes rise under the target at the hand contact, strike it 0.18 s later (one blow per spike) and stand until 2.18 s after rising.
    /// A target gone before the contact gets none.
    /// </summary>
    void StepSpikes(World world, float dt)
    {
        for (int k = 0; k < _spikes.Count; k++)
        {
            var s = _spikes[k];
            s.Age += dt;
            if (!s.Risen && s.Age >= ArcaneRules.SpikeContact) Rise(world, s);
            if (s.Risen && !s.Struck && s.Age >= ArcaneRules.SpikeContact + ArcaneRules.SpikeImpact) StrikeSpikes(world, s);
            if (s.Age >= ArcaneRules.SpikeContact + ArcaneRules.SpikeLife || (s.Age >= ArcaneRules.SpikeContact && !s.Risen)) _spikes.RemoveAt(k--);
        }
    }

    void Rise(World world, Spikes s)
    {
        if (!world.Characters.TryResolveNext(s.Target, out int slot)) return;

        var feet = world.Characters.Previous[slot].Position;
        s.At = feet with { Y = world.Walkability.GroundHeight(feet.X, feet.Z, feet.Y) };
        s.Risen = true;
    }

    void StrikeSpikes(World world, Spikes s)
    {
        s.Struck = true;
        for (int n = 0; n < s.Count; n++)
            Queue(s.Cast, s.Target, s.At - Vector3.UnitY, Vector3.UnitY, Pierce(Uniform(world, s.Cast.Caster, s.Min, s.Max)), true);

        Visuals.Enqueue(new Visual(ArcaneRules.Flash, s.At, CharacterId.None, 1.5f));
    }

    /// <summary>
    /// A whirlwind leaves at the cast's slap from the caster's place, moves 135 units a second along its heading on the ground, ends at ground that
    /// cannot be walked or after its life, and every 0.2 s strikes each living character within 30 across and 20 up, but its caster, the caster's
    /// squad and its allies.
    /// </summary>
    void StepWhirlwinds(World world, float dt)
    {
        for (int k = 0; k < _whirlwinds.Count; k++)
        {
            var w = _whirlwinds[k];
            bool started = w.Age >= 0;
            w.Age += dt;
            if (w.Age < 0) continue;

            if (!started) w.At = w.At with { Y = world.Walkability.GroundHeight(w.At.X, w.At.Z, w.At.Y) };

            var next = w.At + w.Heading * (ArcaneRules.WhirlwindSpeed * dt);
            if (w.Age > w.Life || !world.Walkability.IsWalkable(next.X, next.Z, w.At.Y))
            {
                _whirlwinds.RemoveAt(k--);
                continue;
            }

            w.At = next with { Y = world.Walkability.GroundHeight(next.X, next.Z, w.At.Y) };
            while (w.Struck + ArcaneRules.WhirlwindStep <= w.Age + 1e-6f)
            {
                w.Struck += ArcaneRules.WhirlwindStep;
                Strike(world, w);
            }
        }
    }

    void Strike(World world, Whirlwind w)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            var hot = table.Previous[slot];
            if (!hot.Alive || table.Cold(slot) is not { Medical.Dead: false } cold || table.IdOf(slot) == w.Cast.Caster) continue;
            if (Flat(hot.Position - w.At).LengthSquared() > ArcaneRules.WhirlwindRadius * ArcaneRules.WhirlwindRadius) continue;
            if (MathF.Abs(hot.Position.Y - w.At.Y) > ArcaneRules.WhirlwindHeight || Friend(w.Cast, cold)) continue;

            Queue(w.Cast, table.IdOf(slot), w.At, w.Heading, Pierce(Uniform(world, w.Cast.Caster, w.Min, w.Max)), true);
        }
    }

    bool Friend(CastInfo cast, CharacterCold other)
    {
        if (cast.Squad >= 0 && other.SquadId == cast.Squad) return true;

        var relations = _context.Population.Relations;
        return relations.IsAlly(cast.Faction, other.Faction) || relations.IsAlly(other.Faction, cast.Faction);
    }

    /// <summary>
    /// A bomb is thrown from the caster's left hand at the cast's throw, flies 180 units a second towards the aim point in 0.02 s steps and
    /// bursts on arrival, at ground above it, or after 12 s; the burst is then shown 0.8 s.
    /// </summary>
    void StepBombs(World world, float dt)
    {
        for (int k = 0; k < _bombs.Count; k++)
        {
            var b = _bombs[k];
            b.Age += dt;
            if (b.Burst >= 0)
            {
                b.Burst += dt;
                if (b.Burst >= ArcaneRules.BurstShow) _bombs.RemoveAt(k--);
                continue;
            }

            if (b.Age < ArcaneRules.BombLaunch) continue;

            if (b.Flown == 0 && !Thrown(world, b))
            {
                _bombs.RemoveAt(k--);
                continue;
            }

            Fly(world, b, MathF.Min(dt, b.Age - ArcaneRules.BombLaunch));
        }
    }

    /// <summary>The throw: from the caster's hand height; false (no bomb) when the caster is gone.</summary>
    bool Thrown(World world, Bomb b)
    {
        if (!world.Characters.TryResolveNext(b.Cast.Caster, out int slot)) return false;

        b.At = world.Characters.Previous[slot].Position + Vector3.UnitY * RangedTuning.BarrelHeight;
        b.Flown = 1e-6f;
        return true;
    }

    void Fly(World world, Bomb b, float dt)
    {
        for (float left = dt; left > 0 && b.Burst < 0; left -= ArcaneRules.BombStep)
        {
            float step = MathF.Min(left, ArcaneRules.BombStep);
            b.Flown += step;
            var to = b.Aim - b.At;
            float distance = to.Length();
            float move = step * ArcaneRules.BombSpeed;
            if (distance <= move)
            {
                b.At = b.Aim;
                Explode(world, b);
                return;
            }

            b.At += to * (move / distance);
            if (world.Walkability.GroundHeight(b.At.X, b.At.Z, b.At.Y) > b.At.Y || b.Flown >= ArcaneRules.BombMaxFlight) Explode(world, b);
        }
    }

    /// <summary>A bomb bursts: every living character within 80 of it, the caster and its friends too, takes its cut damage with the falloff.</summary>
    void Explode(World world, Bomb b)
    {
        b.Burst = 0;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            var hot = table.Previous[slot];
            if (!hot.Alive || table.Cold(slot) is not { Medical.Dead: false }) continue;

            float distance = Vector3.Distance(hot.Position, b.At);
            if (distance > ArcaneRules.BombRadius) continue;

            var away = Flat(hot.Position - b.At);
            var direction = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitZ;
            var packet = new DamagePacket(b.Damage * ArcaneRules.BombFalloff(distance), 0, 0, 0, 1, 0);
            Queue(b.Cast, table.IdOf(slot), b.At, direction, packet, table.IdOf(slot) != b.Cast.Caster);
        }

        Visuals.Enqueue(new Visual(ArcaneRules.Flash, b.At + Vector3.UnitY * 10, CharacterId.None, 6));
    }

    SpellShape[] PublishShapes()
    {
        var shapes = new List<SpellShape>();
        foreach (var s in _spikes)
            if (s.Risen) shapes.Add(new SpellShape(ShapeKind.Spike, s.At, Vector3.UnitZ, s.Age - ArcaneRules.SpikeContact, s.Count));

        foreach (var w in _whirlwinds)
            if (w.Age >= 0) shapes.Add(new SpellShape(ShapeKind.Whirlwind, w.At, w.Heading, w.Age));

        foreach (var b in _bombs)
        {
            if (b.Burst >= 0) shapes.Add(new SpellShape(ShapeKind.Burst, b.At, Vector3.UnitZ, b.Burst));
            else if (b.Flown > 0) shapes.Add(new SpellShape(ShapeKind.BombFlying, b.At, b.Aim - b.At, b.Flown));
        }

        return [.. shapes];
    }

    static DamagePacket Pierce(float amount) => new(0, 0, amount, 0, 1, 0);

    void Queue(CastInfo cast, CharacterId victim, Vector3 from, Vector3 direction, DamagePacket packet, bool intended) =>
        _hits.Add((victim, new SpellHit(_nextBolt++, cast.Caster, cast.Faction, cast.Player, cast.Skill, from, direction, packet, intended)));

    /// <summary>
    /// The queued blows, in the combat system's serial schedule (Harmony postfix on <c>CombatSystem.Schedule</c>), each as a bolt hit whose packet
    /// the port's filter sets; a victim gone or dead since is passed by.
    /// </summary>
    public void ApplyHits(World world, CombatSystem combat)
    {
        if (_hits.Count == 0) return;

        var hits = _hits.ToArray();
        _hits.Clear();
        foreach (var (victim, hit) in hits)
        {
            if (!world.Characters.TryResolveNext(victim, out int slot) || world.Characters.Cold(slot) is not { Medical.Dead: false }) continue;

            _packets[hit.Id] = hit.Packet;
            try
            {
                BoltAccess.Hit(combat, world, slot, hit);
            }
            finally
            {
                _packets.Remove(hit.Id);
            }
        }
    }

    /// <summary>The port's hit filter: a spell blow gets its own packet (pierce, or the bomb's cut; bleed 1) in place of a bolt's.</summary>
    public void HitLanding(World world, ref PendingHit hit)
    {
        if (hit.Source == CombatHitSource.Projectile && _packets.TryGetValue(hit.Sequence, out var packet)) hit.Packet = packet;
    }

    void HashSpells(ref StateHasher h)
    {
        h.Add(_nextBolt);
        foreach (var s in _spikes)
        {
            h.Add(s.Target);
            h.Add(s.Age);
        }

        foreach (var w in _whirlwinds)
        {
            h.Add(w.At);
            h.Add(w.Age);
        }

        foreach (var b in _bombs)
        {
            h.Add(b.At);
            h.Add(b.Age);
            h.Add(b.Burst);
        }

        HashSummons(ref h);
        h.Add(_hits.Count);
    }
}
