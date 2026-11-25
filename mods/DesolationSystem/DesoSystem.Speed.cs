using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// The original's <c>calculateAttackOrBlockSpeed</c> hook (<b>Verified</b>, decompiled) as a combat speed filter. An attack at the game's cap
/// (1.199) under a raised cap (above 1.201) is worked out again from dexterity, attack skill and the weapon and gear factors (the Speed Cap tuner's
/// formula); then, in the original's order, the Ryu style, Universal Mastery x1.5, Athletics Momentum, Martial Burst (bare hands) and a polearm's
/// slow multiply it; it is held to the cap and a Lightning Counter burst multiplies it (x1.5). Blocks take Athletics, Universal Mastery and the
/// slow, held to 4 (8 at a cap of 9.59 and up). Reloads take Squad Volley. A pure read of the stats and the published view.
/// </summary>
sealed partial class DesoSystem : ICombatSpeedFilter
{
    const float AtCap = 1.199f, Raised = 1.201f, DexTerm = 0.00228f, SkillTerm = 0.00152f, Base = 0.765f, SwimFactor = 0.8f;

    internal XpService? Xp { get; init; }

    public float Rate(World world, in CombatSpeedAsk ask)
    {
        if (!world.Characters.TryResolveNext(ask.Character, out int slot) || world.Characters.Cold(slot) is not { Stats: { } stats } cold)
            return ask.Rate;

        var cfg = _cfg;
        bool scoped = Gates.InScope(cold, cfg.ScopeIndex);
        return ask.Kind switch
        {
            CombatSpeedKind.Attack => Attack(world, slot, cold, stats, cfg, scoped, ask),
            CombatSpeedKind.Block => Block(world, cold, stats, cfg, scoped, ask),
            CombatSpeedKind.Reload when scoped && cfg.P15 => ask.Rate * Volley(cold, stats, cfg),
            _ => ask.Rate,
        };
    }

    float Attack(World world, int slot, CharacterCold cold, CharacterStats stats, DesoSettings cfg, bool scoped, in CombatSpeedAsk ask)
    {
        float cap = cfg.AttackCap;
        float swim = cold.WaterState == 3 ? SwimFactor : 1;
        float rate = ask.Rate / swim;
        if (_view.WildSquads.Contains(cold.SquadId) && Gates.IsAnimal(cold) && cfg.AscWldBuff) rate *= 1.2f;
        if (rate >= AtCap && cap > Raised) rate = Math.Max(rate, Recalculated(world, slot, cold, stats));

        if (scoped) rate *= RyuSpeed(ask.Character, stats, cfg) * Momentum(stats, cfg) * Martial(cold, stats, cfg);
        if (cfg.AscMartSwift && Gates.Reached(cold, cfg, Gates.Realm.Universal)) rate *= 1.5f;
        rate *= SlowOf(world, ask.Character);
        rate = Math.Clamp(rate, 0, cap);
        if (_view.Bursts.TryGetValue(ask.Character, out long until) && world.Tick < until) rate *= 1.5f;

        return rate > 0 && float.IsFinite(rate) ? rate * swim : ask.Rate;
    }

    float Block(World world, CharacterCold cold, CharacterStats stats, DesoSettings cfg, bool scoped, in CombatSpeedAsk ask)
    {
        float rate = ask.Rate;
        if (scoped) rate *= Momentum(stats, cfg);
        if (cfg.AscMartSwift && Gates.Reached(cold, cfg, Gates.Realm.Universal)) rate *= 1.5f;
        rate *= SlowOf(world, ask.Character);
        float cap = cfg.AttackCap >= 9.59f ? 8 : 4;
        rate = Math.Min(rate, Math.Max(cap, ask.Rate));
        return rate > 0 && float.IsFinite(rate) ? rate : ask.Rate;
    }

    /// <summary>The tuner's rate: (0.00228 dex + 0.00152 attack + 0.765) x the weapon-weight term x the gear's combat speed.</summary>
    float Recalculated(World world, int slot, CharacterCold cold, CharacterStats stats)
    {
        if (cold is not { Fighter: { } fighter, Medical: { } medical } || CombatSystem.AttackSkill(world, slot) is not { } skill || Xp is not { } xp)
            return 0;

        float dex = stats[Dexterity] * stats.Scale;
        float weight = fighter.Unarmed ? 0 : fighter.Weapon?.Stats.Weight ?? 0;
        float f = Math.Clamp(xp.WeaponWeightStrengthFactor(weight, stats[Strength], medical.StatMultiplier(Strength)), 0, 1);
        float weaponTerm = 0.5f + 0.5f * MathF.Min(1, fighter.EncumbranceFactor + 0.5f) * (1 - f);
        float gear = fighter.CombatSpeed > 0.001f ? fighter.CombatSpeed : 1;
        return (DexTerm * dex + SkillTerm * skill + Base) * weaponTerm * gear;
    }

    float RyuSpeed(CharacterId id, CharacterStats stats, DesoSettings cfg)
    {
        if (!cfg.Ryu || !_view.Styles.TryGetValue(id, out int style)) return 1;

        return Ryu(style, Mastery.RyuFactor(Gates.L(stats, MeleeAttack))).Speed;
    }

    /// <summary>The Ryu style's (speed, damage, penetration) at factor t (<b>Verified</b>): Gale, Balance, Crush.</summary>
    internal static (float Speed, float Damage, float Pierce) Ryu(int style, float t) => style switch
    {
        0 => (1 + 0.2f * t, 1 + Math.Max(-0.9f, -0.2f * t), 0),
        1 => (1 + 0.1f * t, 1 + 0.2f * t, 0),
        2 => (1 + Math.Max(-0.45f, -0.2f * t), 1 + 0.4f * t, 0.2f * t),
        _ => (1, 1, 0),
    };

    static float Momentum(CharacterStats stats, DesoSettings cfg) => cfg.P210 ? Perks.Momentum(Gates.L(stats, StatsEnumerated.Athletics)) : 1;

    static float Martial(CharacterCold cold, CharacterStats stats, DesoSettings cfg)
    {
        float level = Gates.L(stats, MartialArts);
        if (!cfg.P13 || cold.Fighter is not { Unarmed: true } || Gates.IsAnimal(cold) || level <= cfg.MasteryThreshold) return 1;

        return 1 + Mastery.Value(level, 0.08f, 0.30f);
    }

    float SlowOf(World world, CharacterId id) => _view.Slows.TryGetValue(id, out var slow) && world.Tick < slow.Until ? 1 - slow.Amount : 1;

    /// <summary>Squad Volley: each humanoid squadmate cuts the reload time by 0 / 1 / 4 % (at 60 / 80 / 100), at most 40 % in all.</summary>
    float Volley(CharacterCold cold, CharacterStats stats, DesoSettings cfg)
    {
        float level = Gates.L(stats, FriendlyFire);
        if (level <= cfg.MasteryThreshold || cold.SquadId < 0) return 1;

        int mates = Math.Max(0, _view.Humanoids.GetValueOrDefault(cold.SquadId) - 1);
        float cut = Math.Min(0.4f, mates * Mastery.Value(level, 0, 0.04f));
        return 1 / (1 - cut);
    }
}
