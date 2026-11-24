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
        if (RecoveryComa(med, pastNoReturn)) return VitalState.RecoveryComa;

        return pastNoReturn || med.Coma ? VitalState.Dying : VitalState.Unconscious;
    }

    /// <summary>
    /// The coma flag, <c>!canGetUpWakeUp</c> (<see cref="MedicalState.CanGetUpWakeUp"/>), no KO timer, not <c>isProbablyDying</c>
    /// (<see cref="MedicalState.ProbablyDying"/>), blood above the KO point and no blood-loss trauma.
    /// </summary>
    static bool RecoveryComa(MedicalState med, bool pastNoReturn)
    {
        if (!med.Coma || med.CanGetUpWakeUp || med.KoTimer > 0) return false;

        return !pastNoReturn && !med.BloodKo && !med.ProbablyDying;
    }
}
