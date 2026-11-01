using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    static bool CanDepositWeaponBlood(ItemInstance? weapon, RaceData race, bool aliveBeforeHit) =>
        weapon is { Type: FcsRecordType.WEAPON } && aliveBeforeHit && !race.IsRobot
        && float.IsFinite(race.BleedRate) && race.BleedRate > 0;

    /// <summary>Blood from an actual open wound, on the particular weapon item that delivered it. Display state only; no damage or random draws.</summary>
    internal static bool DepositWeaponBlood(ItemInstance? weapon, RaceData race, in HitDamage damage, bool aliveBeforeHit, bool organicPartBeforeHit,
        CombatWoundMechanism mechanism = CombatWoundMechanism.Unspecified)
    {
        if (!CanDepositWeaponBlood(weapon, race, aliveBeforeHit) || !organicPartBeforeHit
            || !float.IsFinite(damage.Cut) || !float.IsFinite(damage.Pierce) || damage.Cut < 0 || damage.Pierce < 0
            || !float.IsFinite(damage.Blunt) || damage.Blunt < 0 || !float.IsFinite(damage.Scale) || damage.Scale <= 0) return false;
        // Crush wounds can open skin too; other mechanisms do not turn their incidental blunt stun into blood.
        double opening = (double)damage.Cut + damage.Pierce + (mechanism == CombatWoundMechanism.Crush ? 0.35 * damage.Blunt : 0);
        // One hundred scaled opening points fill the coat; small wounds add proportionately and large wounds saturate safely.
        float deposit = (float)Math.Clamp(opening * damage.Scale * race.BleedRate / 100, 0, 1);
        if (deposit <= 0) return false;
        var colour = new Vector3((race.BloodColour >> 16 & 255) / 255f, (race.BloodColour >> 8 & 255) / 255f, (race.BloodColour & 255) / 255f);
        ItemInstance item = weapon!;
        var before = item.WeaponBlood;
        float amount = float.IsFinite(before.W) ? Math.Clamp(before.W, 0, 1) : 0;
        var previous = float.IsFinite(before.X) && float.IsFinite(before.Y) && float.IsFinite(before.Z)
            ? Vector3.Clamp(new(before.X, before.Y, before.Z), Vector3.Zero, Vector3.One) : colour;
        float total = amount + deposit;
        var next = new Vector4((previous * amount + colour * deposit) / total, MathF.Min(total, 1));
        item.WeaponBlood = next;
        return next != before;
    }
}
