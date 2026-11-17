using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Crime;

namespace Meitou.AreaGuard;

/// <summary>One blocked blow: who swung, who blocked, the swing sequence, the blow index and the tick.</summary>
readonly record struct BlockRecord(CharacterId Attacker, CharacterId Blocker, int Sequence, int Blow, long Tick);

/// <summary>
/// The hit filter: a melee blow about to land on a character is spared when another character already blocked the same blow this tick (same
/// attacker, sequence, blow index) and the protection mode accepts the victim from that blocker's side. The commit applies a sweep's bodies one
/// after another, so only bodies after the block are spared, as in the original.
/// </summary>
sealed class AreaGuardSystem(AreaGuardSettings settings, CombatSystem? combat, AggroSystem? aggro) : ICombatHitFilter, IStateHashed
{
    /// <summary>This tick's blocks; cleared once the tick moves on.</summary>
    readonly List<BlockRecord> _blocks = [];

    long _tick = -1;

    /// <summary>Called from the block postfix, serially in the commit.</summary>
    public void Blocked(BlockRecord block)
    {
        if (!settings.Enabled) return;

        Roll(block.Tick);
        _blocks.Add(block);
    }

    public void HitLanding(World world, ref PendingHit hit)
    {
        if (!settings.Enabled || hit.Source != CombatHitSource.Melee) return;

        Roll(hit.Tick);
        if (Protector(world, hit) is null) return;

        if (settings.Reaction == ReactionMode.NoReaction) hit.Cancelled = true;
        else hit.Packet = hit.Packet with { Cut = 0, Blunt = 0, Pierce = 0, Stun = 0 };
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

    /// <summary>Forgets the blocks of earlier ticks.</summary>
    void Roll(long tick)
    {
        if (tick == _tick) return;

        _blocks.Clear();
        _tick = tick;
    }

    /// <summary>The block of this same blow by another character that protects the victim; null when none does.</summary>
    BlockRecord? Protector(World world, in PendingHit hit)
    {
        foreach (var b in _blocks)
        {
            if (b.Attacker != hit.Attacker || b.Sequence != hit.Sequence || b.Blow != hit.Blow || b.Blocker == hit.Victim) continue;
            if (Accepts(world, b.Blocker, hit.Victim)) return b;
        }

        return null;
    }

    /// <summary>The protection mode, read from the blocker's side.</summary>
    bool Accepts(World world, CharacterId blocker, CharacterId victim)
    {
        if (settings.Protection == ProtectionMode.All) return true;

        var table = world.Characters;
        if (!table.TryResolveNext(blocker, out int b) || !table.TryResolveNext(victim, out int v)) return false;
        if (table.Cold(b) is not { } bc || table.Cold(v) is not { } vc || Relations is not { } relations) return false;

        int mine = bc.Faction;
        int theirs = settings.FactorInDisguises ? ArrestSystem.SeenFaction(vc) : vc.Faction;
        return settings.Protection == ProtectionMode.Friendly ? IsAlly(relations, mine, theirs) : !AggroSystem.IsEnemy(relations, mine, theirs);
    }

    static bool IsAlly(FactionRelations relations, int mine, int theirs) =>
        mine == theirs || relations.Contains(mine) && relations.Contains(theirs) && relations.IsAlly(mine, theirs);

    /// <summary>The world's relation table (the look's, else the combat's).</summary>
    FactionRelations? Relations => aggro?.Relations ?? combat?.Relations;
}
