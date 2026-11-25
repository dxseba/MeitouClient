using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.DirectControl;

/// <summary>
/// Manual combat: the swing gate (<see cref="AttackGate"/>) and the defence pick (<see cref="ICombatDefenceFilter"/>). Both run in the combat act
/// phase on the controlled character's own thread, so the press state they consume is written only from that one slot.
/// </summary>
sealed partial class ControlSystem : ICombatDefenceFilter
{
    /// <summary>
    /// The world's combat system, for its techniques.
    /// </summary>
    CombatSystem? _combat;

    CombatSystem? CombatOf(World world) => _combat ??= world.Systems.OfType<CombatSystem>().FirstOrDefault();

    /// <summary>
    /// Whether slot <paramref name="i"/> may start a swing this tick: a character other than the controlled one always may; the controlled one
    /// never while it is steered (the original: no attack while actively moving), and in manual combat only within the window after an Attack
    /// press (the step-in time when the press asked to approach).
    /// </summary>
    public bool MaySwing(World world, int i)
    {
        if (Controlled.IsNone || !world.Characters.TryResolveNext(Controlled, out int slot) || slot != i) return true;
        if (_steering || _sheathed) return false;
        if (!Manual) return true;

        int window = _attackApproach ? Math.Max(_tuning.AttackApproachMs, _tuning.AttackWindowMs) : _tuning.AttackWindowMs;
        return world.Tick - _attackTick <= Ticks(world, window);
    }

    /// <summary>
    /// The Action RPG scheme's direction for the controlled character's next swing (0: any).
    /// </summary>
    public int SwingDirection(World world, int i) =>
        _tuning.Directional && world.Characters.TryResolveNext(Controlled, out int slot) && slot == i ? _attackDirection : 0;

    /// <summary>
    /// A swing started: its press is used up (one press, one swing).
    /// </summary>
    public void Swung(World world, int i)
    {
        if (world.Characters.TryResolveNext(Controlled, out int slot) && slot == i) _attackTick = Never;
    }

    public int Defence(World world, in DefenceChoiceAsk ask)
    {
        if (!Manual || ask.Defender != Controlled || ask.Stumbling || CombatOf(world) is not { } combat) return ask.Technique;
        if (_sheathed) return -1;
        if (world.Characters.Cold(ask.Defender.Slot) is not { Fighter: { } fighter, Medical: { } medical }) return ask.Technique;

        var techniques = combat.Techniques;
        int category = fighter.TechniqueCategory;
        ulong roll = Rng.Hash(world.Seed, Rng.Key(ask.Defender), purpose, (ulong)ask.Tick ^ ((ulong)(uint)ask.Sequence << 32) ^ (uint)ask.Blow);

        if (DodgePick(world, ask, techniques, category, medical.Prone, roll) is { } dodge) return dodge;
        if (fighter.CanBlock && ForcedBlock(world, ask) && BlockFor(techniques, category, medical.Prone, ask.Direction, roll) is { } block)
        {
            Perfect(world, ask);
            return block;
        }

        return Own(ask, techniques);
    }

    /// <summary>
    /// A timed dodge: a press within the dodge window and past the cooldown rolls the effective dodge plus the timing bonus against the attacker's
    /// skill (the game's chance formula, 0 to 95); a success takes a dodge technique for the blow's side. The press is used either way.
    /// </summary>
    int? DodgePick(World world, in DefenceChoiceAsk ask, IReadOnlyList<CombatTechnique> techniques, int category, bool prone, ulong roll)
    {
        long now = ask.Tick;
        if (now - _dodgeTick > Ticks(world, _tuning.DodgeWindowMs) || now < _dodgeReady) return null;

        _dodgeTick = Never;
        _dodgeReady = now + Ticks(world, _tuning.DodgeCooldownMs + _tuning.DodgeRecoveryMs);
        float dodge = (CombatSystem.EffectiveDodge(world, ask.Defender.Slot) ?? 0) + _tuning.DodgeSkillBonus;
        float attack = CombatSystem.AttackSkill(world, ask.Attacker.Slot) ?? 0;
        float chance = DefenceFormulas.BlockChance(dodge, attack, constants, 0, 95);
        if (Rng.Float(roll) * 100 >= chance) return null;

        int pick = TechniqueChooser.ChooseReaction(techniques, category, prone, false, ask.Direction, 0, 100, 0, Rng.Float(Rng.Mix(roll)));
        return pick >= 0 && techniques[pick].IsDodge ? pick : null;
    }

    /// <summary>
    /// Whether this blow is met by a block whatever the skill: a later blow of an attack a perfect block met; a blow from the front within the
    /// perfect-block window after a Block tap (or while the reward lasts and Block is held); in the Action RPG scheme a held block whose direction
    /// is the blow's (0: any direction).
    /// </summary>
    bool ForcedBlock(World world, in DefenceChoiceAsk ask)
    {
        if (_perfect == (ask.Attacker.Slot, ask.Sequence)) return true;
        if (!Front(world, ask)) return false;
        if (_tuning.Directional) return _blockHeld && (_blockDirection == 0 || _blockDirection == ask.Direction);

        long now = ask.Tick;
        bool tapped = now - _blockTick <= Ticks(world, _tuning.PerfectBlockWindowMs);
        return tapped || _blockHeld && now < _rewardUntil;
    }

    /// <summary>
    /// A perfect block: the reward starts (the next press counts at once, the weapon glows blue) and the attack's later blows are covered.
    /// </summary>
    void Perfect(World world, in DefenceChoiceAsk ask)
    {
        _perfect = (ask.Attacker.Slot, ask.Sequence);
        _blockTick = Never;
        _rewardUntil = ask.Tick + Ticks(world, _tuning.PerfectBlockRewardMs);
        LastPerfect = ask.Tick;
    }

    /// <summary>
    /// The attacker is in front of the defender (a perfect block covers only hits from the front; dodges cover every side).
    /// </summary>
    static bool Front(World world, in DefenceChoiceAsk ask)
    {
        var prev = world.Characters.Previous;
        var defender = prev[ask.Defender.Slot];
        return !HitOutcomes.Behind(prev[ask.Attacker.Slot].Position, defender.Position, defender.Yaw);
    }

    /// <summary>
    /// A standing block technique for the blow's side, from the game's own reaction table (the roll forced to succeed).
    /// </summary>
    static int? BlockFor(IReadOnlyList<CombatTechnique> techniques, int category, bool prone, int direction, ulong roll)
    {
        int pick = TechniqueChooser.ChooseReaction(techniques, category, prone, true, direction, 100, 0, 0, Rng.Float(Rng.Mix(roll ^ 0x5bd1e995)));
        return pick >= 0 && techniques[pick] is { IsBlock: true, IsDodge: false } ? pick : null;
    }

    /// <summary>
    /// No timed press: a held Block keeps the game's own roll (with the Block order's defence bonus); otherwise the character blocks or dodges on
    /// its own only as the settings allow.
    /// </summary>
    int Own(in DefenceChoiceAsk ask, IReadOnlyList<CombatTechnique> techniques)
    {
        int pick = ask.Technique;
        if (pick < 0 || pick >= techniques.Count) return pick;
        if (_tuning.Directional) return _blockHeld && _tuning.AiBlocks ? pick : -1;
        if (_blockHeld) return pick;

        var t = techniques[pick];
        if (t.IsDodge) return _tuning.AiDodges ? pick : -1;

        return _tuning.AiBlocks ? pick : -1;
    }
}
