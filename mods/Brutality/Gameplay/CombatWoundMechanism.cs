using Meitou.Data.Characters;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using Meitou.Data.Gameplay.Combat;

namespace Meitou.Brutality;

/// <summary>A visual interpretation of a blow, independent of the damage that remains after armour.</summary>
public enum CombatWoundMechanism { Unspecified = 0, Slash = 1, Thrust = 2, Crush = 3 }

public static class CombatWoundMechanisms
{
    /// <summary>Reads the attacker's actual weapon category and the pre-armour packet; it changes no combat state.</summary>
    public static CombatWoundMechanism Classify(Fighter fighter, in DamagePacket packet, int cutDirection = -1)
    {
        // Native fists and clubs can deliver cut damage, and a club's jab is still a blunt impact.
        if ((!fighter.IsAnimal && fighter.Unarmed) || fighter.Weapon?.Data.SkillCategory == (int)WeaponCategory.Blunt)
            return CombatWoundMechanism.Crush;
        if (cutDirection == 4 && (packet.Cut > 0 || packet.Pierce > 0)) return CombatWoundMechanism.Thrust;
        if (packet.Cut > 0) return CombatWoundMechanism.Slash;
        if (packet.Pierce > 0) return CombatWoundMechanism.Thrust;
        if (packet.Blunt > 0) return CombatWoundMechanism.Crush;
        return CombatWoundMechanism.Unspecified;
    }
}
