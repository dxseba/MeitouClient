using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// The perks' gates and values, from the original's codex texts and hooks (README "Facts"). Every method is a pure read of the character's stored
/// stats and the settings copy, safe on any thread.
/// </summary>
static class Perks
{
    /// <summary>The ids of the perks whose stat gates <paramref name="cold"/> meets (weapon-held gates are left to the hit), for the panel.</summary>
    internal static IEnumerable<string> Active(CharacterCold cold, DesoSettings c, int style)
    {
        if (cold.Stats is not { } s) yield break;

        if (Gates.InScope(cold, c.ScopeIndex))
        {
            foreach (var id in Base(s, c)) yield return id;
            foreach (var id in Mastery(s, c)) yield return id;
            foreach (var id in Professions(s, c)) yield return id;
            if (c.Ryu && style >= 0) yield return "216";
        }

        for (var realm = Gates.Realm.Physical; realm <= Gates.Realm.Wilderness; realm++)
        {
            if (Gates.Reached(cold, c, realm)) yield return (101 + (int)realm).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    static IEnumerable<string> Base(CharacterStats s, DesoSettings c)
    {
        float str = Gates.L(s, Strength), tough = Gates.L(s, Toughness), dex = Gates.L(s, Dexterity);
        if (c.P1 && str > 0) yield return "1";
        if (c.P2) yield return "2";
        if (c.P3 && Gates.L(s, MeleeDefence) > c.DefenceMastery) yield return "3";
        if (c.P5 && HyperArmor(s, c)) yield return "5";
        if (c.P6 && str >= c.ShockwaveStrength) yield return "6";
        if (c.P16 && Gates.L(s, MeleeDefence) >= 60) yield return "16";
        if (c.P17 && Gates.L(s, Perception) >= 20) yield return "17";
        if (c.P18 && tough >= 80) yield return "18";
        if (c.P205 && Gates.L(s, Dodge) > 60) yield return "205";
        if (c.P206 && tough > 50) yield return "206";
        if (c.P209 && dex > 75) yield return "209";
        if (c.P210 && Gates.L(s, Athletics) > 60) yield return "210";
        if (c.P211 && Gates.L(s, Swimming) > 60) yield return "211";
        if (c.ToughHp && tough > 60) yield return "220";
        if (c.P6Resist && (tough > 60 || Gates.L(s, Dodge) > 60)) yield return "221";
        if (c.P222 && Wall(s)) yield return "222";
    }

    static IEnumerable<string> Mastery(CharacterStats s, DesoSettings c)
    {
        float t = c.MasteryThreshold;
        if (c.P7 && Gates.L(s, Katanas) > t) yield return "7";
        if (c.P8 && Gates.L(s, Sabres) > t) yield return "8";
        if (c.P9 && Gates.L(s, Hackers) > t) yield return "9";
        if (c.P10 && Gates.L(s, Blunt) > t) yield return "10";
        if (c.P11 && Gates.L(s, HeavyWeapons) > t) yield return "11";
        if (c.P12 && Gates.L(s, Polearms) > t) yield return "12";
        if (c.P13 && Gates.L(s, MartialArts) > t) yield return "13";
        if (c.P15 && Gates.L(s, FriendlyFire) > t) yield return "15";
        if (c.P207 && Gates.L(s, Crossbows) > 40) yield return "207";
    }

    static IEnumerable<string> Professions(CharacterStats s, DesoSettings c)
    {
        float medic = Medical(s);
        if (c.P20 && medic >= 50) yield return "20";
        if (c.P21 && Gates.L(s, Medic) > 60) yield return "21";
        if (c.P22 && Gates.L(s, Farming) >= c.Chef && Gates.L(s, Cooking) >= c.Chef) yield return "22";
        if (c.P23 && Gates.L(s, Robotics) >= 40) yield return "23";
        if (c.P27 && Porter(s, c)) yield return "27";
        if (c.P202 && Nutritionist(s)) yield return "202";
        if (c.P204 && medic >= 10) yield return "204";
        if (c.P213 && Gates.L(s, SmithingWeapon) >= 60) yield return "213";
        if (c.P214 && Gates.L(s, SmithingArmour) >= 60) yield return "214";
    }

    /// <summary>The higher of medic and hive medic (one storage in Meitou).</summary>
    internal static float Medical(CharacterStats s) => Gates.L(s, Medic);

    /// <summary>Hyper Armor's gate: strength at the requirement and (heavy weapons or blunt 20, or strength 100).</summary>
    internal static bool HyperArmor(CharacterStats s, DesoSettings c)
    {
        float str = Gates.L(s, Strength);
        return str >= c.HyperArmorStrength && (Gates.L(s, HeavyWeapons) >= 20 || Gates.L(s, Blunt) >= 20 || str >= 100);
    }

    /// <summary>Wind-Eroded Wall's gate: strength and toughness 100, melee defence above 80.</summary>
    internal static bool Wall(CharacterStats s) =>
        Gates.L(s, Strength) >= 100 && Gates.L(s, Toughness) >= 100 && Gates.L(s, MeleeDefence) > 80;

    internal static bool Porter(CharacterStats s, DesoSettings c) => Gates.L(s, Labouring) >= c.Porter && Gates.L(s, Engineering) >= c.Porter;

    /// <summary>Field Nutrition's nutritionist: science and cooking 75, medic above 60.</summary>
    internal static bool Nutritionist(CharacterStats s) => Gates.L(s, Science) >= 75 && Gates.L(s, Cooking) >= 75 && Gates.L(s, Medic) > 60;

    /// <summary>Athletics Momentum: 3.75 % per 10 levels above 60 to 100, then 5 % per 10.</summary>
    internal static float Momentum(float level)
    {
        if (!(level > 60)) return 1;

        return 1 + 0.00375f * (Math.Min(level, 100) - 60) + 0.005f * Math.Max(0, level - 100);
    }

    /// <summary>Toughness Reduction: 12 % of the injury penalties per 10 toughness above 50, at most 85 %.</summary>
    internal static float InjuryReduction(float toughness) => toughness > 50 ? Math.Min(0.85f, 0.012f * (toughness - 50)) : 0;

    /// <summary>Iron Guard's flat reduction on a limb (from melee defence 60) and on a vital part (from 80).</summary>
    internal static (float Limb, float Vital) Guard(float defence)
    {
        float limb = defence >= 60 ? 2 + 0.1f * (Math.Min(defence, 100) - 60) + 0.16f * Math.Max(0, defence - 100) : 0;
        float vital = defence >= 80 ? 6 + 0.1f * (Math.Min(defence, 100) - 80) + 0.16f * Math.Max(0, defence - 100) : 0;
        return (limb, vital);
    }

    /// <summary>Crossbow Mastery's damage factor: 1 at 40, +1 per 30 levels to 100, then +0.8 per 10.</summary>
    internal static float Crossbow(float level) =>
        level > 40 ? 1 + (Math.Min(level, 100) - 40) / 30 + 0.08f * Math.Max(0, level - 100) : 1;

    /// <summary>Shockwave's chest damage: 30, +8 per 10 strength above the requirement, at most 75.</summary>
    internal static float Shockwave(float strength, float requirement) => Math.Min(75, 30 + 0.8f * (strength - requirement));

    /// <summary>Shockwave Resist: 2 % per point of the first 20 above 60 in toughness and dodge, at most 80 %; either above 95 is immune.</summary>
    internal static float ShockResist(CharacterStats s)
    {
        float tough = Gates.L(s, Toughness), dodge = Gates.L(s, Dodge);
        if (tough > 95 || dodge > 95) return 1;

        return Math.Min(0.8f, 0.02f * (Math.Clamp(tough - 60, 0, 20) + Math.Clamp(dodge - 60, 0, 20)));
    }
}
