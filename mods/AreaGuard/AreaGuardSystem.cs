using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Dialogue;

namespace Meitou.AreaGuard;

/// <summary>
/// One blocked blow: who swung, who blocked, the swing sequence, the blow index and the tick.
/// </summary>
readonly record struct BlockRecord(CharacterId Attacker, CharacterId Blocker, int Sequence, int Blow, long Tick);

/// <summary>
/// The rule: a melee blow about to land on a character is spared when another character already blocked the same blow this tick (same
/// attacker, sequence, blow index) and the protection mode accepts the victim from that blocker's side. The commit applies a blow's bodies one
/// after another and reports each block at once (<see cref="ICombatDefenceObserver"/>), so only bodies after the block are spared, as in the
/// original.
/// </summary>
sealed class AreaGuardSystem(AreaGuardSettings settings, CombatSystem? combat, AggroSystem? aggro, DialogueSystem? dialogue, Action<string> log)
    : ICombatHitFilter, ICombatDefenceObserver, IStateHashed
{
    #region Blocks

    /// <summary>
    /// This tick's blocks; cleared once the tick moves on.
    /// </summary>
    readonly List<BlockRecord> _blocks = [];

    /// <summary>
    /// The tick <see cref="_blocks"/> belong to.
    /// </summary>
    long _tick = -1;

    /// <summary>
    /// Notes a block (the original hooks <c>CombatClass::_blockHit</c>, which a dodge does not reach), serially in the commit.
    /// </summary>
    public void Defended(World world, in CombatDefence defence)
    {
        if (!settings.Enabled || defence.Kind != CombatDefenceKind.Blocked) return;

        Roll(defence.Tick);
        _blocks.Add(new BlockRecord(defence.Attacker, defence.Defender, defence.Sequence, defence.Blow, defence.Tick));
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_tick);
        hasher.Add(_blocks.Count);
        foreach (var b in _blocks)
        {
            hasher.Add(b.Attacker);
            hasher.Add(b.Blocker);
            hasher.Add(b.Sequence);
            hasher.Add(b.Blow);
        }
    }

    /// <summary>
    /// Forgets the blocks of earlier ticks.
    /// </summary>
    void Roll(long tick)
    {
        if (tick == _tick) return;

        _blocks.Clear();
        _tick = tick;
    }

    #endregion

    #region Protection

    /// <summary>
    /// Spares a later body of a blocked blow: a miss (NoReaction, the original skips the hit call) or an emptied packet (HitReaction, the
    /// original's <c>Damages::multiply(0)</c> around the hit call).
    /// </summary>
    public void HitLanding(World world, ref PendingHit hit)
    {
        if (!settings.Enabled || hit.Source != CombatHitSource.Melee) return;

        Roll(hit.Tick);
        if (Protector(world, hit) is not { } block) return;

        if (settings.Reaction == ReactionMode.NoReaction) hit.Cancelled = true;
        else hit.Packet = hit.Packet with { Cut = 0, Blunt = 0, Pierce = 0, Stun = 0 };

        if (settings.DebugLogging) log($"protected {hit.Victim} from {hit.Attacker} (blocked by {block.Blocker}, blow {hit.Blow}, {settings.Reaction})");
    }

    /// <summary>
    /// The block of this same blow by another character that protects the victim; null when none does.
    /// </summary>
    BlockRecord? Protector(World world, in PendingHit hit)
    {
        foreach (var b in _blocks)
        {
            if (b.Attacker != hit.Attacker || b.Sequence != hit.Sequence || b.Blow != hit.Blow || b.Blocker == hit.Victim) continue;
            if (Accepts(world, b.Blocker, hit.Victim)) return b;
        }

        return null;
    }

    /// <summary>
    /// The protection mode, asked of the blocker about the victim: Friendly is <c>Character::isAlly(victim, FactorInDisguises)</c>, NonEnemy is
    /// not <c>Character::isEnemy(victim, FactorInDisguises)</c> (original DLL, decompiled: the blocker's vfuncs 0x3f0 and 0x3e8).
    /// </summary>
    bool Accepts(World world, CharacterId blocker, CharacterId victim)
    {
        if (settings.Protection == ProtectionMode.All) return true;

        var table = world.Characters;
        if (!table.TryResolveNext(blocker, out int b) || !table.TryResolveNext(victim, out int v)) return false;
        if (table.Cold(b) is not { } bc || table.Cold(v) is not { } vc) return false;

        return settings.Protection == ProtectionMode.Friendly ? RegardsFriendly(world, b, v, bc, vc) : !RegardsEnemy(world, b, v, bc, vc);
    }

    /// <summary>
    /// The game's "A regards B as friendly" (slot 126): squad memory, love and allied factions; without the dialogue system the same or allied
    /// factions.
    /// </summary>
    bool RegardsFriendly(World world, int b, int v, CharacterCold bc, CharacterCold vc)
    {
        if (dialogue is not null) return dialogue.RegardsFriendly(world, b, v, settings.FactorInDisguises);

        int seen = Seen(vc);
        return bc.Faction == vc.Faction || Relations is { } r && r.Contains(bc.Faction) && r.Contains(seen) && r.IsAlly(bc.Faction, seen);
    }

    /// <summary>
    /// The game's "A regards B as an enemy" (slot 125): never the same faction; the squad memory's TEMP_ENEMY / TEMP_ALLY first; else a
    /// faction hostile to A's (the victim's disguise counted when set).
    /// </summary>
    bool RegardsEnemy(World world, int b, int v, CharacterCold bc, CharacterCold vc)
    {
        if (bc.Faction == vc.Faction) return false;
        if (dialogue?.EnemyMemory(world, b, v) is { } memory and not 0) return memory > 0;

        int seen = Seen(vc);
        return Relations is { } r && r.Contains(bc.Faction) && AggroSystem.IsEnemy(r, bc.Faction, seen);
    }

    /// <summary>
    /// The victim's faction as the blocker sees it: its working disguise's with <see cref="AreaGuardSettings.FactorInDisguises"/>.
    /// </summary>
    int Seen(CharacterCold victim) => settings.FactorInDisguises ? ArrestSystem.SeenFaction(victim) : victim.Faction;

    /// <summary>
    /// The world's relation table (the look's, else the combat's).
    /// </summary>
    FactionRelations? Relations => aggro?.Relations ?? combat?.Relations;

    #endregion
}
