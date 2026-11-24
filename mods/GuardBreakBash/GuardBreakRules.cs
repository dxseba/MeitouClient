namespace Meitou.GuardBreakBash;

/// <summary>
/// An effect's chance curve (<b>Verified</b>, <c>GuardBreakBash.dll</c> decompiled): by the score difference d (source - target), the base chance
/// at 0 runs linearly to the mid chance at the mid difference and from there to the minimum (d below 0) or maximum (d above 0) at the cap
/// difference, flat past it; then held to [minimum, maximum]. Percent.
/// </summary>
readonly record struct Curve(float Base, float Min, float Max, float NegativeMid, float NegativeMidChance, float NegativeCap, float PositiveMid,
    float PositiveMidChance, float PositiveCap)
{
    /// <summary>The original's presets 1 to 3 (its constants): Recommended, Growth, Vanilla-shaped.</summary>
    public static Curve Preset(int preset) => preset switch
    {
        2 => new(17.5f, 2.5f, 47.5f, 30, 10, 60, 30, 32.5f, 60),
        3 => new(17.5f, 2.5f, 47.5f, 15, 10, 30, 15, 32.5f, 30),
        _ => new(17.5f, 2.5f, 47.5f, 30, 7.5f, 60, 30, 35, 60),
    };

    public float Chance(float difference)
    {
        float chance = difference < 0
            ? Side(-difference, NegativeMid, NegativeMidChance, NegativeCap, Min)
            : Side(difference, PositiveMid, PositiveMidChance, PositiveCap, Max);
        return chance < Min ? Min : Math.Min(chance, Max);
    }

    float Side(float gap, float mid, float midChance, float cap, float end)
    {
        if (gap >= cap) return end;
        if (gap > mid) return cap <= mid ? end : midChance + (end - midChance) * Math.Clamp((gap - mid) / (cap - mid), 0, 1);

        return mid <= 0 ? midChance : Base + (midChance - Base) * Math.Clamp(gap / mid, 0, 1);
    }
}

/// <summary>
/// How a side's score is read from strength, dexterity and toughness (<b>Verified</b>, as above): 0 the largest of the three, 1 the larger of
/// strength and dexterity, 2 their average weighted by the weights (a weight at or under 0 counts 0; 0 when the weights sum to 0.0001 or less).
/// </summary>
readonly record struct Reference(int Mode, float Strength, float Dexterity, float Toughness)
{
    const float SmallestWeights = 0.0001f;

    public float Score(float strength, float dexterity, float toughness)
    {
        if (Mode == 1) return MathF.Max(strength, dexterity);
        if (Mode != 2) return MathF.Max(MathF.Max(strength, dexterity), toughness);

        float ws = MathF.Max(Strength, 0), wd = MathF.Max(Dexterity, 0), wt = MathF.Max(Toughness, 0);
        float sum = ws + wd + wt;
        return sum > SmallestWeights ? (ws * strength + wd * dexterity + wt * toughness) / sum : 0;
    }
}

/// <summary>One effect's settings: its curve, the source's and target's references, its cooldown and force terms.</summary>
sealed record EffectRule(string Name, bool Enabled, Curve Curve, Reference Source, Reference Target, int CooldownMs, float ForceMultiplier,
    float MinimumForce);

/// <summary>Every setting the simulation reads, copied from <see cref="GuardBreakSettings"/> in a serial step.</summary>
sealed record GuardBreakRules(bool Enabled, bool AllowBoth, bool Effective, bool IgnoreStumbling, int TemplateMaxAgeMs, int MinimumObservations,
    EffectRule Bash, EffectRule GuardBreak, bool LogRolls, bool LogStagger)
{
    public static GuardBreakRules Of(GuardBreakSettings s)
    {
        int preset = Array.IndexOf(GuardBreakSettings.Presets, s.BalancePreset);
        var bash = new EffectRule("Bash", s.BashEnabled, preset > 0 ? Curve.Preset(preset) : BashCurve(s),
            Ref(s.BashBlockerForce, s.BashBlockerStrengthWeight, s.BashBlockerDexterityWeight, s.BashBlockerToughnessWeight),
            Ref(s.BashAttackerResistance, s.BashAttackerStrengthWeight, s.BashAttackerDexterityWeight, s.BashAttackerToughnessWeight),
            s.BashCooldownMs, s.BashForceMultiplier, s.BashMinimumForce);
        var guardBreak = new EffectRule("GuardBreak", s.GuardBreakEnabled, preset > 0 ? Curve.Preset(preset) : GuardBreakCurve(s),
            Ref(s.GuardBreakAttackerForce, s.GuardBreakAttackerStrengthWeight, s.GuardBreakAttackerDexterityWeight,
                s.GuardBreakAttackerToughnessWeight),
            Ref(s.GuardBreakBlockerResistance, s.GuardBreakBlockerStrengthWeight, s.GuardBreakBlockerDexterityWeight,
                s.GuardBreakBlockerToughnessWeight),
            s.GuardBreakCooldownMs, s.GuardBreakForceMultiplier, s.GuardBreakMinimumForce);
        return new(s.Enabled, s.AllowBothEffects, s.EffectiveStats, s.IgnoreAlreadyStumbling, Math.Max(1, s.TemplateMaxAgeMs),
            Math.Max(1, s.MinimumTemplateObservations), bash, guardBreak, s.LogRolls, s.LogStagger);
    }

    static Curve BashCurve(GuardBreakSettings s) => new(s.BashBaseChance, s.BashMinimumChance, s.BashMaximumChance, s.BashNegativeMidDifference,
        s.BashNegativeMidChance, s.BashNegativeCapDifference, s.BashPositiveMidDifference, s.BashPositiveMidChance, s.BashPositiveCapDifference);

    static Curve GuardBreakCurve(GuardBreakSettings s) => new(s.GuardBreakBaseChance, s.GuardBreakMinimumChance, s.GuardBreakMaximumChance,
        s.GuardBreakNegativeMidDifference, s.GuardBreakNegativeMidChance, s.GuardBreakNegativeCapDifference, s.GuardBreakPositiveMidDifference,
        s.GuardBreakPositiveMidChance, s.GuardBreakPositiveCapDifference);

    static Reference Ref(string mode, float strength, float dexterity, float toughness) =>
        new(Math.Max(0, Array.IndexOf(GuardBreakSettings.References, mode)), strength, dexterity, toughness);
}
