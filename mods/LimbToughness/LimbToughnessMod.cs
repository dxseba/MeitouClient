using Meitou.Modding;
using Meitou.Simulation;

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
        context.AddSystem("limbs", _ => new LimbSystem(settings, context.Log), SystemAnchor.End);
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

    [Setting("Application mode", Min = 0, Max = 4, Step = 1,
        Hint = "ApplicationMode: 1 setup, 2 init, 3 updateStats, 4 all stored paths (the default); here 1 to 4 all apply when a character is "
            + "made or loaded and when its Toughness changes. 0 getter-only is not available and acts as 4.")]
    public int Mode { get; set; } = 4;
}
