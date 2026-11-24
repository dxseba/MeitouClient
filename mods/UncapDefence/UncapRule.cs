using System.Globalization;

namespace Meitou.UncapDefence;

/// <summary>
/// The original's chance rule (<b>Verified</b>, <c>UncapDefenceSuccessRate.dll</c> decompiled, its block and dodge hooks' shared step): with gap =
/// own defence (melee defence for a block, dodge for a dodge) - the attacker's attack, a pressure p = 1 - e^(-|gap| / scale) (Amplify) or min(|gap|
/// / full gap, 1) (Threshold); a positive gap lifts the vanilla chance c towards the ceiling, c + (ceiling - c) p, a negative one lowers it, c (1 -
/// p); then held to at most the ceiling, at least the floor, and to [0, 1]. Mode 0 keeps the vanilla chance. Chances are fractions here.
/// </summary>
readonly record struct UncapRule(int Mode, float Scale, float FullGap, float Ceiling, float Floor)
{
    public static UncapRule Of(UncapSettings s) => new(Array.IndexOf(UncapSettings.Modes, s.Mode), Number(s.Scale), Number(s.FullGap),
        Number(s.Ceiling) / 100, Math.Clamp(s.Floor, 0, 0.5f));

    public bool On => Mode is 1 or 2;

    /// <summary>The chance for vanilla chance <paramref name="vanilla"/> (a fraction) at the level gap <paramref name="gap"/>.</summary>
    public float Chance(float vanilla, float gap)
    {
        if (!On) return vanilla;

        float pressure = Pressure(MathF.Abs(gap));
        float chance = MathF.Max(vanilla, 0);
        if (gap > 0) chance += (Ceiling - chance) * pressure;
        else if (gap < 0) chance *= 1 - pressure;

        return Math.Clamp(MathF.Max(MathF.Min(chance, Ceiling), Floor), 0, 1);
    }

    float Pressure(float gap) => Mode == 1 ? 1 - MathF.Exp(-gap / Scale) : MathF.Min(gap / FullGap, 1);

    static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}
