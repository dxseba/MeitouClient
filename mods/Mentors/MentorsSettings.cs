using Meitou.Modding;

namespace Meitou.Mentors;

/// <summary>The original's Mentors.ini options that its description names, under Mod settings.</summary>
public sealed class MentorsSettings
{
    [Setting("Enable Mentors", Hint = "Shows the Learn from row on friendly characters.")]
    public bool Enabled { get; set; } = true;

    [Setting("Require profession", Hint = "Off: any teacher can teach any skill it knows well enough (the original's RequireProfession=0).")]
    public bool RequireProfession { get; set; } = true;

    [Setting("Minimum teacher level", Hint = "A teacher needs at least this level in a skill to teach it.", Min = 1, Max = 100)]
    public int MinTeacherLevel { get; set; } = 20;

    [Setting("Levels below teacher", Hint = "Training stops this many levels below the teacher's own.", Min = 0, Max = 50)]
    public int TeacherGap { get; set; } = 5;

    [Setting("Maximum training level", Hint = "No skill is trained above this; beyond it only practice helps.", Min = 1, Max = 100)]
    public int MaxLevel { get; set; } = 60;

    [Setting("Price per level", Hint = "Base cats for one level at level 0 (grows with the level and the teacher's expertise).", Min = 1, Max = 1000)]
    public int BasePrice { get; set; } = 40;

    [Setting("Attribute price multiplier", Hint = "Strength, dexterity, toughness and perception cost this much more.", Min = 1, Max = 5, Step = 0.1)]
    public float AttributeMultiplier { get; set; } = 1.5f;

    [Setting("Teaching distance", Hint = "The student must stand this close to the teacher to pay for a lesson.", Min = 20, Max = 500)]
    public int Distance { get; set; } = 80;
}
