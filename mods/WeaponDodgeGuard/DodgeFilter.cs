using System.Collections.Concurrent;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Player;

namespace Meitou.WeaponDodgeGuard;

/// <summary>
/// The dodge before the guard (the original's <c>chooseBlock</c> hook, <b>Verified</b> original DLL (decompiled)), as an
/// <see cref="ICombatDefenceFilter"/>: only an armed fighter whose pick is a standing block, standing normally (with Require standing), not in
/// defensive mode (unless allowed), past its cooldown and (with Adaptive priority) not better at guarding rolls the unarmed dodge chance, scaled by
/// Chance scale and clamped to the minimum and maximum; on success its block becomes a dodge technique (<see cref="DodgeTechniques"/>), else the
/// guard stands. Any thread: reads the defender and attacker as the tick settled them; the cooldown is per defender, written from its own asks.
/// </summary>
sealed class DodgeFilter(CombatConstants constants, RngPurpose purpose) : ICombatDefenceFilter
{
    /// <summary>
    /// The last weapon-dodge tick per defender (for the cooldown).
    /// </summary>
    readonly ConcurrentDictionary<CharacterId, long> _lastDodge = new();

    /// <summary>
    /// The world's combat system, for its techniques (found on the first ask; every thread finds the same one).
    /// </summary>
    CombatSystem? _combat;

    public int Defence(World world, in DefenceChoiceAsk ask)
    {
        if (!WeaponDodgeGuardMod.Settings.Enabled || ask.Stumbling || CombatOf(world) is not { } combat) return ask.Technique;
        if (!StandingBlock(combat, ask.Technique) || Dodge(world, combat, ask) is not { } pick) return ask.Technique;

        if (WeaponDodgeGuardMod.Settings.CooldownMs > 0) _lastDodge[ask.Defender] = world.Tick;
        return pick;
    }

    /// <summary>
    /// The dodge technique the defender takes up instead of its guard, or null when the gate or the roll keeps the guard.
    /// </summary>
    int? Dodge(World world, CombatSystem combat, in DefenceChoiceAsk ask)
    {
        var table = world.Characters;
        if (!table.IsAlive(ask.Defender) || table.Cold(ask.Defender.Slot) is not { Fighter: { CanBlock: true } fighter, Medical: { } medical } cold)
        {
            return null;
        }

        if (!Eligible(cold, medical) || CoolingDown(world, ask.Defender)) return null;
        if (!table.IsAlive(ask.Attacker) || table.Cold(ask.Attacker.Slot) is not { } attacker) return null;

        float dodge = EffectiveDodge(cold, fighter, medical);
        if (WeaponDodgeGuardMod.Settings.Adaptive && dodge < EffectiveMeleeDefence(cold, fighter)) return null;

        ulong roll = Rng.Hash(world.Seed, Rng.Key(ask.Defender), purpose, (ulong)(uint)ask.Tick);
        if (Rng.Float(roll) * 100 >= Chance(dodge, attacker)) return null;

        return DodgeTechniques.Pick(combat.Techniques, dodge, Rng.Float(Rng.Mix(roll)));
    }

    CombatSystem? CombatOf(World world) => _combat ??= world.Systems.OfType<CombatSystem>().FirstOrDefault();

    /// <summary>
    /// The vanilla pick is a block that is not a dodge and not prone (a dodge or no reaction is kept).
    /// </summary>
    static bool StandingBlock(CombatSystem combat, int pick) =>
        pick >= 0 && pick < combat.Techniques.Count && combat.Techniques[pick] is { IsBlock: true, IsDodge: false, IsProne: false };

    static bool Eligible(CharacterCold cold, MedicalState medical)
    {
        var settings = WeaponDodgeGuardMod.Settings;
        if (settings.RequireStanding && medical.Prone) return false;

        return settings.AllowInDefensiveMode || !Orders.Has(cold, CharacterOrders.Block);
    }

    /// <summary>
    /// <c>DodgeCooldownMs</c> after the defender's last weapon dodge (game time here: the original's clock is the wall's).
    /// </summary>
    bool CoolingDown(World world, CharacterId id)
    {
        int ms = WeaponDodgeGuardMod.Settings.CooldownMs;
        if (ms <= 0 || !_lastDodge.TryGetValue(id, out long last)) return false;

        return (world.Tick - last) * world.TickSeconds * 1000 < ms;
    }

    /// <summary>
    /// The scaled chance: the vanilla dodge chance against the attacker's skill, held to [0, 100], times Chance scale, held to the minimum and
    /// maximum.
    /// </summary>
    float Chance(float dodge, CharacterCold attacker)
    {
        var settings = WeaponDodgeGuardMod.Settings;
        float vanilla = Math.Clamp(DefenceFormulas.BlockChance(dodge, AttackSkill(attacker), constants, 0, 95), 0, 100);
        float scaled = settings.ChanceScale * vanilla;
        return scaled < settings.MinimumChance ? settings.MinimumChance : Math.Min(scaled, settings.MaximumChance);
    }

