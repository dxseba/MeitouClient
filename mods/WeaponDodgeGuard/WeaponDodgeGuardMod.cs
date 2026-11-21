using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.WeaponDodgeGuard;

/// <summary>
/// Port of Weapon Dodge &amp; Guard (Tata-Gari, workshop 3767056805, GPLv3; rebuilt from its description): a fighter holding a weapon that blocks may
/// dodge first, with the unarmed dodge chance (the game's own formula), then guard as usual. A postfix on the engine's defence choice
/// (<c>CombatSystem.ChooseDefence</c>) replaces its pick with a dodge technique of the weapon's category, else the unarmed dodge back.
/// </summary>
public sealed class WeaponDodgeGuardMod : IMod
{
    internal static readonly DodgeSettings Settings = new();

    internal static RngPurpose Purpose { get; private set; }

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        Purpose = context.RngPurpose;
        DodgeHook.Install(context);
        context.Log("Weapon Dodge & Guard initialized");
    }
}

public sealed class DodgeSettings
{
    [Setting("Enable weapon dodge", Hint = "Armed fighters may dodge before they guard.")]
    public bool Enabled { get; set; } = true;

    [Setting("Adaptive priority", Hint = "On: a fighter dodges first only while its dodge chance is at least its guard chance (AdaptivePriority=1).")]
    public bool Adaptive { get; set; }

    [Setting("Dodge back fallback", Hint = "A weapon category with no dodge techniques of its own uses the unarmed dodges.")]
    public bool Fallback { get; set; } = true;
}

static class DodgeHook
{
    static AccessTools.FieldRef<CombatSystem, CombatConstants>? s_constants;
    static PropertyInfo? s_attacker;

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
        CombatSystem __instance, World world, CharacterCold cold, ulong key, int now, bool stumbling, object[] __args, ref int __result)
    #pragma warning restore IDE1006
    {
        if (!WeaponDodgeGuardMod.Settings.Enabled || stumbling) return;
        if (cold.Fighter is not { CanBlock: true } fighter || cold.Medical is not { } medical) return;
        if (s_attacker?.GetValue(__args[5]) is not int attackerSlot || world.Characters.Cold(attackerSlot) is not { } attacker) return;

        float chance = DodgeChance(__instance, cold, fighter, medical, attacker);
        if (WeaponDodgeGuardMod.Settings.Adaptive && chance < GuardChance(__instance, cold, fighter, attacker)) return;

        ulong roll = Rng.Hash(world.Seed, key, WeaponDodgeGuardMod.Purpose, (ulong)(uint)now);
        if (Rng.Float(roll) * 100 >= chance) return;

        if (Dodge(__instance, fighter, medical.Prone, Rng.Float(Rng.Mix(roll))) is { } pick) __result = pick;
    }

    /// <summary>The unarmed dodge chance against the attacker's skill (the engine's, with the defender's martial arts).</summary>
    static float DodgeChance(CombatSystem combat, CharacterCold cold, Fighter fighter, MedicalState medical, CharacterCold attacker)
    {
        float martialArts = Stat(cold, StatsEnumerated.MartialArts);
        float effective = DefenceFormulas.EffectiveDefence(martialArts, fighter.EncumbranceFactor, Speed.LegFactor(medical), fighter.DefenceBonus,
            fighter.Guarding);
        return DefenceFormulas.BlockChance(effective, AttackSkill(attacker), s_constants!(combat), 0, 95);
    }

    static float GuardChance(CombatSystem combat, CharacterCold cold, Fighter fighter, CharacterCold attacker)
    {
        float defence = DefenceFormulas.Defence(cold.Stats?[StatsEnumerated.MeleeDefence] ?? 0, fighter.Weapon!.Stats.DefenceMod, true,
            fighter.DefenceBonus, fighter.Guarding);
        return DefenceFormulas.BlockChance(defence, AttackSkill(attacker), s_constants!(combat));
    }

    static float AttackSkill(CharacterCold attacker) => attacker.Fighter is { } f ? Stat(attacker, f.WeaponSkill) : 0;

    static float Stat(CharacterCold c, StatsEnumerated stat) =>
        c.Stats is { } s && c.Medical is { } m ? s[stat] * s.Scale * m.StatMultiplier(stat) : 0;

    /// <summary>A dodge technique of the weapon's category, else (with the fallback) an unarmed one; null when there is none.</summary>
    static int? Dodge(CombatSystem combat, Fighter fighter, bool prone, float pick)
    {
        int own = TechniqueChooser.ChooseReaction(combat.Techniques, fighter.TechniqueCategory, prone, false, 0, 0, 100, 0, pick);
        if (own >= 0) return own;
        if (!WeaponDodgeGuardMod.Settings.Fallback) return null;

        int unarmed = TechniqueChooser.ChooseReaction(combat.Techniques, (int)WeaponCategory.Unarmed, prone, false, 0, 0, 100, 0, pick);
        return unarmed >= 0 ? unarmed : null;
    }
}
