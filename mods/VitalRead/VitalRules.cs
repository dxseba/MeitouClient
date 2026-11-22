using Meitou.Simulation;

namespace Meitou.VitalRead;

/// <summary>
/// A portrait's state, in the original's order of precedence (Vital-Read DLL, the overlay kind of a member snapshot).
/// </summary>
public enum VitalState
{
    None,
    Repairing,
    InBed,
    PlayingDead,
    Dying,
    RecoveryComa,
    Unconscious,
    CrippledLeg,
    CrippledArm,
    Starving,
}

/// <summary>
/// What the published body status does not carry: from the tick (<see cref="VitalSystem"/>).
/// </summary>
/// <param name="Dying">The original's dying test, its terms folded in the tick (<see cref="VitalSystem"/>).</param>
/// <param name="RecoveryComa">The original's recovery-coma test (<see cref="VitalSystem"/>).</param>
/// <param name="LegCrippled">A crippled leg (stand-in for <c>isCrippled &amp;&amp; !canIkick</c>).</param>
/// <param name="ArmCrippled">Either arm not working (<c>!isLeftArmOk || !isRightArmOk</c>).</param>
/// <param name="Bed">The function of the bed the character lies in (6 a bed, 25 a repair bed, 0 none or another).</param>
public readonly record struct VitalFacts(bool Dying, bool RecoveryComa, bool LegCrippled, bool ArmCrippled, int Bed);

/// <summary>
/// The state rules: the first match of dead (no badge), repair bed, bed, playing dead, dying, recovery coma, unconscious, crippled leg, crippled
/// arm, starving (<b>Verified</b>, original DLL (decompiled)).
/// </summary>
static class VitalRules
{
    /// <summary>
    /// The building function of a repair bed (<c>BF_SKELETON_BED</c>).
    /// </summary>
    public const int RepairBed = 25;

    /// <summary>
    /// The building function of a bed (<c>BF_BED</c>).
    /// </summary>
    public const int Bed = 6;

    public static VitalState Of(BodyStatus body, VitalFacts facts)
    {
        if (body.Dead) return VitalState.None;

        return Beds(facts) ?? Down(body, facts) ?? Hurt(body, facts);
    }

    /// <summary>
    /// A repair bed, then a bed: any character in one, robot or not.
    /// </summary>
    static VitalState? Beds(VitalFacts facts) => facts.Bed switch
    {
        RepairBed => VitalState.Repairing,
        Bed => VitalState.InBed,
        _ => null,
    };

    /// <summary>
    /// Playing dead, dying, recovery coma, unconscious.
    /// </summary>
    static VitalState? Down(BodyStatus body, VitalFacts facts)
    {
        if (body.PlayingDead) return VitalState.PlayingDead;
        if (facts.Dying) return VitalState.Dying;
        if (facts.RecoveryComa) return VitalState.RecoveryComa;
        if (body.Unconscious) return VitalState.Unconscious;

        return null;
    }

    /// <summary>
    /// A crippled leg, a crippled arm, starving (stand-in for <c>isReallyHungry</c>: the health word's hunger below 1). A crippled arm hides
    /// starving.
    /// </summary>
    static VitalState Hurt(BodyStatus body, VitalFacts facts)
    {
        if (facts.LegCrippled) return VitalState.CrippledLeg;
        if (facts.ArmCrippled) return VitalState.CrippledArm;

        return body.Hunger < 1 ? VitalState.Starving : VitalState.None;
    }
}
