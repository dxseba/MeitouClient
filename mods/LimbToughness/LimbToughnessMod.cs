using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.LimbToughness;

/// <summary>
/// Port of LimbToughness (workshop 3787222853): every point of Toughness raises each part's maximum health by
/// <see cref="ToughnessSettings.PercentPerPoint"/> of its own (0.5%: 50 Toughness = +25%), additively, rounded to a whole number as the original's.
/// Applied on the part's HP multiplier.
/// </summary>
public sealed class LimbToughnessMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new ToughnessSettings();
        context.AddSettings(settings);
        context.AddSystem("limbs", _ => new LimbSystem(settings), SystemAnchor.End);
        context.Log("Limb Toughness initialized");
    }
}

/// <summary>The original's LimbToughness.ini keys, defaults and fallbacks.</summary>
public sealed class ToughnessSettings
{
    [Setting("Health % per Toughness", Min = 0, Max = 5, Step = 0.05,
        Hint = "Extra maximum health of each limb per point of Toughness, in % of its own (HealthPercentPerToughness).")]
    public float PercentPerPoint { get; set; } = 0.5f;

    [Setting("Skeletons and robot limbs", Hint = "Robotic parts too: a robot race's parts and fitted robotic limbs (ApplyToSkeletonsAndRobotLimbs).")]
    public bool RoboticParts { get; set; }

    [Setting("Robot races", Hint = "Races marked as robots, and races cloned from them (ApplyToRobotRaces).")]
    public bool RobotRaces { get; set; } = true;

    [Setting("Animals", Hint = "Animal races (ApplyToAnimals).")]
    public bool Animals { get; set; } = true;
}

/// <summary>
/// Every 60th tick in the serial slow step, sets each living character's part multiplier to its baseline times 1 + Toughness x the rate, the
/// maximum rounded to a whole number. The baseline is the multiplier first seen (kept by part object, so a new world starts afresh), or for an
/// animal its size now, as its growth resets the multiplier. The health is rescaled with the maximum, as the original's (floored).
/// </summary>
sealed class LimbSystem(ToughnessSettings settings) : ITickSystem
{
    /// <summary>The original's tolerance for a part at full baseline health, and its flooring nudge.</summary>
    const float FullWithin = 1, Nudge = 1e-4f;

    readonly ConditionalWeakTable<HealthPart, StrongBox<float>> _baseline = new();

    public void SlowWorld(World world)
    {
        if (world.Tick % 60 != 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical: { Dead: false } medical, Stats: { } stats, Race: { } race } cold)
                continue;

            float factor = Applies(cold, race) ? Factor(stats.ScaledToughness) : 1;
            foreach (var part in medical.Parts)
            {
                bool excluded = part.IsRobotic(race) && !settings.RoboticParts;
                Apply(part, BaselineOf(part, cold, medical), excluded ? 1 : factor);
            }
        }
    }

    bool Applies(CharacterCold cold, Meitou.Data.Gameplay.Bodies.RaceData race) =>
        (!race.IsRobot || settings.RobotRaces) && (cold.Animal is null || settings.Animals);

    /// <summary>1 + rate / 100 x toughness, 1 for a rate or toughness at or below 0 (an invalid rate is the default 0.5).</summary>
    float Factor(float toughness)
    {
        float rate = float.IsFinite(settings.PercentPerPoint) && settings.PercentPerPoint >= 0 ? settings.PercentPerPoint : 0.5f;
        return rate > 0 && float.IsFinite(toughness) && toughness > 0 ? 1 + rate / 100 * toughness : 1;
    }

    float BaselineOf(HealthPart part, CharacterCold cold, MedicalState medical)
    {
        if (cold.Animal is not null) return medical.ScaleK;

        return _baseline.GetValue(part, p => new StrongBox<float>(p.HpMultiplier)).Value;
    }

    void Apply(HealthPart part, float baseline, float factor)
    {
        float unit = part.EffectiveBaseHp * part.BodyScale;
        if (unit <= 0 || baseline <= 0) return;

        float wanted = MathF.Floor(unit * baseline * factor + 0.5f) / unit;
        if (MathF.Abs(part.HpMultiplier - wanted) < 1e-6f) return;

        float plain = unit * baseline - part.Wear, before = part.MaxHp;
        bool savedRaised = part.Flesh > before + FullWithin;   // a load starts from the plain maximum, the health saved with the bonus
        if (!savedRaised && MathF.Abs(part.Flesh - plain) <= FullWithin && plain < before) part.Flesh = before;

        part.HpMultiplier = wanted;
        if (savedRaised) part.Flesh = MathF.Min(part.Flesh, part.MaxHp);
        else if (before > 0 && part.Flesh >= 0) part.Flesh = MathF.Min(MathF.Floor(part.Flesh * part.MaxHp / before + Nudge), part.MaxHp);
    }
}
