using Meitou.Modding;

namespace Meitou.Mentors;

/// <summary>The original's <c>Mentors.ini</c> options under Mod settings, with its defaults (<b>Verified</b>, original <c>Mentors.ini</c>).</summary>
public sealed class MentorsSettings
{
    [Setting("Enable Mentors", Hint = "Port option: shows the Learn from row on characters who can teach.")]
    public bool Enabled { get; set; } = true;

    [Setting("Require profession", Hint = "RequireProfession. On: an NPC teaches only the skills of their trade. Off: anyone teaches anything they are good at.")]
    public bool RequireProfession { get; set; } = true;

    [Setting("Minimum teacher level", Hint = "MinTeacherLevel: a teacher needs at least this level in a skill to teach it.", Min = 1, Max = 100)]
    public int MinTeacherLevel { get; set; } = 20;

    [Setting("Teacher margin", Hint = "TeacherMargin: a student can be trained up to (teacher's level - this) at most.", Min = 0, Max = 50)]
    public int TeacherGap { get; set; } = 5;

    [Setting("Training cap", Hint = "TrainingCap: training never goes above this level, the rest comes from practice only.", Min = 1, Max = 100)]
    public int MaxLevel { get; set; } = 60;

    [Setting("Base price", Hint = "BasePrice: one lesson (+1 level) = BasePrice x (1 + level/10)^2 x (0.75 + teacher/100), rounded up to 10 cats.",
        Min = 1, Max = 1000)]
    public int BasePrice { get; set; } = 60;

    [Setting("Attribute price multiplier", Hint = "AttributePriceMult: price multiplier for strength, dexterity, toughness and perception.", Min = 1,
        Max = 5, Step = 0.1)]
    public float AttributeMultiplier { get; set; } = 1.5f;

    [Setting("Hotkey radius", Hint = "Radius: the Mentors window key opens training with the nearest teacher this close (game units).", Min = 20,
        Max = 2000, Step = 10)]
    public int Radius { get; set; } = 500;
}
