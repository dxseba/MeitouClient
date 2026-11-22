using Meitou.Modding;

namespace Meitou.LimitedStats;

/// <summary>
/// The original's <c>[General]</c>, <c>[Weights]</c> and <c>[Debug]</c> keys as Mod settings. The built-in defaults are the original DLL's own
/// (<b>Verified</b>, original source's <c>Config</c>: counts and weights 0, rates and multipliers 1, caps 100 x race, at most 150); the values come
/// from the original mod's <c>LimitTalent.ini</c> when it is installed, set before the settings are registered, so they are what Reset puts back
/// and the player's own changes still win. Nothing of the author's ini is shipped.
/// </summary>
public sealed class LimitedStatsSettings
{
    const string Caps = "Caps", Talents = "Talents", Weights = "Talent weights (%)", Port = "Port";

    [Setting("Cap scale", Hint = "CapScale: a stat's race cap is the race's XP multiplier for it times this, plus Cap shift.", Min = 0, Max = 300,
        Step = 1, SubGroup = Caps)]
    public float CapScale { get; set; } = 100;

    [Setting("Cap shift", Hint = "CapShift: added to every race cap.", Min = -100, Max = 100, Step = 1, SubGroup = Caps)]
    public float CapShift { get; set; }

    [Setting("Lowest cap", Hint = "CapMin: no race cap is below this.", Min = 0, Max = 300, Step = 1, SubGroup = Caps)]
    public float CapMin { get; set; }

    [Setting("Highest cap", Hint = "CapMax: no race cap is above this, and no training goes past it (an entry's own cap may be higher).", Min = 1,
        Max = 300, Step = 1, SubGroup = Caps)]
    public float CapMax { get; set; } = 150;

    [Setting("Over-cap protection", Hint = "ProtectGap: a stat this far or more above its cap is left alone and still trains; -1 off.", Min = -1,
        Max = 100, Step = 1, SubGroup = Caps)]
    public float ProtectGap { get; set; } = -1;

    [Setting("Lazy: skills", Hint = "LazySkills: how many skills a Lazy character trains slower.", Min = 0, Max = 33, SubGroup = Talents)]
    public int LazySkills { get; set; }

    [Setting("Lazy: rate", Hint = "LazyRate: their XP multiplier.", Min = 0, Max = 5, Step = 0.05f, SubGroup = Talents)]
    public float LazyRate { get; set; } = 1;

    [Setting("Loser: skills", Hint = "LoserSkills: how many skills a Loser trains much slower.", Min = 0, Max = 33, SubGroup = Talents)]
    public int LoserSkills { get; set; }

    [Setting("Loser: rate", Hint = "LoserRate: a Loser's XP multiplier on every other skill.", Min = 0, Max = 5, Step = 0.05f, SubGroup = Talents)]
    public float LoserRate { get; set; } = 1;

    [Setting("Loser: skill rate", Hint = "LoserSkillRate: the XP multiplier of a Loser's worst skills.", Min = 0, Max = 5, Step = 0.05f,
        SubGroup = Talents)]
    public float LoserSkillRate { get; set; } = 1;

    [Setting("Diligent: skills", Hint = "DiligentSkills: how many skills a Diligent character trains faster.", Min = 0, Max = 33, SubGroup = Talents)]
    public int DiligentSkills { get; set; }

    [Setting("Diligent: rate", Hint = "DiligentRate: their XP multiplier.", Min = 0, Max = 5, Step = 0.05f, SubGroup = Talents)]
    public float DiligentRate { get; set; } = 1;

    [Setting("Genius: skills", Hint = "GeniusSkills: how many skills a Genius trains much faster.", Min = 0, Max = 33, SubGroup = Talents)]
    public int GeniusSkills { get; set; }

    [Setting("Genius: rate", Hint = "GeniusRate: their XP multiplier.", Min = 0, Max = 5, Step = 0.05f, SubGroup = Talents)]
    public float GeniusRate { get; set; } = 1;

