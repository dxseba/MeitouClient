using System.Collections.Frozen;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Saving;

namespace Meitou.KillCounter;

public readonly record struct Tally(int Kills, int Knockouts, int Limbs = 0);

/// <summary>
/// Counts every character's kills, knockouts and limbs removed, as the original does for any attacker. A victim struck by someone is followed
/// while it stays in the world: its last attacker gets the kill when it dies, however late (bleeding out too); the attacker of a hit that takes it
/// from standing to unconscious gets a knockout (read in the slow phase within <see cref="Window"/> ticks, as the medical step that downs it can
/// run after the hit);
/// a stealth knock-out credits the sneaker. Dying while still down to the character that downed it turns that knockout into the kill.
/// Tallies are keyed by the characters' lasting ids. Each changed tally is also written into the character's vanilla STATS record as the
/// original's floats <c>kills</c>, <c>kos</c> and <c>limbs</c> (<see cref="World.SaveFields"/>, set in the serial slow phase, so they enter the
/// state hash); a character without a tally in the mod's own data takes them from the loaded save (one made by the original) at World ready.
/// Besides that and the lasting ids it never changes the world.
/// </summary>
public sealed class KillCounterSystem : ICombatHitObserver, ICharacterDeathObserver, IWorldReady, IModSaveData
{
    /// <summary>The original's STATS record keys of the three counts.</summary>
    const string KillsKey = "kills", KnockoutsKey = "kos", LimbsKey = "limbs";

    /// <summary>The ticks after a hit within which its knockout is looked for.</summary>
    const long Window = 30;

    /// <summary>How often tallies of characters gone for good are dropped (ten minutes of game time).</summary>
    const long PruneTicks = 30 * 60 * 10;

    /// <summary>A followed victim: its last attacker (the kill), who downed it (the knockout), the knockout check of the latest hit, and whether it
    /// was down when last seen.</summary>
    sealed class Victim
    {
        public LastingId LastAttacker;
        public LastingId KoCreditor;
        public LastingId CheckAttacker;
        public long CheckTick = -1;
        public bool Down;
    }

    readonly SortedDictionary<ulong, Tally> _tallies = [];
    readonly Dictionary<string, Tally> _legacy = new(StringComparer.Ordinal);
    readonly SortedDictionary<(int Slot, int Generation), Victim> _victims = [];
    readonly Dictionary<CharacterId, int> _stumps = [];
    HashSet<CharacterId> _sneakDown = [], _sneakDownNow = [];
    readonly HashSet<CharacterId> _killed = [];
    readonly SortedSet<ulong> _unsaved = [];
    volatile FrozenDictionary<ulong, Tally> _published = FrozenDictionary<ulong, Tally>.Empty;
    bool _changed = true;

    public KillCounterSystem() => Current = this;

    /// <summary>The system of the world built or ticked last, for the interface (display only).</summary>
    public static volatile KillCounterSystem? Current;

    /// <summary>The tallies by lasting id, rebuilt in the tick when they change; safe to read from the interface thread.</summary>
    public FrozenDictionary<ulong, Tally> Published => _published;

    public void Ready(World world, bool fromSave)
    {
        Current = this;
        AdoptLegacy(world);
        AdoptSaved(world);
        Prune(world);
        StoreFields(world);
        Publish();
    }

    public void SlowWorld(World world)
    {
        Current = this;
        _killed.Clear();
        CreditSneakKnockouts(world);
        SettleKnockouts(world);
        CountStumps(world);
        if (world.Tick % PruneTicks == 0) Prune(world);

        StoreFields(world);
        Publish();
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!hit.VictimWasAlive || hit.Attacker == hit.Victim) return;

        var attacker = world.LastingIds.GetOrGive(hit.Attacker);
        if (attacker.IsNone) return;

