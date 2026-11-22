using Meitou.Simulation.Bodies;

namespace Meitou.VitalSense;

/// <summary>
/// The marker states, as the original's <c>STATE_*</c>; <see cref="None"/> is a standing character (a bounty-only marker).
/// </summary>
public enum VitalState : byte { None, Unconscious, RecoveryComa, Dying, PlayingDead, Dead }

/// <summary>
/// A character's published state and total bounty, and whether it is medically dead (its marker then takes the enemy colour unless it is the
/// player's).
/// </summary>
public readonly record struct Vital(VitalState State, int Bounty, bool MedicallyDead = false);

/// <summary>
/// The original's state rule (<b>Verified</b>, original DLL (decompiled), the KO-target state): dead first; then only an unconscious character
/// (playing dead included) gets one: playing dead PD; recovery coma RC (the coma flag, no way to get up now, no KO timer left, not probably dying,
/// blood above the KO point, no blood-loss trauma); dying DY (blood at or under the KO point, or the coma flag); else ZZ.
/// </summary>
static class VitalRule
{
    public static VitalState Of(MedicalState med, float? koPoint)
    {
        if (med.Dead) return VitalState.Dead;
        if (!med.Unconscious && !med.PlayingDead) return VitalState.None;
        if (med.PlayingDead) return VitalState.PlayingDead;

        bool pastNoReturn = koPoint is { } ko && med.Blood <= ko;
        if (RecoveryComa(med, koPoint, pastNoReturn)) return VitalState.RecoveryComa;

        return pastNoReturn || med.Coma ? VitalState.Dying : VitalState.Unconscious;
    }

    /// <summary>
    /// <c>canGetUpWakeUp</c> is taken as <see cref="MedicalState.WakesWithin"/> now; <c>isProbablyDying</c>, whose rule is not known, as the health
    /// word's Dying (<see cref="HealthWordDying"/>).
    /// </summary>
    static bool RecoveryComa(MedicalState med, float? koPoint, bool pastNoReturn)
    {
        if (!med.Coma || med.WakesWithin(0) || med.KoTimer > 0) return false;

        return !pastNoReturn && !med.BloodKo && !HealthWordDying(med, koPoint);
    }

    /// <summary>
    /// The health word's Dying (docs/game/ui-screens.md "Health word choice"): a vital part below 0 and not at or above the KO point, or blood at
    /// or under 0 with a non-vital part destroyed past its maximum.
    /// </summary>
    static bool HealthWordDying(MedicalState med, float? koPoint)
    {
        int worst = 0;
        foreach (var part in med.Parts)
        {
            float health = part.Flesh + part.Bandage;
            if (health - part.Stun <= -part.MaxHp)
            {
                if (!part.Template.Vital && worst == 0) worst = 1;
            }
            else if (health < 0)
            {
                bool aboveKo = koPoint is { } ko && health >= ko;
                worst = Math.Max(worst, part.Template.Vital && !aboveKo ? 3 : 2);
            }
        }

        return worst == 3 || worst == 1 && med.Blood <= 0;
    }
}