    [Setting("Genius: cap breaks", Hint = "GeniusBreakCount: how many of those skills get a higher cap.", Min = 0, Max = 33, SubGroup = Talents)]
    public int GeniusBreakCount { get; set; }

    [Setting("Genius: cap break", Hint = "GeniusBreakMult: their cap is the race cap times this.", Min = 0, Max = 5, Step = 0.05f, SubGroup = Talents)]
    public float GeniusBreakMult { get; set; } = 1;

    [Setting("Superhuman: cap breaks", Hint = "SuperBreakCount: how many skills of a Superhuman get a higher cap.", Min = 0, Max = 33,
        SubGroup = Talents)]
    public int SuperBreakCount { get; set; }

    [Setting("Superhuman: cap break", Hint = "SuperBreakMult: their cap is the race cap times this.", Min = 0, Max = 5, Step = 0.05f,
        SubGroup = Talents)]
    public float SuperBreakMult { get; set; } = 1;

    [Setting("Skill correlation jitter", Hint = "CorrelationJitter: randomness added when a talent's skills are drawn by their [Correlation].",
        Min = 0, Max = 2, Step = 0.05f, SubGroup = Talents)]
    public float CorrelationJitter { get; set; }

    [Setting("Normal", Hint = "Weight of the Normal talent.", Min = 0, Max = 100, Step = 0.01f, SubGroup = Weights)]
    public float WeightNormal { get; set; }

    [Setting("Lazy", Hint = "Weight of the Lazy talent.", Min = 0, Max = 100, Step = 0.01f, SubGroup = Weights)]
    public float WeightLazy { get; set; }

    [Setting("Diligent", Hint = "Weight of the Diligent talent.", Min = 0, Max = 100, Step = 0.01f, SubGroup = Weights)]
    public float WeightDiligent { get; set; }

    [Setting("Loser", Hint = "Weight of the Loser talent.", Min = 0, Max = 100, Step = 0.01f, SubGroup = Weights)]
    public float WeightLoser { get; set; }

    [Setting("Genius", Hint = "Weight of the Genius talent.", Min = 0, Max = 100, Step = 0.01f, SubGroup = Weights)]
    public float WeightGenius { get; set; }

    [Setting("Superhuman", Hint = "Weight of the Superhuman talent.", Min = 0, Max = 100, Step = 0.01f, SubGroup = Weights)]
    public float WeightSuperhuman { get; set; }

    [Setting("Slave is Loser (%)", Hint = "SlaveLoserPercent: chance that a slave rolls Loser; a slave that does not never rolls it.", Min = 0,
        Max = 100, Step = 1, SubGroup = Weights)]
    public float SlaveLoserPercent { get; set; }

    [Setting("Player XP rate", Hint = "PlayerExpRate: multiplies every XP gain of player characters.", Min = 0, Max = 5, Step = 0.05f)]
    public float PlayerExpRate { get; set; } = 1;

    [Setting("NPC XP rate", Hint = "NpcExpRate: multiplies every XP gain of other characters.", Min = 0, Max = 5, Step = 0.05f)]
    public float NpcExpRate { get; set; } = 1;

    [Setting("Log talents", Hint = "WriteLog: log the talents rolled and the ini's problems.")]
    public bool WriteLog { get; set; }

    [Setting("Player characters only", Hint = "Port option. On: only player characters get caps, talents and XP rates (the original: everyone).",
        SubGroup = Port)]
    public bool PlayerOnly { get; set; }

    [Setting("Panel lines", Hint = "Port option. Lines in the HUD's character panel: 1 the cap range, 2 also the stats at their cap; 0 none.",
        Min = 0, Max = 2, Step = 1, SubGroup = Port)]
    public int PanelLines { get; set; }

