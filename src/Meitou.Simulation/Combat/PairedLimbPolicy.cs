namespace Meitou.Simulation.Combat;

/// <summary>A rare high-skill follow-through of a real limb-severing sweep, independent of other combat rolls.</summary>
public static class PairedLimbPolicy
{
    public const float UnlockLevel = 80, MinimumCut = 100;

    public static float Chance(float brutality)
    {
        if (!float.IsFinite(brutality) || brutality < UnlockLevel) return 0;
        float t = Math.Clamp((brutality - UnlockLevel) / 20, 0, 1);
        return .01f + .04f * t * t * (3 - 2 * t);
    }

    public static bool ShouldFollowThrough(float brutality, CombatWoundMechanism mechanism, int direction, float cut, float maximum, float roll) =>
        mechanism == CombatWoundMechanism.Slash && direction is 2 or 3 or 9
        && float.IsFinite(cut) && float.IsFinite(maximum) && maximum > 0 && cut >= MathF.Max(MinimumCut, maximum)
        && float.IsFinite(roll) && roll >= 0 && roll < Chance(brutality);
}
