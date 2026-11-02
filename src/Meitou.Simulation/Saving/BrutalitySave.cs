using Meitou.Data.Fcs;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Saving;

/// <summary>An optional character-state extension; original STATS fields and unrelated keys remain untouched.</summary>
public static class BrutalitySave
{
    public const string ExperienceKey = "meitou brutality xp";

    public static float ReadExperience(FcsRecord? state) => BrutalityProgress.ValidExperience(state?.Floats.GetValueOrDefault(ExperienceKey) ?? 0);
    public static BrutalityProgress Read(FcsRecord? state) => new(ReadExperience(state));

    public static void Write(FcsRecord state, BrutalityProgress? skill)
    {
        if (skill is null) return; // An untouched offscreen member keeps its original extension, if any.
        if (skill.Experience > 0) state.Floats[ExperienceKey] = skill.Experience;
        else state.Floats.Remove(ExperienceKey);
    }
}
