using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.PoshDamageNumbers;

/// <summary>What the numbers show: health lost (with its share of the part's health and whether the victim is yours), health regained, or a word.</summary>
abstract record Shown(CharacterId Victim);

sealed record DamageShown(CharacterId Victim, float Damage, float Ratio, bool Squad) : Shown(Victim);

sealed record HealShown(CharacterId Victim, float Healed) : Shown(Victim);

sealed record WordShown(CharacterId Victim, Word Word) : Shown(Victim);

/// <summary>The original's word sprites and their colours (<b>Verified</b>, <c>damage_numbers.lua</c> COLOURS).</summary>
enum Word { Knockout, Severed, Killed }

/// <summary>
/// Reads the body parts' health as the original samples it (<b>Verified</b>, <c>damage_numbers.lua</c> <c>sample</c>), from the engine's health
/// changes (an observer: display only, it changes no world state): per character and tick, each part's flesh lost plus stun gained is its hit, the
/// sum the damage and its ratio the damage over the worst-hit part's maximum health (0.2 when unknown); flesh regained is healing. The blow that
/// kills is shown, after that the body is quiet. Words: KNOCKOUT when the character falls unconscious, SEVERED once per part a person loses (not
/// animals), KILLED at its death. Only characters that concern you: your people, those within Fight range of one of them (10 world units a
/// metre) and those that fought them in the last 10 seconds (a hit between them, the original's "fighting your people" mark).
/// </summary>
sealed class HitWatch(DamageSettings settings) : IHealthChangeObserver, ICombatHitObserver, ICharacterDeathObserver
{
    const float UnitsPerMetre = 10, UnknownRatio = 0.2f, EngagedSeconds = 10;
    const int Pruned = 30 * 60;

    /// <summary>Waiting for the host, oldest first.</summary>
    public static readonly ConcurrentQueue<Shown> Queue = new();

    readonly List<Vector2> _squad = [];
    readonly HashSet<(CharacterId, int)> _severed = [];
    readonly HashSet<CharacterId> _deadShown = [], _unconscious = [];
    readonly Dictionary<CharacterId, long> _engaged = [];
    readonly Dictionary<int, float> _hits = [];
    CharacterId _pending = CharacterId.None;
    float _healed;
    long _squadTick = -1;

    /// <summary>Tells the last character of the previous tick, and now and then forgets characters gone from the world.</summary>
    public void SlowWorld(World world)
    {
        Flush(world);
        if (world.Tick % Pruned != 0) return;

        _deadShown.RemoveWhere(id => !world.Characters.IsAlive(id));
        _unconscious.RemoveWhere(id => !world.Characters.IsAlive(id));
        _severed.RemoveWhere(s => !world.Characters.IsAlive(s.Item1));
        foreach (var id in _engaged.Where(e => e.Value < world.Tick).Select(e => e.Key).ToList()) _engaged.Remove(id);
    }

    public void HealthChanged(World world, in HealthChange change)
    {
        if (change.Character != _pending) Flush(world);

        _pending = change.Character;
        if (change.Part < 0) return;

        float hit = change.Value switch
        {
            HealthValue.Flesh => MathF.Max(0, -change.Delta),
            HealthValue.Stun => MathF.Max(0, change.Delta),
            _ => 0,
        };
        if (hit > 0) _hits[change.Part] = _hits.GetValueOrDefault(change.Part) + hit;
        if (change.Value == HealthValue.Flesh && change.Delta > 0) _healed += change.Delta;
    }

    /// <summary>A hit between your people and another marks the other as fighting them.</summary>
    public void HitResolved(World world, in CombatHit hit)
    {
        long until = world.Tick + (long)(EngagedSeconds / world.TickSeconds);
        if (IsPlayer(world, hit.Victim)) _engaged[hit.Attacker] = until;
        if (IsPlayer(world, hit.Attacker)) _engaged[hit.Victim] = until;
    }

    public void Died(World world, in CharacterDeath death)
    {
        if (!settings.Enabled || !settings.ShowWords || death.Victim.IsNone) return;
        if (!world.Characters.TryResolveNext(death.Victim, out int slot) || world.Characters.Cold(slot) is not { } cold) return;

        if (Concerns(world, death.Victim, cold, world.Characters.Next[slot].Position)) Enqueue(new WordShown(death.Victim, Word.Killed));
    }

    /// <summary>What the pending character's changes of this tick show.</summary>
    void Flush(World world)
    {
        var id = _pending;
        _pending = CharacterId.None;
        var table = world.Characters;
        if (!id.IsNone && settings.Enabled && table.TryResolveNext(id, out int slot) && table.Cold(slot) is { Medical: { } medical } cold
            && Concerns(world, id, cold, table.Next[slot].Position))
        {
            Numbers(id, cold, medical);
            if (settings.ShowWords) Words(id, cold, medical);
        }

        Clear();
    }

    void Clear()
    {
        _hits.Clear();
        _healed = 0;
    }

    void Numbers(CharacterId id, CharacterCold cold, MedicalState medical)
    {
        float damage = _hits.Values.Sum();
        bool quiet = medical.Dead && !_deadShown.Add(id);
        if (damage >= settings.MinDamage * 0.5f && !quiet && (!cold.IsPlayer || settings.ShowOwnSquadDamage))
            Enqueue(new DamageShown(id, damage, Ratio(medical, damage), cold.IsPlayer));

        if (settings.ShowHealing && _healed > 0 && !medical.Dead) Enqueue(new HealShown(id, _healed));
    }

    float Ratio(MedicalState medical, float damage)
    {
        int worst = _hits.MaxBy(h => h.Value).Key;
        return worst < medical.Parts.Count && medical.Parts[worst].MaxHp > 0 ? damage / medical.Parts[worst].MaxHp : UnknownRatio;
    }

    void Words(CharacterId id, CharacterCold cold, MedicalState medical)
    {
        if (medical.Unconscious && !medical.Dead && _unconscious.Add(id)) Enqueue(new WordShown(id, Word.Knockout));
        if (!medical.Unconscious) _unconscious.Remove(id);
        if (cold.Animal is not null) return;

        foreach (int part in _hits.Keys)
        {
            if (part < medical.Parts.Count && medical.Parts[part].Limb == LimbState.Stump && _severed.Add((id, part)))
                Enqueue(new WordShown(id, Word.Severed));
        }
    }

    static void Enqueue(Shown shown)
    {
        Queue.Enqueue(shown);
        while (Queue.Count > 512) Queue.TryDequeue(out _);
    }

    static bool IsPlayer(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) && world.Characters.Cold(slot) is { IsPlayer: true };

    bool Concerns(World world, CharacterId id, CharacterCold cold, Vector3 at)
    {
        if (cold.IsPlayer || _engaged.GetValueOrDefault(id, -1) >= world.Tick) return true;

        float range = settings.FightRange * UnitsPerMetre;
        var here = new Vector2(at.X, at.Z);
        return Squad(world).Any(p => Vector2.DistanceSquared(p, here) <= range * range);
    }

    /// <summary>The player characters' ground positions, read once per tick.</summary>
    List<Vector2> Squad(World world)
    {
        if (_squadTick == world.Tick) return _squad;

        _squadTick = world.Tick;
        _squad.Clear();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { IsPlayer: true })
                _squad.Add(new(table.Next[slot].Position.X, table.Next[slot].Position.Z));
        }

        return _squad;
    }
}
