using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.PoshDamageNumbers;

/// <summary>What the numbers show: a hit's damage (with its share of the part's health and whether the victim is yours) or a word.</summary>
abstract record Shown(CharacterId Victim);

sealed record DamageShown(CharacterId Victim, float Damage, float Ratio, bool Squad) : Shown(Victim);

sealed record WordShown(CharacterId Victim, Word Word) : Shown(Victim);

/// <summary>The original's word sprites and their colours (<b>Verified</b>, <c>damage_numbers.lua</c> COLOURS).</summary>
enum Word { Knockout, Severed, Killed }

/// <summary>
/// Reads each resolved hit on the tick thread (an observer: display only, it changes no world state) and queues what to show for the host:
/// the damage after armour (cut + blunt + pierce, times the hit's scale), its share of the hit part's maximum health (0.2 when unknown, as the
/// original), and KNOCKOUT / SEVERED / KILLED when the hit brought them. A victim already dead never gets a number. Only hits that concern you
/// (<b>Verified</b>, original): on your people, by them, or within Fight range of one of them (10 world units a metre).
/// </summary>
sealed class HitWatch(DamageSettings settings) : ICombatHitObserver
{
    const float UnitsPerMetre = 10, UnknownRatio = 0.2f;

    /// <summary>Waiting for the host, oldest first.</summary>
    public static readonly ConcurrentQueue<Shown> Queue = new();

    readonly List<Vector2> _squad = [];
    readonly HashSet<(CharacterId, int)> _severed = [];
    long _squadTick = -1;

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!settings.Enabled || !hit.VictimWasAlive) return;
        if (!world.Characters.TryResolveNext(hit.Victim, out int slot) || world.Characters.Cold(slot) is not { Medical: { } medical } victim) return;
        if (victim.IsPlayer && !settings.ShowOwnSquadDamage) return;
        if (!Concerns(world, hit, victim, world.Characters.Next[slot].Position)) return;

        float damage = (hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce) * hit.Damage.Scale;
        if (MathF.Floor(damage + 0.5f) >= settings.MinDamage)
            Queue.Enqueue(new DamageShown(hit.Victim, damage, Ratio(medical, hit.Part, damage), victim.IsPlayer));
        if (settings.ShowWords) Words(hit, victim, medical);
        while (Queue.Count > 512) Queue.TryDequeue(out _);
    }

    static float Ratio(MedicalState medical, int part, float damage) =>
        part >= 0 && part < medical.Parts.Count && medical.Parts[part].MaxHp > 0 ? damage / medical.Parts[part].MaxHp : UnknownRatio;

    /// <summary>KNOCKOUT when the hit knocked the victim out, KILLED when it killed it, SEVERED once per part a person loses (not animals).</summary>
    void Words(in CombatHit hit, CharacterCold victim, MedicalState medical)
    {
        if (!hit.VictimWasIncapacitated && medical.Unconscious && !medical.Dead) Queue.Enqueue(new WordShown(hit.Victim, Word.Knockout));
        if (victim.Animal is null && hit.Part >= 0 && hit.Part < medical.Parts.Count && medical.Parts[hit.Part].Limb == LimbState.Stump
            && _severed.Add((hit.Victim, hit.Part)))
            Queue.Enqueue(new WordShown(hit.Victim, Word.Severed));
        if (medical.Dead) Queue.Enqueue(new WordShown(hit.Victim, Word.Killed));
    }

    bool Concerns(World world, in CombatHit hit, CharacterCold victim, Vector3 at)
    {
        if (victim.IsPlayer) return true;
        if (world.Characters.TryResolveNext(hit.Attacker, out int a) && world.Characters.Cold(a) is { IsPlayer: true }) return true;

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
