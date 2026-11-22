using Meitou.Data.Gameplay.Bodies;

namespace Meitou.LevelUpNotify;

/// <summary>
/// The skills the original notifies about, with its names (its skill table: every stat 1-38 but hive medic, vet, weapons, mass combat and
/// survival, which it never checks).
/// </summary>
static class Skills
{
    public static readonly (StatsEnumerated Stat, string Name)[] Watched =
    [
        (S(1), "Strength"), (S(2), "Melee Attack"), (S(3), "Labouring"), (S(4), "Science"), (S(5), "Engineering"), (S(6), "Robotics"),
        (S(7), "Weapon Smith"), (S(8), "Armour Smith"), (S(9), "Field Medic"), (S(10), "Thievery"), (S(11), "Turrets"), (S(12), "Farming"),
        (S(13), "Cooking"), (S(16), "Stealth"), (S(17), "Athletics"), (S(18), "Dexterity"), (S(19), "Melee Defence"), (S(21), "Toughness"),
        (S(22), "Assassination"), (S(23), "Swimming"), (S(24), "Perception"), (S(25), "Katanas"), (S(26), "Sabres"), (S(27), "Hackers"),
        (S(28), "Heavy Weapons"), (S(29), "Blunt"), (S(30), "Martial Arts"), (S(32), "Dodge"), (S(34), "Polearms"), (S(35), "Crossbows"),
        (S(36), "Friendly Fire"), (S(37), "Lockpicking"), (S(38), "Crossbow Smith"),
    ];

    static StatsEnumerated S(int number) => (StatsEnumerated)number;
}
