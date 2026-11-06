using Meitou.Data.Characters;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using Meitou.Data.Gameplay.Bodies;

namespace Meitou.Brutality;

/// <summary>Meitou's passive combat skill, independent of the original's fixed stat numbers. Experience is the saved authority.</summary>
public sealed class BrutalityProgress(float experience = 0)
{
    public const float MaximumLevel = 100, ExperiencePerLevelSquared = 10;
    public const float MaximumExperience = MaximumLevel * MaximumLevel * ExperiencePerLevelSquared;

    public float Experience { get; private set; } = ValidExperience(experience);
    public float Level => MathF.Sqrt(Experience / ExperiencePerLevelSquared);
    public float ExperienceToNextLevel => Level >= MaximumLevel ? 0 : AtLevel(MathF.Floor(Level) + 1) - Experience;
    public float Progress01
    {
        get
        {
            float level = MathF.Floor(Level);
            return level >= MaximumLevel ? 1 : (Experience - AtLevel(level)) / (AtLevel(level + 1) - AtLevel(level));
        }
    }

    /// <summary>Returns the experience actually gained; invalid/negative awards do nothing and mastery stops at 100.</summary>
    public float Award(float experience)
    {
        if (!float.IsFinite(experience) || experience <= 0) return 0;
        float before = Experience;
        Experience = MathF.Min(MaximumExperience, Experience + experience);
        return Experience - before;
    }

    public static BrutalityProgress FromLevel(float level) => new(AtLevel(level));

    /// <summary>A new NPC's experience reflects raw attack and its strongest melee discipline; no faction or moral alignment is read.</summary>
    public static BrutalityProgress FromCombat(CharacterStats stats)
    {
        float strongest = 0;
        foreach (var stat in MeleeSkills) strongest = MathF.Max(strongest, ValidLevel(stats[stat]));
        return FromLevel((ValidLevel(stats[StatsEnumerated.MeleeAttack]) + strongest) * 0.5f);
    }

    static readonly StatsEnumerated[] MeleeSkills = [StatsEnumerated.Katanas, StatsEnumerated.Sabres, StatsEnumerated.Hackers,
        StatsEnumerated.HeavyWeapons, StatsEnumerated.Blunt, StatsEnumerated.Polearms, StatsEnumerated.MartialArts];

    static float ValidLevel(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, MaximumLevel) : 0;
    public static float ValidExperience(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, MaximumExperience) : 0;
    static float AtLevel(float level) { level = ValidLevel(level); return level * level * ExperiencePerLevelSquared; }

    internal void Hash(ref StateHasher hash) => HashExperience(ref hash, Experience);
    internal static void HashExperience(ref StateHasher hash, float experience)
    {
        if (!(experience > 0)) return;
        hash.Add(0x42727574);
        hash.Add(experience);
    }
}
