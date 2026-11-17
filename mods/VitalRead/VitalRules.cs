using Meitou.Simulation;

namespace Meitou.VitalRead;

/// <summary>A portrait's state, the original's first match plus the fork's two bed states.</summary>
public enum VitalState
{
    None,
    PlayingDead,
    Dying,
    RecoveryComa,
    Unconscious,
    CrippledLeg,
    CrippledArm,
    Starving,
    Repairing,
    Resting,
}

/// <summary>What the published body status does not carry: from the tick (<see cref="VitalSystem"/>).</summary>
/// <param name="Wounds">The health word's wound severity w (docs/game/ui-screens.md "Health word choice"): 0 none, 1 a non-vital part destroyed,
/// 2 a part below 0, 3 a vital part below 0 and under the KO point.</param>
public readonly record struct VitalFacts(int Wounds, bool LegCrippled, bool ArmCrippled, bool Robot, bool RepairBed);

/// <summary>The state rules (Vital-Read <c>TryResolvePortraitTextLabelState</c>), with dying taken from the game's own health word.</summary>
static class VitalRules
{
    public static VitalState Of(BodyStatus body, bool inBed, VitalFacts facts) =>
        Down(body, facts) ?? Hurt(body, facts) ?? Bed(inBed, facts);

    /// <summary>Dead (no badge), playing dead, dying, recovery coma, unconscious.</summary>
    static VitalState? Down(BodyStatus body, VitalFacts facts)
    {
        if (body.Dead) return VitalState.None;
        if (body.PlayingDead) return VitalState.PlayingDead;
        if (body.Blood <= 0 && facts.Wounds == 1 || facts.Wounds == 3) return VitalState.Dying;
        if (body.Unconscious && body.Coma && body.KoTimer <= 0) return VitalState.RecoveryComa;
        if (body.Unconscious) return VitalState.Unconscious;

        return null;
    }

    /// <summary>A crippled leg, a crippled arm, starving (the health word's hunger below 1).</summary>
    static VitalState? Hurt(BodyStatus body, VitalFacts facts)
    {
        if (facts.LegCrippled) return VitalState.CrippledLeg;
        if (facts.ArmCrippled) return VitalState.CrippledArm;
        if (body.Hunger < 1) return VitalState.Starving;

        return null;
    }

    /// <summary>The fork's extras: a robot in a repair bed, anyone else in a bed.</summary>
    static VitalState Bed(bool inBed, VitalFacts facts)
    {
        if (!inBed) return VitalState.None;

        return facts.Robot && facts.RepairBed ? VitalState.Repairing : VitalState.Resting;
    }
}
