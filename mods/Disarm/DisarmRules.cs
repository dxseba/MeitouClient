using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using Meitou.Simulation.Player;

namespace Meitou.Disarm;

/// <summary>
/// The original's success chances and target rules (dafitime/disarm <c>CalculateSuccessChance</c>, <c>GetKODropChance</c>), read from the world.
/// </summary>
static class DisarmRules
{
    /// <summary>Seconds a looker's "detected" sighting of a sneaker counts.</summary>
    const float SightingSeconds = 2;

    /// <summary>The disarm skill: Thievery weighted 0.65, Stealth 0.35.</summary>
    public static float Skill(CharacterCold actor) =>
        actor.Stats is { } s ? s[StatsEnumerated.Thieving] * 0.65f + s[StatsEnumerated.Stealth] * 0.35f : 0;

    /// <summary>
    /// The chance (0..100) that <paramref name="actor"/> disarms <paramref name="target"/>: 100 on a downed target, a skill roll on a sleeping
    /// one, a skill roll against Perception (0 once the actor is detected) on an awake one while the actor sneaks; null when it cannot try.
    /// </summary>
    public static int? Chance(World world, int actor, int target, DisarmSettings settings)
    {
        var table = world.Characters;
        if (table.Cold(actor) is not { } a || table.Cold(target) is not { Medical.Dead: false } t || !Targetable(t, settings)) return null;
        if (t.Medical!.Unconscious) return 100;
        if (CarrySystem.Asleep(table, target)) return Scaled(10 + (int)(Skill(a) * 0.9f), 100, settings.ChanceMultiplier);
        if ((a.Orders & CharacterOrders.Sneak) == 0) return null;
        if (Detected(world, a)) return 0;

        float perception = t.Stats?[StatsEnumerated.Perception] ?? 0;
        return Scaled(5 + (int)(Skill(a) * 0.85f) - (int)(perception * 0.3f), 95, settings.ChanceMultiplier);
    }

    /// <summary>The knockout drop chance: 55 - 0.4 Strength - 0.2 Athletics, times the multiplier, within 5..90.</summary>
    public static int KnockoutDropChance(CharacterCold victim, float multiplier)
    {
        if (victim.Stats is not { } s) return 0;

        int chance = 55 - (int)(s[StatsEnumerated.Strength] * 0.4f) - (int)(s[StatsEnumerated.Athletics] * 0.2f);
        return Math.Clamp((int)(chance * multiplier), 5, 90);
    }

    /// <summary>Not an animal, and not the player's unless "Allow on own squad" is on.</summary>
    public static bool Targetable(CharacterCold target, DisarmSettings settings) => target.Animal is null && (!target.IsPlayer || settings.OwnSquad);

    /// <summary>The item of the weapon the character fights with (its hip or back slot), or null when unarmed.</summary>
    public static ItemInstance? Wielded(CharacterCold c)
    {
        if (c.Fighter is not { Weapon: not null } fighter || c.Inventory is not { } inventory) return null;

        string section = fighter.WeaponIsBack ? "back" : "hip";
        return inventory.Items.Find(i => i.Section == section && !i.LockedOn);
    }

    static bool Detected(World world, CharacterCold actor)
    {
        long since = world.Tick - (long)MathF.Ceiling(SightingSeconds / world.TickSeconds);
        return actor.Watchers?.Exists(w => w.Level == StealthExposure.Detected && w.Tick >= since) == true;
    }

    /// <summary>The chance times the multiplier, at most <paramref name="max"/>, at least a floor of 5 that shrinks with a multiplier below 1.</summary>
    static int Scaled(int chance, int max, float multiplier)
    {
        int scaled = Math.Min((int)(chance * multiplier), max);
        int floor = Math.Clamp((int)(5 * multiplier), 0, 5);
        return Math.Max(scaled, floor);
    }
}
