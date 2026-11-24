using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;

namespace Meitou.SleepFix;

/// <summary>
/// The original's body checks (SleepFix.dll, decompiled): the health rating its wounded and healed tests compare with <c>healThreshold</c>, the
/// Skeleton Repair Bed test and the unbandaged-cut test. A part's health is its <see cref="HealthPart.Fraction"/>; stumps and crushed limbs,
/// which no bed heals, are left out (this port's).
/// </summary>
static class BodyRating
{
    /// <summary>
    /// The slack under <c>healThreshold</c> that still counts as healed, and a part's health above which the original takes the body as healed
    /// (a reading it does not trust).
    /// </summary>
    const float HealSlack = 0.0005f, ImplausibleHealth = 1.05f;

    /// <summary>
    /// Whether a <see cref="Rating"/> counts as not fully healed: under <see cref="SleepSettings.HealThreshold"/> less the slack.
    /// </summary>
    public static bool Wounded(float rating, SleepSettings settings) => rating < settings.HealThreshold - HealSlack;

    /// <summary>
    /// The lowest health of the counted parts, each clamped to 0-1; 1 when none counts or a part reads above 1.05. A robotic part counts only
    /// when robot parts are not ignored, or, with repair beds about and <c>wearThreshold</c> set, by its wear (1 - wear / maximum, from the
    /// threshold up) and by its health under <c>repairRobotHp</c>.
    /// </summary>
    public static float Rating(MedicalState medical, RaceData? race, SleepSettings settings, bool repairBeds)
    {
        float lowest = 1;
        foreach (var part in medical.Parts)
        {
            if (!Counted(part)) continue;

            bool robotic = Robotic(part, race);
            if (!robotic || !settings.IgnoreRobotParts)
            {
                if (part.Fraction > ImplausibleHealth) return 1;

                lowest = MathF.Min(lowest, Math.Clamp(part.Fraction, 0, 1));
            }
            else if (repairBeds && settings.WearThreshold > 0) lowest = MathF.Min(lowest, RobotRating(part, settings));
        }

        return lowest;
    }

    /// <summary>
    /// A robotic part's share of the rating: its wear from the threshold up, its health under <c>repairRobotHp</c>; 1 otherwise.
    /// </summary>
    static float RobotRating(HealthPart part, SleepSettings settings)
    {
        float rating = 1;
        float full = part.MaxHp + part.Wear;
        if (part.Wear >= settings.WearThreshold && full > 0 && part.Wear < full) rating = 1 - part.Wear / full;

        float health = part.Fraction;
        if (settings.RepairRobotHp > 0 && health <= ImplausibleHealth && health < settings.RepairRobotHp)
            rating = MathF.Min(rating, Math.Clamp(health, 0, 1));

        return rating;
    }

    /// <summary>
    /// Whether the character belongs in a Skeleton Repair Bed: a robotic part worn to <c>wearThreshold</c> or under <c>repairRobotHp</c>, while
    /// every other part is healed (a hurt flesh part goes to a normal bed first).
    /// </summary>
    public static bool NeedsRepair(MedicalState medical, RaceData? race, SleepSettings settings)
    {
        if (settings.WearThreshold <= 0) return false;

        bool due = false;
        foreach (var part in medical.Parts)
        {
            if (!Counted(part)) continue;

            float health = part.Fraction;
            if (!Robotic(part, race))
            {
                if (health > ImplausibleHealth || health < 0 || health < settings.HealThreshold - HealSlack) return false;

                continue;
            }

            if (part.Wear >= settings.WearThreshold) due = true;
            if (settings.RepairRobotHp > 0 && health < settings.RepairRobotHp && health <= ImplausibleHealth) due = true;
        }

        return due;
    }

    /// <summary>
    /// Whether a flesh part has damage no bandage covers (flesh + bandage more than 1 under its maximum): the original's
    /// <c>waitForTreatment</c> test. Blunt damage (stun) and self-healing races do not count.
    /// </summary>
    public static bool UnbandagedCuts(MedicalState medical, RaceData? race)
    {
        foreach (var part in medical.Parts)
        {
            if (!Counted(part) || Robotic(part, race) || part.SelfHealing || part.MaxHp <= 0) continue;
            if (part.Flesh + part.Bandage < part.MaxHp - 1) return true;
        }

        return false;
    }

    /// <summary>
    /// A part a bed can heal: not a stump or a crushed limb, with a maximum.
    /// </summary>
    static bool Counted(HealthPart part) => part.Limb is not (LimbState.Stump or LimbState.Crushed) && part.MaxHp > 0;

    /// <summary>
    /// A robot's part or a fitted limb (<see cref="HealthPart.IsRobotic"/>).
    /// </summary>
    static bool Robotic(HealthPart part, RaceData? race) => race is not null ? part.IsRobotic(race) : part.Limb == LimbState.Replaced;

    /// <summary>
    /// The part table for the <c>dumpParts</c> log: each part's name, health share, flesh, stun, bandage, wear and maximum.
    /// </summary>
    public static string Describe(MedicalState medical) => string.Join("; ", medical.Parts.Select(p => FormattableString.Invariant(
        $"{p.Template.Name} {p.Fraction:0.000} flesh={p.Flesh:0.0} stun={p.Stun:0.0} bandage={p.Bandage:0.0} wear={p.Wear:0.0} max={p.MaxHp:0.0}")));
}