    /// <summary>One <c>[General]</c>, <c>[Weights]</c> or <c>[Debug]</c> key of the original's ini (lower case), as its reader parses it.</summary>
    internal void ApplyIni(string key, string v)
    {
        switch (key)
        {
            case "capscale": CapScale = Ini.Number(v); break;
            case "capshift": CapShift = Ini.Number(v); break;
            case "capmin": CapMin = Ini.Number(v); break;
            case "capmax": CapMax = Ini.Number(v); break;
            case "lazyskills": LazySkills = Ini.Integer(v); break;
            case "loserskills": LoserSkills = Ini.Integer(v); break;
            case "diligentskills": DiligentSkills = Ini.Integer(v); break;
            case "geniusskills": GeniusSkills = Ini.Integer(v); break;
            case "geniusbreakcount": GeniusBreakCount = Ini.Integer(v); break;
            case "superbreakcount": SuperBreakCount = Ini.Integer(v); break;
            case "lazyrate": LazyRate = Ini.Number(v); break;
            case "loserrate": LoserRate = Ini.Number(v); break;
            case "loserskillrate": LoserSkillRate = Ini.Number(v); break;
            case "diligentrate": DiligentRate = Ini.Number(v); break;
            case "geniusrate": GeniusRate = Ini.Number(v); break;
            case "geniusbreakmult": GeniusBreakMult = Ini.Number(v); break;
            case "superbreakmult": SuperBreakMult = Ini.Number(v); break;
            case "correlationjitter": CorrelationJitter = Ini.Number(v); break;
            default: ApplyWeightsOrDebug(key, v); break;
        }
    }

    void ApplyWeightsOrDebug(string key, string v)
    {
        switch (key)
        {
            case "normal": WeightNormal = Ini.Number(v); break;
            case "lazy": WeightLazy = Ini.Number(v); break;
            case "diligent": WeightDiligent = Ini.Number(v); break;
            case "loser": WeightLoser = Ini.Number(v); break;
            case "genius": WeightGenius = Ini.Number(v); break;
            case "superhuman": WeightSuperhuman = Ini.Number(v); break;
            case "slaveloserpercent": SlaveLoserPercent = Ini.Number(v); break;
            case "playerexprate": PlayerExpRate = Ini.Number(v); break;
            case "npcexprate": NpcExpRate = Ini.Number(v); break;
            case "writelog": WriteLog = Ini.Integer(v) != 0; break;
            case "protectgap": ProtectGap = Ini.Number(v); break;
        }
    }

    /// <summary>The values the simulation reads, copied in its serial step.</summary>
    internal Rules Snapshot() => new(CapScale, CapShift, CapMin, CapMax, ProtectGap, LazySkills, LazyRate, LoserSkills, LoserRate, LoserSkillRate,
        DiligentSkills, DiligentRate, GeniusSkills, GeniusRate, GeniusBreakCount, GeniusBreakMult, SuperBreakCount, SuperBreakMult,
        CorrelationJitter, [WeightNormal, WeightLazy, WeightDiligent, WeightLoser, WeightGenius, WeightSuperhuman], SlaveLoserPercent,
        PlayerExpRate, NpcExpRate, PlayerOnly);
}

/// <summary>
/// The settings as the simulation reads them. <see cref="Weights"/> is in the original's roll order: Normal, Lazy, Diligent, Loser, Genius,
/// Superhuman.
/// </summary>
sealed record Rules(float CapScale, float CapShift, float CapMin, float CapMax, float ProtectGap, int LazySkills, float LazyRate, int LoserSkills,
    float LoserRate, float LoserSkillRate, int DiligentSkills, float DiligentRate, int GeniusSkills, float GeniusRate, int GeniusBreakCount,
    float GeniusBreakMult, int SuperBreakCount, float SuperBreakMult, float CorrelationJitter, float[] Weights, float SlaveLoserPercent,
    float PlayerExpRate, float NpcExpRate, bool PlayerOnly)
{
    /// <summary>A cap held in [CapMin, CapMax] (min first, as the original clamps).</summary>
    public float Clamp(float cap)
    {
        if (cap < CapMin) cap = CapMin;
        return cap > CapMax ? CapMax : cap;
    }
}
