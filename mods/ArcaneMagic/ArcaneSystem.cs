using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;

namespace Meitou.ArcaneMagic;

/// <summary>A character's Arcane state: XP towards the next level, mana, the spell selected (the level is the Arcane skill's).</summary>
sealed class Caster
{
    public float Xp, Mana;
    public Spell Spell;
}

/// <summary>What the interface shows of a caster (published each tick; display only).</summary>
sealed record CasterView(float Mana, float Max, float Level, Spell Spell, bool Locked);

/// <summary>Something for the host's frame to draw (a particle burst at a point or on a character) or tell (a notice).</summary>
readonly record struct Visual(string Effect, Vector3 At, CharacterId On, float Size, string? Notice = null);

/// <summary>
/// The port's world state and its tick work (README "Facts"): the casters' mana, XP and spell, the faction's unlocked spells, the spells under
/// way (Earth Spikes, whirlwinds, bombs, summons) and their hits. Its own serial steps only; the Harmony patches call it from the combat system's
/// serial steps (<see cref="FocusPatches"/>), and read it (never write) from the parallel aim step.
/// </summary>
sealed partial class ArcaneSystem : ITickSystem, IStateHashed, IModSaveData, IWorldReady, ICombatHitFilter, IExperienceFilter,
    ICharacterDeathObserver, IPausedCommandHandler
{
    static readonly ConditionalWeakTable<CombatSystem, ArcaneSystem> ByCombat = [];

    readonly ModContext _context;
    readonly ModSkill _skill;
    readonly CombatSystem? _combat;
    readonly PopulationSystem? _population;
    readonly Dictionary<CharacterId, Caster> _casters = [];

    /// <summary>Spells the player's faction has unlocked by a Magus's defeat, and the debug locks/unlocks over them (README "Port choices").</summary>
    readonly HashSet<Spell> _unlocked = [], _manualOn = [], _manualOff = [];

    /// <summary>The running world's system for the interface (null before a world).</summary>
    public static volatile ArcaneSystem? Running;

    /// <summary>The casters the interface may show, published each tick.</summary>
    public volatile FrozenDictionary<CharacterId, CasterView> View = FrozenDictionary<CharacterId, CasterView>.Empty;

    /// <summary>Bursts and notices for the host's frame.</summary>
    public readonly ConcurrentQueue<Visual> Visuals = new();

    ulong _rolls;

    /// <summary>The world this system ticks (set at its first step), for the save's lasting ids.</summary>
    World? _world;

    public ArcaneSystem(ModContext context, ModSkill skill, CombatSystem? combat, PopulationSystem? population)
    {
        _context = context;
        _skill = skill;
        _combat = combat;
        _population = population;
        if (combat is not null) ByCombat.AddOrUpdate(combat, this);
        Running = this;
    }

    /// <summary>The port's system for a combat system (the Harmony patches' way in).</summary>
    public static ArcaneSystem? Of(CombatSystem combat) => ByCombat.TryGetValue(combat, out var system) ? system : null;

    public static bool HoldsFocus(CharacterCold? cold) => cold?.Fighter?.Crossbow?.Data.StringId == ArcaneRules.Focus;

    public float Level(CharacterCold cold) => cold.Stats is { } stats ? ArcaneRules.Clamp(_skill.Level(stats)) : 0;

    /// <summary>A caster's state, read only (null before its first serial step with the Focus).</summary>
    public Caster? Find(CharacterId id) => _casters.GetValueOrDefault(id);

    /// <summary>A caster's state, made full of mana on first sight, a named Magus raised to its Arcane level (serial steps).</summary>
    Caster Ensure(World world, int slot, CharacterCold cold)
    {
        var id = world.Characters.IdOf(slot);
        if (_casters.TryGetValue(id, out var caster)) return caster;

        if (ArcaneRules.Magi.TryGetValue(cold.RecordId, out var magus) && cold.Stats is { } stats && _skill.Level(stats) < magus.Level)
            _skill.Set(stats, magus.Level);

        caster = new Caster { Mana = ArcaneRules.ManaMax(Level(cold)) };
        FromSaveKeys(world, id, cold, caster);
        if (cold.IsPlayer) world.LastingIds.GetOrGive(id);
        _casters[id] = caster;
        return caster;
    }

    public void Ready(World world, bool fromSave)
    {
        Running = this;
        _world = world;
        RestoreSaved(world);
        if (fromSave) DropStraySummons(world);
    }

    public void SlowWorld(World world)
    {
        _world = world;
        var table = world.Characters;
        float dt = world.TickSeconds;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Previous[slot].Alive || table.Cold(slot) is not { } cold || !HoldsFocus(cold)) continue;

            Regenerate(Ensure(world, slot, cold), cold, dt);
        }

        StepSpells(world, dt);
        if (world.Tick % 30 == 0) WriteSaveKeys(world);
        WatchMagi(world);
        DropGone(world);
        View = Publish(world);
    }

    void Regenerate(Caster caster, CharacterCold cold, float dt)
    {
        float level = Level(cold);
        bool levigator = cold.Inventory?.Backpack?.Record == ArcaneRules.Levigator;
        caster.Mana = Math.Clamp(caster.Mana + ArcaneRules.Regen(level, levigator) * dt, 0, ArcaneRules.ManaMax(level));
    }

    /// <summary>Gives Arcane XP and raises the level as the original does (each level needs 20 plus 2 a level); 100 is the cap.</summary>
    void GiveXp(CharacterCold cold, Caster caster, float amount)
    {
        if (cold.Stats is not { } stats) return;

        CastXp(cold);
        float level = Level(cold);
        if (level >= ArcaneRules.MaxLevel) return;

        caster.Xp += amount;
        while (level < ArcaneRules.MaxLevel && caster.Xp >= ArcaneRules.XpFor(level))
        {
            caster.Xp -= ArcaneRules.XpFor(level);
            level++;
        }

        _skill.Set(stats, MathF.Min(level, ArcaneRules.MaxLevel));
    }

    /// <summary>
    /// Every release also trains the character's stats as the original's cast does (event XP): Science 2.5, Friendly fire 1, Perception 1,
    /// Dexterity 0.5.
    /// </summary>
    static void CastXp(CharacterCold cold)
    {
        if (cold.Stats is not { } stats) return;

        stats.Gain(StatsEnumerated.Science, 2.5f);
        stats.Gain(StatsEnumerated.FriendlyFire, 1);
        stats.Gain(StatsEnumerated.Perception, 1);
        stats.Gain(StatsEnumerated.Dexterity, 0.5f);
    }

    /// <summary>The original's per-character STATS keys (floats, the spell by its number).</summary>
    const string LevelKey = "AM_arcane_level", XpKey = "AM_arcane_xp", ManaKey = "AM_mana_current", SpellKey = "AM_selected_spell";

    /// <summary>
    /// A character first seen with a save's keys (a save made by the original, or without the port's save data): its XP, mana, spell and a level
    /// above the skill's.
    /// </summary>
    void FromSaveKeys(World world, CharacterId id, CharacterCold cold, Caster caster)
    {
        var fields = world.SaveFields;
        if (fields.Float(id, CharacterSaveRecord.Stats, ManaKey) is { } mana) caster.Mana = mana;
        if (fields.Float(id, CharacterSaveRecord.Stats, XpKey) is { } xp) caster.Xp = xp;
        if (fields.Float(id, CharacterSaveRecord.Stats, SpellKey) is { } spell) caster.Spell = (Spell)Math.Clamp((int)spell, 0, 4);
        if (fields.Float(id, CharacterSaveRecord.Stats, LevelKey) is { } level && cold.Stats is { } stats && level > _skill.Level(stats))
            _skill.Set(stats, ArcaneRules.Clamp(level));
    }

    /// <summary>Once a second, every caster's state under the original's keys, so a save taken to the original keeps it.</summary>
    void WriteSaveKeys(World world)
    {
        var fields = world.SaveFields;
        foreach (var (id, caster) in _casters.OrderBy(p => p.Key.Slot))
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { } cold) continue;

            fields.SetFloat(id, CharacterSaveRecord.Stats, LevelKey, Level(cold));
            fields.SetFloat(id, CharacterSaveRecord.Stats, XpKey, caster.Xp);
            fields.SetFloat(id, CharacterSaveRecord.Stats, ManaKey, caster.Mana);
            fields.SetFloat(id, CharacterSaveRecord.Stats, SpellKey, (int)caster.Spell);
        }
    }

    /// <summary>Forgets the state of characters no longer in the world (a player's comes back from the save data by its lasting id).</summary>
    void DropGone(World world)
    {
        if (world.Tick % 300 != 0) return;

        foreach (var id in _casters.Keys.Where(id => !world.Characters.IsAlive(id)).ToList())
        {
            _casters.Remove(id);
        }

        foreach (var cold in _lastCast.Keys.Where(c => c.Medical is null || c.Medical.Dead).ToList()) _lastCast.Remove(cold);
    }

    FrozenDictionary<CharacterId, CasterView> Publish(World world)
    {
        var shown = new Dictionary<CharacterId, CasterView>();
        foreach (var (id, caster) in _casters)
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { IsPlayer: true } cold) continue;

            float level = Level(cold);
            shown[id] = new CasterView(caster.Mana, ArcaneRules.ManaMax(level), level, caster.Spell, !Unlocked(caster.Spell));
        }

        return shown.ToFrozenDictionary();
    }

    /// <summary>Whether the player's faction may cast a spell: the Missile always, the rest once unlocked (the debug switch over both).</summary>
    public bool Unlocked(Spell spell) =>
        spell == Spell.Missile ? !_manualOff.Contains(spell) : (_unlocked.Contains(spell) || _manualOn.Contains(spell)) && !_manualOff.Contains(spell);

    public void Hash(ref StateHasher h)
    {
        foreach (var (id, caster) in _casters.OrderBy(p => p.Key.Slot).ThenBy(p => p.Key.Generation))
        {
            h.Add(id.Slot);
            h.Add(id.Generation);
            h.Add(caster.Xp);
            h.Add(caster.Mana);
            h.Add((int)caster.Spell);
        }

        foreach (var spell in _unlocked.Order()) h.Add((int)spell + 100);
        foreach (var spell in _manualOn.Order()) h.Add((int)spell + 200);
        foreach (var spell in _manualOff.Order()) h.Add((int)spell + 300);
        HashSpells(ref h);
        h.Add((long)_rolls);
    }

    void Notice(string text) => Visuals.Enqueue(new Visual("", default, CharacterId.None, 0, _context.Tr(text)));

    float Uniform(World world, CharacterId caster, float min, float max) =>
        min + Rng.Float(Rng.Hash(world.Seed, Rng.Key(caster), _context.RngPurpose, _rolls++)) * (max - min);
}
