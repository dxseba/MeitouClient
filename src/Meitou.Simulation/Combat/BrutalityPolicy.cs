using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

/// <summary>Meitou's separate brutality progression: visual unlocks and the chance of a qualifying lethal chest cleave.</summary>
public static class BrutalityPolicy
{
    public const float SuperficialHeadLevel = 20, MajorHeadLevel = 40, BisectionLevel = 60;

    public static int HeadTier(float level) => !float.IsFinite(level) || level < SuperficialHeadLevel ? 0 : level < MajorHeadLevel ? 1 : 2;

    public static bool CanDecapitate(float level, CombatWoundMechanism mechanism) =>
        float.IsFinite(level) && level >= MajorHeadLevel && mechanism == CombatWoundMechanism.Slash;

    /// <summary>Exact user anchors, joined by monotone smoothstep curves; the unlock at 60 is intentionally a hard gate.</summary>
    public static float BisectionChance(float level)
    {
        if (!float.IsFinite(level) || level < BisectionLevel) return 0;
        ReadOnlySpan<float> chances = [.10f, .30f, .55f, .80f, .95f];
        if (level >= 100) return chances[^1];
        float position = (level - BisectionLevel) / 10;
        int band = Math.Min((int)position, chances.Length - 2);
        float t = position - band;
        t = t * t * (3 - 2 * t);
        return chances[band] + (chances[band + 1] - chances[band]) * t;
    }

    /// <summary>Actual delivered channels, bounded by the part's pre-hit remaining health range; extra stun and overkill never train the skill.</summary>
    public static float UsefulDamage(in HitDamage damage, float remaining)
    {
        float total = damage.Cut + damage.Blunt + damage.Pierce;
        return float.IsFinite(total) && float.IsFinite(remaining) ? Math.Clamp(total, 0, MathF.Max(remaining, 0)) : 0;
    }
}
