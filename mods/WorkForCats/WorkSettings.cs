using Meitou.Data.Gameplay.Construction;
using Meitou.Modding;

namespace Meitou.WorkForCats;

/// <summary>
/// The original's <c>WorkForCats.ini</c> <c>[Employment]</c> keys, under Mod settings; same defaults and limits (its <c>Config.h</c>; slider ends
/// below the ini's 1000000 are Meitou's).
/// </summary>
public sealed class WorkSettings
{
    [Setting("Show employment window", Hint = "A window with Get a job / Quit job while a workplace is selected.")]
    public bool ShowEmploymentWindow { get; set; } = true;

    [Setting("Wage at skill 0", Hint = "Whole cats per in-game hour of work at skill 0 (and below).", Min = 0, Max = 10000, Step = 1)]
    public int WageAtSkill0 { get; set; } = 20;

    [Setting("Wage at skill 100", Hint = "Whole cats per in-game hour of work at skill 100 (and above).", Min = 0, Max = 10000, Step = 1)]
    public int WageAtSkill100 { get; set; } = 200;

    [Setting("Allow own workplaces", Hint = "Also employ at your own workplaces: no relation progress and no relation bonus there.")]
    public bool AllowOwnWorkplaces { get; set; }

    [Setting("Relations enabled", Hint = "Work hours raise the employer's relation. Off keeps the progress made so far.")]
    public bool RelationsEnabled { get; set; } = true;

    [Setting("Relation hours", Hint = "Combined work hours for one faction per relation gain.", Min = 0.1, Max = 1000, Step = 0.1)]
    public float RelationHours { get; set; } = 8;

    [Setting("Relation points", Hint = "Relation gained per interval, up to 100.", Min = 0, Max = 100, Step = 0.5)]
    public float RelationPoints { get; set; } = 1;

    [Setting("Relation bonus enabled", Hint = "Positive relations raise the wage.")]
    public bool RelationBonusEnabled { get; set; } = true;

    [Setting("Bonus % at relation 100", Hint = "Wage bonus at relation 100, scaled smoothly from 0 at relation 0.", Min = 0, Max = 1000, Step = 1)]
    public float BonusPercentAt100 { get; set; } = 20;

    /// <summary>
    /// The values the tick works with, copied in a serial step.
    /// </summary>
    internal WorkRules Rules() => new(WageAtSkill0, WageAtSkill100, AllowOwnWorkplaces, RelationsEnabled, RelationHours, RelationPoints,
        RelationBonusEnabled, BonusPercentAt100);
}

/// <summary>
/// A copy of <see cref="WorkSettings"/> for the tick.
/// </summary>
internal sealed record WorkRules(float WageAtSkill0, float WageAtSkill100, bool AllowOwn, bool Relations, float RelationHours, float RelationPoints,
    bool Bonus, float BonusPercent)
{
    public static readonly WorkRules Defaults = new WorkSettings().Rules();
}

/// <summary>
/// The wage formula (workshop description "Skills and wages", "Relations with your employer").
/// </summary>
internal static class Wages
{
    /// <summary>
    /// An in-game hour in simulated seconds: the game day at speed 1 (<see cref="MachineData.DaySeconds"/>) / 24.
    /// </summary>
    public const double HourSeconds = MachineData.DaySeconds / 24.0;

    /// <summary>
    /// The highest relation the work raises to and the one giving the full bonus.
    /// </summary>
    public const float TopRelation = 100;

    /// <summary>
    /// The base pay: lerp from the skill-0 to the skill-100 rate over the skill clamped to 0..100.
    /// </summary>
    public static float Base(WorkRules rules, float skill) =>
        rules.WageAtSkill0 + (rules.WageAtSkill100 - rules.WageAtSkill0) * Math.Clamp(skill, 0, 100) / 100;

    /// <summary>
    /// The relation factor: 1 + clamp(relation, 0, 100) / 100 x the bonus percent; 1 at the player's own workplaces or with the bonus off.
    /// </summary>
    public static float Bonus(WorkRules rules, float relation, bool own) =>
        own || !rules.Bonus ? 1 : 1 + Math.Clamp(relation, 0, TopRelation) / TopRelation * rules.BonusPercent / 100;

    /// <summary>
    /// Cats per in-game hour of work.
    /// </summary>
    public static float Hourly(WorkRules rules, float skill, float relation, bool own) => Base(rules, skill) * Bonus(rules, relation, own);
}
