using Meitou.Simulation;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;

namespace Meitou.ArcaneMagic;

/// <summary>The selector's press: the next spell for each listed caster (the original's button cycles through all five, locked ones too).</summary>
public sealed record CycleSpell(IReadOnlyList<CharacterId> Casters) : SimCommand, IPausedCommand;

/// <summary>The original's Shift + right-click on the selector: a spell locked or unlocked by hand for the player's faction.</summary>
public sealed record ToggleSpellLock(Spell Spell) : SimCommand, IPausedCommand;

/// <summary>
/// Commands, the Magi's defeat (the spell unlocks), the Focus's Crossbows XP and the save data of <see cref="ArcaneSystem"/>.
/// </summary>
sealed partial class ArcaneSystem
{
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        RunSummonOrders(world);
        foreach (var command in commands)
        {
            if (command is CycleSpell cycle) Cycle(world, cycle);
            else if (command is ToggleSpellLock toggle) Toggle(toggle.Spell);
        }
    }

    void Cycle(World world, CycleSpell cycle)
    {
        foreach (var id in cycle.Casters)
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { IsPlayer: true } cold) continue;

            var caster = Ensure(world, slot, cold);
            caster.Spell = (Spell)(((int)caster.Spell + 1) % 5);
        }
    }

    void Toggle(Spell spell)
    {
        bool on = !Unlocked(spell);
        _manualOn.Remove(spell);
        _manualOff.Remove(spell);
        if (on && spell != Spell.Missile && !_unlocked.Contains(spell)) _manualOn.Add(spell);
        if (!on) _manualOff.Add(spell);
        Notice($"{ArcaneRules.Caption(spell)} {(on ? "UNLOCKED" : "LOCKED")}");
    }

    /// <summary>A named Magus's death unlocks its spell for the player's faction (the original also counts its imprisonment).</summary>
    public void Died(World world, in CharacterDeath death) => Defeated(death.Record);

    /// <summary>Once a second: a named Magus held imprisoned unlocks its spell too.</summary>
    void WatchMagi(World world)
    {
        if (world.Tick % 30 != 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Previous[slot].Alive && table.Cold(slot) is { IsImprisoned: true } cold) Defeated(cold.RecordId);
        }
    }

    void Defeated(string record)
    {
        if (!ArcaneRules.Magi.TryGetValue(record, out var magus) || magus.Spell == Spell.Missile || !_unlocked.Add(magus.Spell)) return;

        Notice(ArcaneRules.Unlocked(magus.Spell));
    }

    /// <summary>The original suppresses the crossbow XP of a Focus: its holder's Crossbows XP from shooting is none.</summary>
    public float Gain(World world, in ExperienceGain gain)
    {
        if (gain.Skill != StatsEnumerated.Crossbows || gain.Source != ExperienceSource.Ranged) return gain.Amount;

        bool focus = world.Characters.TryResolveNext(gain.Character, out int slot) && HoldsFocus(world.Characters.Cold(slot));
        return focus ? 0 : gain.Amount;
    }

    #region Save data

    /// <summary>Casters read from the save by lasting id, set on the characters as the world is ready; summons likewise.</summary>
    readonly Dictionary<ulong, (float Xp, float Mana, Spell Spell)> _saved = [];
    readonly List<(ulong Creature, ulong Caster, float Left)> _savedSummons = [];

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(1);
        WriteSpells(w, _unlocked);
        WriteSpells(w, _manualOn);
        WriteSpells(w, _manualOff);

        var kept = _casters.Where(p => !CharacterOf(p.Key).IsNone).ToList();
        w.Write(kept.Count);
        foreach (var (id, caster) in kept)
        {
            w.Write(CharacterOf(id).Value);
            w.Write(caster.Xp);
            w.Write(caster.Mana);
            w.Write((int)caster.Spell);
        }

        w.Write(_summons.Count);
        foreach (var (creature, caster, left) in _summons)
        {
            w.Write(CharacterOf(creature).Value);
            w.Write(CharacterOf(caster).Value);
            w.Write(left);
        }
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != 1) return;

        ReadSpells(r, _unlocked);
        ReadSpells(r, _manualOn);
        ReadSpells(r, _manualOff);
        for (int n = r.ReadInt32(); n > 0; n--) _saved[r.ReadUInt64()] = (r.ReadSingle(), r.ReadSingle(), (Spell)r.ReadInt32());
        for (int n = r.ReadInt32(); n > 0; n--) _savedSummons.Add((r.ReadUInt64(), r.ReadUInt64(), r.ReadSingle()));
    }

    void RestoreSaved(World world)
    {
        foreach (var (lasting, (xp, mana, spell)) in _saved)
        {
            var id = world.LastingIds.Find(new LastingId(lasting));
            if (!id.IsNone) _casters[id] = new Caster { Xp = xp, Mana = mana, Spell = spell };
        }

        foreach (var (creature, caster, left) in _savedSummons)
        {
            var made = world.LastingIds.Find(new LastingId(creature));
            if (!made.IsNone) _summons.Add((made, world.LastingIds.Find(new LastingId(caster)), left));
        }

        _saved.Clear();
        _savedSummons.Clear();
    }

    /// <summary>The lasting id a character has now (none until given); bound to the world by <see cref="Running"/>'s last world.</summary>
    LastingId CharacterOf(CharacterId id) => _world?.Characters.TryResolveNext(id, out int slot) == true
        ? _world.Characters.Cold(slot)?.LastingId ?? default
        : default;

    static void WriteSpells(BinaryWriter w, HashSet<Spell> spells)
    {
        w.Write(spells.Count);
        foreach (var spell in spells.Order()) w.Write((int)spell);
    }

    static void ReadSpells(BinaryReader r, HashSet<Spell> spells)
    {
        spells.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) spells.Add((Spell)r.ReadInt32());
    }

    #endregion
}
