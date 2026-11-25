using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// Who a perk applies to: the scope (the original's <c>TargetScope</c> 0 global, 1 player only, 2 NPC only, for the base perks) and the five Supreme
/// Realms' stat gates (player characters only, <c>EnableAscension</c>). Stats are read as stored (training level, without other modifiers), so a
/// perk's own stat bonus never qualifies another.
/// </summary>
static class Gates
{
    /// <summary>The realms in the original's order (ids 101 to 105; 106 Heavenly Artifice is not ported).</summary>
    internal enum Realm
    {
        Physical,
        Universal,
        Shadow,
        Cyber,
        Wilderness,
    }

    static readonly StatsEnumerated[] PhysicalStats = [Strength, Toughness, Dexterity, Athletics, Swimming, Labouring];
    static readonly StatsEnumerated[] UniversalStats = [MeleeAttack, MeleeDefence, MartialArts, Dodge];
    static readonly StatsEnumerated[] WeaponStats = [Katanas, Sabres, Hackers, HeavyWeapons, Blunt, Polearms, Crossbows, Turrets];
    static readonly StatsEnumerated[] ShadowStats = [Stealth, Assassination, Thieving, Lockpicking, Perception];
    static readonly StatsEnumerated[] CyberStats = [Science, Robotics, SmithingWeapon, SmithingArmour, SmithingBow];
    static readonly StatsEnumerated[] WildStats = [Medic, Perception, Cooking, Farming, Science, Strength];

    const float RealmLevel = 90;

    /// <summary>A stat's stored level.</summary>
    internal static float L(CharacterStats stats, StatsEnumerated stat) => stats.Stored(stat);

    /// <summary>Whether the base perks apply to <paramref name="cold"/> under scope <paramref name="scope"/>.</summary>
    internal static bool InScope(CharacterCold cold, int scope) => scope switch
    {
        1 => cold.IsPlayer,
        2 => !cold.IsPlayer,
        _ => true,
    };

    /// <summary>An animal (its own stats object, no human gear).</summary>
    internal static bool IsAnimal(CharacterCold cold) => SquadRegistry.IsAnimal(cold);

    /// <summary>Whether <paramref name="cold"/> has reached <paramref name="realm"/> (a player character, realms on).</summary>
    internal static bool Reached(CharacterCold cold, DesoSettings cfg, Realm realm)
    {
        if (!cfg.Ascension || !cold.IsPlayer || cold.Stats is not { } s) return false;

        return realm switch
        {
            Realm.Physical => All(s, PhysicalStats),
            Realm.Universal => All(s, UniversalStats) && WeaponStats.Count(w => L(s, w) >= RealmLevel) >= 3,
            Realm.Shadow => All(s, ShadowStats),
            Realm.Cyber => All(s, CyberStats),
            _ => All(s, WildStats),
        };
    }

    static bool All(CharacterStats s, StatsEnumerated[] stats) => stats.All(x => L(s, x) >= RealmLevel);
}
