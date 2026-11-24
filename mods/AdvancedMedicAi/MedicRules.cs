namespace Meitou.AdvancedMedicAi;

/// <summary>
/// A faction's rule in the rules window (the original's cycle button: Use general rule, Always heal, Never heal).
/// </summary>
public enum FactionRule
{
    General,
    Always,
    Never,
}

/// <summary>
/// The treatment groups, in the order the original serves them (its category numbers): own characters, allies, neutrals, wild animals, enemies.
/// </summary>
public enum PatientGroup
{
    Player = 0,
    Ally = 1,
    Neutral = 2,
    Animal = 3,
    Enemy = 4,
}

/// <summary>
/// The settings the simulation runs on, copied in a serial step; the ini-only values are <see cref="MedicTuning"/>. Distances are in game units
/// (<see cref="MedicTuning.UnitsPerMeter"/> per meter).
/// </summary>
public sealed record MedicRules
{
    public bool[] Groups { get; init; } = [true, true, true, false, false];
    public bool WholeBase { get; init; } = true;
    public float FieldRadiusSq { get; init; }
    public float RobotRadiusSq { get; init; }
    public bool CombatMedic { get; init; }
    public float CombatRadiusSq { get; init; }
    public bool AvoidHazards { get; init; } = true;
    public float GraceSeconds { get; init; } = 60;
    public bool Notify { get; init; } = true;
    public IReadOnlyDictionary<string, FactionRule> Factions { get; init; } = new Dictionary<string, FactionRule>();

    public static MedicRules From(MedicSettings s, float unitsPerMeter) => new()
    {
        Groups = [s.TreatPlayerCharacters, s.TreatAllies, s.TreatNeutrals, s.TreatAnimals, s.TreatEnemies],
        WholeBase = s.WholeBaseReach,
        FieldRadiusSq = Squared(s.FieldRadiusMeters, unitsPerMeter),
        RobotRadiusSq = Squared(s.FieldRobotRadiusMeters, unitsPerMeter),
        CombatMedic = s.CombatMedic,
        CombatRadiusSq = Squared(s.CombatRadiusMeters, unitsPerMeter),
        AvoidHazards = s.AvoidHazards,
        GraceSeconds = s.ManualOrderGraceSeconds,
        Notify = s.ShowNotifications,
        Factions = FactionRuleText.Parse(s.FactionRules),
    };

    static float Squared(float meters, float units) => meters * units * meters * units;

    /// <summary>
    /// Whether a group is treated (the original's per-category switch).
    /// </summary>
    public bool Treats(PatientGroup group) => Groups[(int)group];
}

/// <summary>
/// The faction rules' text form: <c>id:always</c> and <c>id:never</c> entries separated by <c>;</c> (a faction on the general rule is left
/// out). Unknown entries are skipped.
/// </summary>
public static class FactionRuleText
{
    public static Dictionary<string, FactionRule> Parse(string text)
    {
        var rules = new Dictionary<string, FactionRule>(StringComparer.Ordinal);
        foreach (string entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = entry.LastIndexOf(':');
            if (colon <= 0) continue;

            var rule = entry[(colon + 1)..] switch
            {
                "always" => FactionRule.Always,
                "never" => FactionRule.Never,
                _ => FactionRule.General,
            };
            if (rule != FactionRule.General) rules[entry[..colon]] = rule;
        }

        return rules;
    }

    public static string Format(IReadOnlyDictionary<string, FactionRule> rules) =>
        string.Join(";", rules.Where(r => r.Value != FactionRule.General).OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => r.Key + ":" + (r.Value == FactionRule.Always ? "always" : "never")));
}
