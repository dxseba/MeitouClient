using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.AttackSpeedUncapped;

/// <summary>
/// The original's <c>CharStats::calculateAttackOrBlockSpeed</c> hook as a combat speed filter (<b>Verified</b>, <c>SpeedCapTuner.dll</c>
/// decompiled): when the game's rate (before the swimming factor) is at least 1.199 and the chosen cap is above 1.201, the rate is worked out again
/// from the fighter's dexterity, its attack (or defence) rating and its weapon and gear factors, the larger of the two taken, held to the cap. The
/// same formula serves attacks and blocks. The caps are copied from the settings in <see cref="Inputs"/> (serial), so every partition of a tick
/// sees the same ones.
/// </summary>
sealed class SpeedCapSystem(XpService xp) : ICombatSpeedFilter, IStateHashed
{
    /// <summary>The original's gates: the game's rate at least <see cref="AtCap"/>, the cap above <see cref="Raised"/>.</summary>
    const float AtCap = 1.199f, Raised = 1.201f;

    /// <summary>The original's recalculated rate: <see cref="DexTerm"/> dexterity + <see cref="SkillTerm"/> skill + <see cref="Base"/>.</summary>
    const float DexTerm = 0.00228f, SkillTerm = 0.00152f, Base = 0.765f;

    /// <summary>Below this the gear's combat speed factor counts as 1 (the original's 0.001 test).</summary>
    const float SmallestCombatSpeed = 0.001f;

    /// <summary>The game's swimming factor on both rates (water state 3).</summary>
    const float Swimming = 0.8f;

    float _attackCap = 1.2f, _blockCap = 4;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        var settings = AttackSpeedUncappedMod.Settings;
        _attackCap = settings.Attack;
        _blockCap = settings.Block;
    }

    public float Rate(World world, in CombatSpeedAsk ask)
    {
        float cap = ask.Kind switch
        {
            CombatSpeedKind.Attack => _attackCap,
            CombatSpeedKind.Block => _blockCap,
            _ => 0,
        };
        if (cap <= Raised || !world.Characters.IsAlive(ask.Character)) return ask.Rate;

        int slot = ask.Character.Slot;
        if (world.Characters.Cold(slot) is not { Fighter: { } fighter, Stats: { } stats, Medical: { } medical } cold) return ask.Rate;

        float swim = cold.WaterState == 3 ? Swimming : 1;
        float vanilla = ask.Rate / swim;
        if (vanilla < AtCap) return ask.Rate;

        float? skill = ask.Kind == CombatSpeedKind.Attack ? CombatSystem.AttackSkill(world, slot) : CombatSystem.MeleeDefence(world, slot);
        if (skill is not { } rating) return ask.Rate;

        float dex = stats[StatsEnumerated.Dexterity] * stats.Scale;
        float recalc = (DexTerm * dex + SkillTerm * rating + Base) * WeaponFactor(fighter, stats, medical) * CombatSpeed(fighter);
        return Math.Max(0, Math.Min(Math.Max(vanilla, recalc), cap)) * swim;
    }

    /// <summary>
    /// The game's weapon-weight term of the play rate: 0.5 + 0.5 min(1, encumbrance + 0.5) (1 - f), f the weapon-weight-over-strength factor of
    /// the strength XP held to [0, 1].
    /// </summary>
    float WeaponFactor(Fighter fighter, CharacterStats stats, MedicalState medical)
    {
        float weight = fighter.Unarmed ? 0 : fighter.Weapon?.Stats.Weight ?? 0;
        float strength = stats[StatsEnumerated.Strength];
        float f = Math.Clamp(xp.WeaponWeightStrengthFactor(weight, strength, medical.StatMultiplier(StatsEnumerated.Strength)), 0, 1);
        return 0.5f + 0.5f * MathF.Min(1, fighter.EncumbranceFactor + 0.5f) * (1 - f);
    }

    static float CombatSpeed(Fighter fighter) => fighter.CombatSpeed > SmallestCombatSpeed ? fighter.CombatSpeed : 1;

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_attackCap);
        hasher.Add(_blockCap);
    }
}
