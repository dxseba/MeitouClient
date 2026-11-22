using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Player;

namespace Meitou.WeaponDodgeGuard;

/// <summary>
/// The dodge before the guard (the original's <c>chooseBlock</c> hook, <b>Verified</b> original DLL (decompiled)): only an armed fighter whose
/// vanilla pick is a standing block, standing normally (with Require standing), not in defensive mode (unless allowed), past its cooldown and (with
/// Adaptive priority) not better at guarding rolls the unarmed dodge chance, scaled by Chance scale and clamped to the minimum and maximum; on
/// success its block becomes a dodge technique (<see cref="DodgeTechniques"/>), else the guard stands.
/// </summary>
static class DodgeHook
{
    static AccessTools.FieldRef<CombatSystem, CombatConstants>? s_constants;
    static PropertyInfo? s_attacker;

    /// <summary>
    /// Each world's last weapon-dodge tick per defender (for the cooldown); written only from the defender's own defence choice.
    /// </summary>
    static readonly ConditionalWeakTable<World, ConcurrentDictionary<CharacterId, long>> LastDodge = [];

    public static void Install(ModContext context)
    {
        var choose = AccessTools.Method(typeof(CombatSystem), "ChooseDefence")
            ?? throw new MissingMethodException(nameof(CombatSystem), "ChooseDefence");
        s_constants = AccessTools.FieldRefAccess<CombatSystem, CombatConstants>("_combat");
        var blow = AccessTools.Inner(typeof(CombatSystem), "IncomingBlow") ?? throw new TypeLoadException("CombatSystem.IncomingBlow");
        s_attacker = AccessTools.Property(blow, "Attacker") ?? throw new MissingMemberException(blow.FullName, "Attacker");
        context.Harmony.Patch(choose, postfix: new HarmonyMethod(typeof(DodgeHook), nameof(AfterChoose)));
    }

    #pragma warning disable IDE1006 // Harmony binds __instance / __args / __result by name
    static void AfterChoose(
        CombatSystem __instance, World world, int i, CharacterCold cold, ulong key, int now, bool stumbling, object[] __args, ref int __result)
    #pragma warning restore IDE1006
    {
        if (!WeaponDodgeGuardMod.Settings.Enabled || stumbling || !StandingBlock(__instance, __result)) return;
        if (cold.Fighter is not { CanBlock: true } fighter || cold.Medical is not { } medical || !Eligible(cold, medical)) return;
        if (s_attacker?.GetValue(__args[5]) is not int attackerSlot || world.Characters.Cold(attackerSlot) is not { } attacker) return;

        var id = world.Characters.IdOf(i);
        if (CoolingDown(world, id)) return;

        float dodge = EffectiveDodge(cold, fighter, medical);
        if (WeaponDodgeGuardMod.Settings.Adaptive && dodge < EffectiveMeleeDefence(cold, fighter)) return;

        ulong roll = Rng.Hash(world.Seed, key, WeaponDodgeGuardMod.Purpose, (ulong)(uint)now);
        if (Rng.Float(roll) * 100 >= Chance(__instance, dodge, attacker)) return;

        if (DodgeTechniques.Pick(__instance.Techniques, dodge, Rng.Float(Rng.Mix(roll))) is not { } pick) return;

        __result = pick;
        if (WeaponDodgeGuardMod.Settings.CooldownMs > 0) Dodges(world)[id] = world.Tick;
    }

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

    static ConcurrentDictionary<CharacterId, long> Dodges(World world) => LastDodge.GetValue(world, _ => new());

    /// <summary>
    /// <c>DodgeCooldownMs</c> after the defender's last weapon dodge (game time here: the original's clock is the wall's).
    /// </summary>
    static bool CoolingDown(World world, CharacterId id)
    {
        int ms = WeaponDodgeGuardMod.Settings.CooldownMs;
        if (ms <= 0 || !Dodges(world).TryGetValue(id, out long last)) return false;

        return (world.Tick - last) * world.TickSeconds * 1000 < ms;
    }

    /// <summary>
    /// The scaled chance: the vanilla dodge chance against the attacker's skill, held to [0, 100], times Chance scale, held to the minimum and
    /// maximum.
    /// </summary>
    static float Chance(CombatSystem combat, float dodge, CharacterCold attacker)
    {
        var settings = WeaponDodgeGuardMod.Settings;
        float vanilla = Math.Clamp(DefenceFormulas.BlockChance(dodge, AttackSkill(attacker), s_constants!(combat), 0, 95), 0, 100);
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
