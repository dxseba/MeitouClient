namespace Meitou.DesolationSystem;

/// <summary>
/// The original's two growth curves (<b>Verified</b>, <c>DesolationSystem.dll</c> decompiled: the mastery factor and the Ryu factor).
/// Mastery: 0 up to level 60, ((L - 60) / 40)^2 up to 100 (so 1/4 at 80, 1 at 100: "doubling levels above 60 quadruples extra gains"), then
/// 1 + (L - 100) / 25. A perk's value is its level-60 value plus (its level-100 value - its level-60 value) times the factor. Ryu: linear,
/// (L - 60) / 40 up to 100, then 1 + (L - 100) / 60.
/// </summary>
static class Mastery
{
    /// <summary>The mastery factor of a level.</summary>
    internal static float Factor(float level)
    {
        if (!(level > 60)) return 0;
        if (level <= 100)
        {
            float t = (level - 60) / 40;
            return t * t;
        }

        return 1 + (level - 100) / 25;
    }

    /// <summary>The original's global effect scale (1 by default), set from the settings in the serial input step.</summary>
    internal static volatile float Scale = 1;

    /// <summary>A mastery perk's value at <paramref name="level"/>: <paramref name="at60"/> grown towards <paramref name="at100"/>, times the scale.</summary>
    internal static float Value(float level, float at60, float at100) => (at60 + (at100 - at60) * Factor(level)) * Scale;

    /// <summary>The Ryu styles' factor of a melee attack level.</summary>
    internal static float RyuFactor(float level)
    {
        if (!(level > 60)) return 0;

        return level <= 100 ? (level - 60) / 40 : 1 + (level - 100) / 60;
    }
}
