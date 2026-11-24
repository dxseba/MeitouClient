using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.UncapDefence;

/// <summary>
/// The original's <c>CharStats::_calculateBlockChance</c> and <c>calculateDodgeChance</c> hooks as a defence choice filter: the game's chance of
/// the defender (block for an armed fighter, dodge for one that cannot block) is changed by <see cref="UncapRule"/>, and the defence is picked again
/// by the game's own chooser with the game's own two rolls (<see cref="CombatRolls"/>, as <c>CombatSystem.ChooseDefence</c> draws them), so a
/// defence the vanilla chance won stays won while the new chance is higher, and the reverse. Every fighter, both ways (the original's global scope).
/// A pick another filter changed, or one of a stumbling defender (its break-out dodge), is kept. Settings are copied in <see cref="Inputs"/>.
/// </summary>
sealed class UncapFilter : ICombatDefenceFilter, IStateHashed
{
    /// <summary>The game's elephant moves category (<c>CombatSystem.ElephantCategory</c>), which animals with drawn natural weapons use.</summary>
    const int ElephantCategory = 9;

    UncapRule _rule;

    CombatSystem? _combat;

    /// <summary>The last block and dodge chances changed (vanilla, now; fractions), for the readout card; display only.</summary>
    internal static volatile Readout? LastBlock, LastDodge;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands) => _rule = UncapRule.Of(UncapDefenceMod.Settings);

    public int Defence(World world, in DefenceChoiceAsk ask)
    {
        var rule = _rule;
        if (!rule.On || ask.Stumbling || CombatOf(world) is not { } combat) return ask.Technique;

        var table = world.Characters;
        if (!table.IsAlive(ask.Defender) || !table.IsAlive(ask.Attacker)) return ask.Technique;
        if (table.Cold(ask.Defender.Slot) is not { Fighter: { } fighter, Medical: { } medical }) return ask.Technique;

        bool block = fighter.CanBlock;
        float? own = block ? CombatSystem.MeleeDefence(world, ask.Defender.Slot) : CombatSystem.EffectiveDodge(world, ask.Defender.Slot);
        if (own is not { } defence || CombatSystem.AttackSkill(world, ask.Attacker.Slot) is not { } attack) return ask.Technique;

        float vanilla = block ? ask.BlockChance : ask.DodgeChance;
        float chance = rule.Chance(vanilla / 100, defence - attack) * 100;
        Publish(block, vanilla, chance);
        if (chance == vanilla) return ask.Technique;

        int category = combat.AnimalMovesFix && fighter.WeaponDrawn ? ElephantCategory : fighter.TechniqueCategory;
        var (chanceRoll, pickRoll) = Rolls(world, ask);
        int direction = ask.Direction;
        int Pick(float c) => TechniqueChooser.ChooseReaction(combat.Techniques, category, medical.Prone, block, direction, block ? c : 0,
            block ? 0 : c, chanceRoll, pickRoll);

        // another filter's pick is left alone
        if (Pick(vanilla) != ask.Technique) return ask.Technique;

        return Pick(chance);
    }

    /// <summary>The game's chance and pick rolls of this decision (<c>CombatSystem.ChooseDefence</c>: the blow's counter salted by the tick).</summary>
    static (float Chance, float Pick) Rolls(World world, in DefenceChoiceAsk ask)
    {
        ulong key = Rng.Key(ask.Defender);
        ulong counter = CombatRolls.BlowCounter(ask.Attacker.Slot, ask.Sequence, ask.Blow, 0) ^ (ulong)(uint)(int)ask.Tick * 0x9E3779B97F4A7C15UL;
        return (CombatRolls.Float(world.Seed, key, CombatRoll.Block, counter), CombatRolls.Float(world.Seed, key, CombatRoll.Reaction, counter));
    }

    static void Publish(bool block, float vanilla, float chance)
    {
        var readout = new Readout(vanilla / 100, chance / 100);
        if (block) LastBlock = readout;
        else LastDodge = readout;
    }

    CombatSystem? CombatOf(World world) => _combat ??= world.Systems.OfType<CombatSystem>().FirstOrDefault();

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_rule.Mode);
        hasher.Add(_rule.Scale);
        hasher.Add(_rule.FullGap);
        hasher.Add(_rule.Ceiling);
        hasher.Add(_rule.Floor);
    }
}

/// <summary>One changed chance: the game's and the mod's (fractions).</summary>
sealed record Readout(float Vanilla, float Now);
