using Meitou.Simulation.Bodies;

namespace Meitou.VitalSense;

/// <summary>The marker states, as the original's <c>STATE_*</c>; <see cref="None"/> is a standing character (a bounty-only marker).</summary>
public enum VitalState : byte { None, Unconscious, RecoveryComa, Dying, PlayingDead, Dead }

/// <summary>A character's published state and total bounty.</summary>
public readonly record struct Vital(VitalState State, int Bounty);

/// <summary>
/// The state of a body, from Meitou's medical header word (docs/game/ui-screens.md "Health word choice"): its first match decides, and only a
/// character that is down (unconscious or playing dead) or dead gets one.
/// </summary>
static class VitalRule
{
    public static VitalState Of(MedicalState med, float? koPoint)
    {
        if (med.Dead) return VitalState.Dead;
        if (!med.Unconscious && !med.PlayingDead) return VitalState.None;

        int w = WoundSeverity(med.Parts, koPoint);
        if (med.EatenCountdown > 0 || w == 3 || med.Blood <= 0 && w == 1) return VitalState.Dying;
        if (med.KoTimer > 0) return VitalState.Unconscious;
        if (med.Unconscious && med.Coma && w == 0) return VitalState.RecoveryComa;
        if (med.PlayingDead) return VitalState.PlayingDead;

        // "Bleeding out" (out from blood loss, still bleeding) dies without help; "Blood loss KO" recovers.
        return med.Blood <= 0 && med.Bleeding > 0 ? VitalState.Dying : VitalState.Unconscious;
    }

    /// <summary>
    /// The header's wound severity: 3 a vital part below 0 (above the knockout point it is only 2, critical), 2 a non-vital part below 0, 1 a
    /// non-vital part destroyed past its maximum, 0 none.
    /// </summary>
    static int WoundSeverity(IReadOnlyList<HealthPart> parts, float? koPoint)
    {
        int worst = 0;

        foreach (var part in parts)
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

        return worst;
    }
}
