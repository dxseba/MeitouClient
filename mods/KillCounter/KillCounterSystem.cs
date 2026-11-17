using System.Collections.Frozen;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.KillCounter;

public readonly record struct Tally(int Kills, int Knockouts, int Limbs = 0);

/// <summary>
/// Credits the attacker of a hit that leaves an alive victim dead (a kill) or a conscious one down (a knockout); a downed victim who later dies is a kill for its last attacker: the victim's state is read in the
/// slow phase within a second of the hit, as the medical update that downs it can run after the hit is resolved. Presentation only: never changes
/// the world, so it is not hashed. Tallies are keyed by character name, as saved ids are not stable across loads; two namesakes share one tally.
/// </summary>
public sealed class KillCounterSystem : ICombatHitObserver, IModSaveData
{
    readonly SortedDictionary<string, Tally> _tallies = new(StringComparer.Ordinal);
    volatile FrozenDictionary<CharacterId, Tally> _published = FrozenDictionary<CharacterId, Tally>.Empty;
    bool _changed = true;
    readonly Dictionary<CharacterId, (string Attacker, long Tick)> _pending = [];
    readonly Dictionary<CharacterId, string> _downed = [];
    readonly Dictionary<CharacterId, int> _stumps = [];
    const long Window = 30;

    /// <summary>The player characters' tallies by id, rebuilt in the tick about once a second; safe to read from the interface thread.</summary>
    public FrozenDictionary<CharacterId, Tally> Published => _published;

    public void SlowWorld(World world)
    {
        Settle(world);
        SettleDowned(world);
        CountStumps(world);
        Publish(world);
    }

    void Settle(World world)
    {
        foreach (var (victim, (attacker, tick)) in _pending.ToArray())
        {
            var medical = world.Characters.TryResolveNext(victim, out int slot) ? world.Characters.Cold(slot)?.Medical : null;
            bool expired = medical is null || world.Tick - tick > Window;

            if (medical is { Dead: true }) Add(attacker, new(1, 0));
            else if (medical is { Unconscious: true }) KnockedOut(victim, attacker);
            else if (!expired) continue;

            _pending.Remove(victim);
        }
    }

    void KnockedOut(CharacterId victim, string attacker)
    {
        Add(attacker, new(0, 1));
        _downed[victim] = attacker;
    }

    /// <summary>A downed victim who later dies (more blows or bleeding out) is a kill for whoever last struck it; one who gets up is dropped.</summary>
    void SettleDowned(World world)
    {
        foreach (var (victim, attacker) in _downed.ToArray())
        {
            var medical = world.Characters.TryResolveNext(victim, out int slot) ? world.Characters.Cold(slot)?.Medical : null;
            if (medical is { Dead: false, Unconscious: true }) continue;

            if (medical is { Dead: true }) Add(attacker, new(1, 0));
            _downed.Remove(victim);
        }
    }

    void Publish(World world)
    {
        if (!_changed && world.Tick % 30 != 0) return;
        _changed = false;

        var table = world.Characters;
        var map = new Dictionary<CharacterId, Tally>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { IsPlayer: true } cold)
                map[new(slot, table.Next[slot].Generation)] = _tallies.GetValueOrDefault(cold.Name);
        }
        _published = map.ToFrozenDictionary();
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!hit.VictimWasAlive) return;
        if (!world.Characters.TryResolveNext(hit.Attacker, out int attacker) || world.Characters.Cold(attacker) is not { Name.Length: > 0 } killer) return;

        CreditLimbs(world, hit.Victim, killer.Name);

        if (hit.VictimWasIncapacitated)
        {
            _downed[hit.Victim] = killer.Name;
            return;
        }

        _pending[hit.Victim] = (killer.Name, world.Tick);
    }

    /// <summary>
    /// Limbs removed: the victim's stumps now against the count last seen (refreshed for everyone in the slow phase, so a prosthetic or a loaded
    /// game is not credited); the difference goes to the attacker of this hit.
    /// </summary>
    void CreditLimbs(World world, CharacterId victim, string attacker)
    {
        if (!world.Characters.TryResolveNext(victim, out int slot) || !_stumps.TryGetValue(victim, out int before)) return;

        int now = Stumps(world, slot);
        if (now > before) Add(attacker, new(0, 0, now - before));
        _stumps[victim] = now;
    }

    void CountStumps(World world)
    {
        var table = world.Characters;
        _stumps.Clear();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive) _stumps[table.IdOf(slot)] = Stumps(world, slot);
        }
    }

    static int Stumps(World world, int slot) =>
        world.Characters.Cold(slot)?.Medical?.Parts.Count(p => p.Limb == Meitou.Simulation.Bodies.LimbState.Stump) ?? 0;

    void Add(string name, Tally delta)
    {
        var old = _tallies.GetValueOrDefault(name);
        _tallies[name] = new(old.Kills + delta.Kills, old.Knockouts + delta.Knockouts, old.Limbs + delta.Limbs);
        _changed = true;
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(2);
        writer.Write(_tallies.Count);
        foreach (var (name, tally) in _tallies)
        {
            writer.Write(name);
            writer.Write(tally.Kills);
            writer.Write(tally.Knockouts);
            writer.Write(tally.Limbs);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version is not (1 or 2)) throw new InvalidDataException("Unknown Kill Counter save version");

        _tallies.Clear();
        for (int count = reader.ReadInt32(), i = 0; i < count; i++)
            _tallies[reader.ReadString()] = new(reader.ReadInt32(), reader.ReadInt32(), version == 2 ? reader.ReadInt32() : 0);
        _changed = true;
    }
}