        CreditLimbs(world, hit.Victim, attacker);
        if (!_killed.Contains(hit.Victim)) Struck(Follow(hit.Victim), attacker, world.Tick, hit.VictimWasIncapacitated);
    }

    /// <summary>A new hit: it is the last attacker; a knockout of the hit before, which this hit already found down, is credited now.</summary>
    void Struck(Victim victim, LastingId attacker, long tick, bool wasDown)
    {
        if (victim.CheckTick >= 0 && wasDown) KnockedOut(victim, victim.CheckAttacker);

        victim.LastAttacker = attacker;
        victim.CheckAttacker = attacker;
        victim.CheckTick = wasDown ? -1 : tick;
        victim.Down = wasDown;
    }

    /// <summary>
    /// The kill goes to the killing blow's attacker (the last hitter, though its hit may be told after the death), else to the last attacker seen;
    /// the victim's later hit report of the same step is then not followed.
    /// </summary>
    public void Died(World world, in CharacterDeath death)
    {
        _killed.Add(death.Victim);
        _victims.Remove(Key(death.Victim), out var victim);

        bool blow = !death.Killer.IsNone && death.Killer != death.Victim;
        var killer = blow ? IdOf(world, death.Killer) : victim?.LastAttacker ?? LastingId.None;
        if (killer.IsNone) return;

        Add(killer, new(1, 0));

        bool down = victim is not null && (victim.Down || world.Characters.Cold(death.Victim.Slot)?.Medical is { Unconscious: true });
        if (down && killer == victim!.KoCreditor && _tallies.GetValueOrDefault(killer.Value).Knockouts > 0) Add(killer, new(0, -1));
    }

    /// <summary>
    /// A character's lasting id, given now if it has none; the id it has when none may be given (a death told between a save's capture and the
    /// next tick).
    /// </summary>
    static LastingId IdOf(World world, CharacterId id)
    {
        try
        {
            return world.LastingIds.GetOrGive(id);
        }
        catch (InvalidOperationException)
        {
            return world.Characters.TryResolveNext(id, out int slot) ? world.Characters.Cold(slot)?.LastingId ?? LastingId.None : LastingId.None;
        }
    }

    /// <summary>
    /// The knockout checks: a victim down within the window of its last hit gives that hit's attacker a knockout; gone victims are dropped.
    /// </summary>
    void SettleKnockouts(World world)
    {
        List<(int, int)>? gone = null;
        foreach (var (key, victim) in _victims)
        {
            var id = new CharacterId(key.Slot, key.Generation);
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot)?.Medical is not { } medical)
            {
                (gone ??= []).Add(key);
                continue;
            }

            if (medical.Dead) continue;   // its death is told by Died

            if (victim.CheckTick >= 0 && medical.Unconscious) KnockedOut(victim, victim.CheckAttacker);
            else if (victim.CheckTick >= 0 && world.Tick - victim.CheckTick > Window) victim.CheckTick = -1;

            victim.Down = medical.Unconscious;
        }

        foreach (var key in gone ?? []) _victims.Remove(key);
    }

    void KnockedOut(Victim victim, LastingId attacker)
    {
        Add(attacker, new(0, 1));
        victim.KoCreditor = attacker;
        victim.CheckTick = -1;
    }

    /// <summary>
    /// Stealth knock-outs (the original pairs the knock-out chance with the knockout itself): a sneaker in its knock-out task whose target goes
    /// down during the task gets the knockout and becomes the target's last attacker. Runs after the carry step that rolls the swing.
    /// </summary>
    void CreditSneakKnockouts(World world)
    {
        var table = world.Characters;
        _sneakDownNow.Clear();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            ref readonly var n = ref table.Next[slot];
            if (!n.Alive || n.Task != (byte)Simulation.Movement.CharacterTask.StealthKo || table.Cold(slot) is not { } sneaker) continue;

            var target = sneaker.CarryTarget;
            if (!table.TryResolveNext(target, out int t) || table.Cold(t)?.Medical is not { Unconscious: true, Dead: false }) continue;
            if (!_sneakDownNow.Add(target) || _sneakDown.Contains(target)) continue;

            SneakKnockout(world, table.IdOf(slot), target);
        }
        (_sneakDown, _sneakDownNow) = (_sneakDownNow, _sneakDown);
    }

    void SneakKnockout(World world, CharacterId sneaker, CharacterId target)
    {
        var victim = Follow(target);
        if (victim.Down) return;

        var attacker = world.LastingIds.GetOrGive(sneaker);
        if (attacker.IsNone) return;

        victim.LastAttacker = attacker;
        KnockedOut(victim, attacker);
        victim.Down = true;
    }

    Victim Follow(CharacterId id)
    {
        var key = Key(id);
        if (!_victims.TryGetValue(key, out var victim)) _victims[key] = victim = new();
        return victim;
    }

    static (int, int) Key(CharacterId id) => (id.Slot, id.Generation);

    /// <summary>
    /// Limbs removed: the victim's lost arms and legs now against the count last seen (refreshed for everyone in the slow phase, so a prosthetic or a
    /// loaded game is not credited); the difference goes to the attacker of this hit.
    /// </summary>
    void CreditLimbs(World world, CharacterId victim, LastingId attacker)
    {
        if (!world.Characters.TryResolveNext(victim, out int slot) || !_stumps.TryGetValue(victim, out int before)) return;

        int now = LostLimbs(world, slot);
        if (now > before) Add(attacker, new(0, 0, now - before));
        _stumps[victim] = now;
    }

    void CountStumps(World world)
    {
        var table = world.Characters;
        _stumps.Clear();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive) _stumps[table.IdOf(slot)] = LostLimbs(world, slot);
        }
    }

    /// <summary>Cut off or crushed limbs, as the original's four limb states count them (only a limb part has a limb state).</summary>
    static int LostLimbs(World world, int slot) =>
        world.Characters.Cold(slot)?.Medical?.Parts.Count(p => p.Limb is LimbState.Stump or LimbState.Crushed) ?? 0;

    void Add(LastingId who, Tally delta)
    {
        var old = _tallies.GetValueOrDefault(who.Value);
        _tallies[who.Value] = new(old.Kills + delta.Kills, old.Knockouts + delta.Knockouts, old.Limbs + delta.Limbs);
        _unsaved.Add(who.Value);
        _changed = true;
    }

    void Publish()
    {
        if (!_changed) return;
        _changed = false;
        _published = _tallies.ToFrozenDictionary();
    }

    /// <summary>Drops the tallies of characters the game no longer keeps (killed and removed, despawned).</summary>
    void Prune(World world)
    {
        if (_tallies.Count == 0) return;

        var kept = world.LastingIds.KeptIds();
        foreach (ulong id in _tallies.Keys.ToArray())
        {
            if (!kept.Contains(new LastingId(id))) _tallies.Remove(id);
        }
        _changed = true;
    }

    /// <summary>A save of version 1 or 2 kept tallies by name: they go to the player's characters of that name.</summary>
    void AdoptLegacy(World world)
    {
        if (_legacy.Count == 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold) continue;
            if (!_legacy.TryGetValue(cold.Name, out var tally)) continue;

            var id = world.LastingIds.GetOrGive(table.IdOf(slot));
            if (!id.IsNone) Add(id, tally);
        }
        _legacy.Clear();
    }

    /// <summary>
    /// The counts a loaded save keeps in a character's STATS record (the original writes them), for each character the mod's own data has no
    /// tally for.
    /// </summary>
    void AdoptSaved(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { } cold || !cold.LastingId.IsNone && _tallies.ContainsKey(cold.LastingId.Value)) continue;

            var character = table.IdOf(slot);
            if (!table.TryResolveNext(character, out _) || Saved(world, character) is not { } tally) continue;

            var id = world.LastingIds.GetOrGive(character);
            if (!id.IsNone) Add(id, tally);
        }
    }

    /// <summary>The three counts of <paramref name="character"/>'s STATS record; null when it has none of them.</summary>
    static Tally? Saved(World world, CharacterId character)
    {
        var fields = world.SaveFields;
        float? kills = fields.Float(character, CharacterSaveRecord.Stats, KillsKey);
        float? knockouts = fields.Float(character, CharacterSaveRecord.Stats, KnockoutsKey);
        float? limbs = fields.Float(character, CharacterSaveRecord.Stats, LimbsKey);
        if (kills is null && knockouts is null && limbs is null) return null;

        return new(Count(kills), Count(knockouts), Count(limbs));
    }

    static int Count(float? value) => value is { } v && v > 0 ? (int)MathF.Round(v) : 0;

    /// <summary>Writes the changed tallies into their characters' STATS records (the original's keys); a character gone is skipped.</summary>
    void StoreFields(World world)
    {
        foreach (ulong id in _unsaved)
        {
            var character = world.LastingIds.Find(new LastingId(id));
            if (character.IsNone || !world.Characters.TryResolveNext(character, out _)) continue;

            var tally = _tallies.GetValueOrDefault(id);
            world.SaveFields.SetFloat(character, CharacterSaveRecord.Stats, KillsKey, tally.Kills);
            world.SaveFields.SetFloat(character, CharacterSaveRecord.Stats, KnockoutsKey, tally.Knockouts);
            world.SaveFields.SetFloat(character, CharacterSaveRecord.Stats, LimbsKey, tally.Limbs);
        }

        _unsaved.Clear();
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(3);
        writer.Write(_tallies.Count);
        foreach (var (id, tally) in _tallies)
        {
            writer.Write(id);
            writer.Write(tally.Kills);
            writer.Write(tally.Knockouts);
            writer.Write(tally.Limbs);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version is not (1 or 2 or 3)) throw new InvalidDataException("Unknown Kill Counter save version");

        _tallies.Clear();
        _legacy.Clear();
        _unsaved.Clear();
        for (int count = reader.ReadInt32(), i = 0; i < count; i++)
        {
            if (version == 3) _tallies[reader.ReadUInt64()] = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            else _legacy[reader.ReadString()] = new(reader.ReadInt32(), reader.ReadInt32(), version == 2 ? reader.ReadInt32() : 0);
        }

        _unsaved.UnionWith(_tallies.Keys);
        _changed = true;
    }
}
