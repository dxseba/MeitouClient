namespace Meitou.Nitoryu;

/// <summary>
/// The five ranks (README "The ranks"): Deshi, Bushi, Ronin, Kenshi, Kensei at Path <c>nitoryu tier dp</c> (20, 40, 60, 75, 90), ranks 3 to 5
/// also at Dual Wielding <c>nitoryu tier min ambidexterity</c> (40, 60, 80) as it counts in the fight; 0 below the first.
/// </summary>
static class Ranks
{
    static readonly string[] Names = ["", "Deshi", "Bushi", "Ronin", "Kenshi", "Kensei"];

    public static int Tier(NitoryuConfig config, float path, float ambidexterity)
    {
        int tier = 0;
        for (int t = 1; t <= 5 && t <= config.TierPath.Length; t++)
        {
            float needs = t <= config.TierAmbidexterity.Length ? config.TierAmbidexterity[t - 1] : 0;
            if (path < config.TierPath[t - 1] || ambidexterity < needs) break;

            tier = t;
        }

        return tier;
    }

    /// <summary>The split guard's block penalty at a rank (<c>nitoryu divided guard</c>).</summary>
    public static float Divided(NitoryuConfig config, int tier) =>
        (uint)tier < (uint)config.DividedGuard.Length ? config.DividedGuard[tier] : 0;

    public static string Name(int tier) => (uint)tier < (uint)Names.Length ? Names[tier] : "";

    /// <summary>The stance track of a rank (<c>nitoryu stance deshi</c> ...); null below rank 1.</summary>
    public static string? StanceTrack(int tier) => tier is >= 1 and <= 5 ? NitoryuData.ClipPrefix + "stance " + Names[tier].ToLowerInvariant() : null;
}