    /// <summary>
    /// The unarmed dodge's effective defence (the engine's, with the defender's martial arts): the original's <c>getDodge(true)</c>.
    /// </summary>
    static float EffectiveDodge(CharacterCold cold, Fighter fighter, MedicalState medical)
    {
        float martialArts = Stat(cold, StatsEnumerated.MartialArts);
        return DefenceFormulas.EffectiveDefence(martialArts, fighter.EncumbranceFactor, Speed.LegFactor(medical), fighter.DefenceBonus,
            fighter.Guarding);
    }

    /// <summary>
    /// The guard's defence: the original's <c>getMeleeDefence(true)</c>.
    /// </summary>
    static float EffectiveMeleeDefence(CharacterCold cold, Fighter fighter) =>
        DefenceFormulas.Defence(cold.Stats?[StatsEnumerated.MeleeDefence] ?? 0, fighter.Weapon!.Stats.DefenceMod, true, fighter.DefenceBonus,
            fighter.Guarding);

    static float AttackSkill(CharacterCold attacker) => attacker.Fighter is { } f ? Stat(attacker, f.WeaponSkill) : 0;

    static float Stat(CharacterCold c, StatsEnumerated stat) =>
        c.Stats is { } s && c.Medical is { } m ? s[stat] * s.Scale * m.StatMultiplier(stat) : 0;
}

/// <summary>
/// The dodge technique (<b>Verified</b>, original DLL (decompiled)): VanillaOnly the dodge back alone; Compatible a draw weighted on <c>chance</c>
/// over every standing dodge technique (a dodge, not a stumble dodge or prone, not disabled, no taunt or battle cry, not blocked, in the allow list
/// when one is given) whose skill range holds the effective dodge, else the dodge back; CustomAllowList the same over the allowed only. The dodge
/// back too needs its skill range.
/// </summary>
static class DodgeTechniques
{
    static readonly string[] Excluded = ["taunt", "battlecry", "battle cry"];

    public static int? Pick(IReadOnlyList<CombatTechnique> all, float dodge, float roll)
    {
        var settings = WeaponDodgeGuardMod.Settings;
        if (settings.Animations != AnimationMode.VanillaOnly && Weighted(all, dodge, roll) is { } chosen) return chosen;

        return DodgeBack(all, dodge);
    }

    static int? Weighted(IReadOnlyList<CombatTechnique> all, float dodge, float roll)
    {
        float total = 0;
        for (int i = 0; i < all.Count; i++) total += Weight(all[i], dodge);
        if (total <= 0) return null;

        float target = roll * total, sum = 0;
        int last = -1;
        for (int i = 0; i < all.Count; i++)
        {
            float w = Weight(all[i], dodge);
            if (w <= 0) continue;

            sum += w;
            last = i;
            if (target <= sum) return i;
        }

        return last;
    }

    static float Weight(CombatTechnique t, float dodge) => StandingDodge(t) && Listed(t.Name) && InRange(t, dodge) ? t.Chance : 0;

    static int? DodgeBack(IReadOnlyList<CombatTechnique> all, float dodge)
    {
        for (int i = 0; i < all.Count; i++)
            if (StandingDodge(all[i]) && Canonical(all[i].Name) == "dodgeback" && InRange(all[i], dodge)) return i;

        return null;
    }

    static bool StandingDodge(CombatTechnique t) => t is { IsDodge: true, IsStumbleDodge: false, IsProne: false, Disabled: false };

    static bool InRange(CombatTechnique t, float dodge) => t.MinSkill <= dodge && dodge <= t.MaxSkill;

    /// <summary>
    /// Not a taunt or battle cry, not blocked; in the allow list when it has names (CustomAllowList needs one).
    /// </summary>
    static bool Listed(string name)
    {
        var settings = WeaponDodgeGuardMod.Settings;
        string lower = name.ToLowerInvariant();
        if (Excluded.Any(lower.Contains) || Names(settings.Blocked).Contains(Canonical(name))) return false;

        var allowed = Names(settings.Allowed);
        if (allowed.Count == 0) return settings.Animations != AnimationMode.CustomAllowList;

        return allowed.Contains(Canonical(name));
    }

    static HashSet<string> Names(string list) =>
        list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Canonical).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A name compared without case, spaces or punctuation ("Dodge back" is <c>dodgeback</c>).
    /// </summary>
    static string Canonical(string name) => new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
