using Meitou.Data.Gameplay.Bodies;

namespace Meitou.LimitedStats;

/// <summary>
/// The data half of the original's <c>LimitTalent.ini</c>: <c>[Stats]</c> and <c>[SpecialCharacter]</c> entries, <c>[Race]</c> base multipliers and
/// <c>[Correlation]</c> (Chinese section names too); its <c>[General]</c>, <c>[Weights]</c> and <c>[Debug]</c> values become the settings' defaults
/// (<see cref="LimitedStatsSettings.ApplyIni"/>). The author's values are not shipped: the file is read from the original mod's folder.
/// </summary>
sealed class TalentConfig
{
    public Dictionary<string, TalentSpec> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TalentSpec> Specials { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RaceOverride> Races { get; } = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, float> _correlation = [];

    public float Correlation(StatsEnumerated a, StatsEnumerated b) => _correlation.GetValueOrDefault(CorrelationKey(a, b));

    static int CorrelationKey(StatsEnumerated a, StatsEnumerated b) => Math.Min((int)a, (int)b) * 100 + Math.Max((int)a, (int)b);

    /// <summary>Reads <paramref name="path"/> (nothing when it is missing), handing the settings sections to <paramref name="settings"/>.</summary>
    public static TalentConfig Load(string? path, LimitedStatsSettings settings, Action<string> log)
    {
        var config = new TalentConfig();
        if (path is null) return config;

        foreach (var (section, key, value) in Ini.Read(path))
            config.Apply(section, key, value, settings, log);

        log($"LimitTalent.ini read from {path}: {config.Overrides.Count} stats, {config.Specials.Count} special, {config.Races.Count} race entries");
        return config;
    }

    void Apply(string section, string key, string value, LimitedStatsSettings settings, Action<string> log)
    {
        switch (section)
        {
            case "general" or "常规" or "weights" or "权重" or "debug" or "调试":
                settings.ApplyIni(key.ToLowerInvariant(), value);
                break;
            case "stats" or "属性":
                AddSpec(Overrides, key, value, log);
                break;
            case "specialcharacter" or "特殊角色":
                AddSpec(Specials, key, value, log);
                break;
            case "race" or "races" or "种族天赋":
                AddRace(key, value, log);
                break;
            case "correlation" or "相关度":
                AddCorrelation(key, value);
                break;
        }
    }

    static void AddSpec(Dictionary<string, TalentSpec> into, string rawKey, string value, Action<string> log)
    {
        var spec = new TalentSpec();
        string key = Ini.Trim(rawKey);
        int bracket = key.IndexOf('[');
        if (bracket >= 0 && key.Length > bracket + 1 && key[^1] == ']')
        {
            ReadModifier(spec, Ini.Trim(key[(bracket + 1)..^1]), log);
            key = Ini.Trim(key[..bracket]);
        }

        foreach (string raw in Ini.SplitTop(Ini.Trim(value), ','))
        {
            string group = Ini.Trim(raw);
            if (group.Length == 0) continue;

            int colon = group.IndexOf(':');
            string head = Ini.Trim(colon < 0 ? group : group[..colon]);
            string tail = colon < 0 ? "" : group[(colon + 1)..];
            spec.Groups.Add((head, tail.Length > 0 ? StatFunctions.Parse(tail, log) : new StatFunctions()));
        }

        into[key] = spec;
    }

    /// <summary><c>rand_other_skills</c>, optionally <c>_not</c>, then talent type names.</summary>
    static void ReadModifier(TalentSpec spec, string modifier, Action<string> log)
    {
        const string prefix = "rand_other_skills";
        if (!modifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        spec.RandomOthers = true;
        string rest = Ini.Trim(modifier[prefix.Length..]);
        if (rest.StartsWith("_not", StringComparison.OrdinalIgnoreCase))
        {
            spec.RandomOthersExcept = true;
            rest = Ini.Trim(rest[4..]);
        }

        foreach (string raw in rest.Split(','))
        {
            string token = Ini.Trim(raw);
            if (token.Length == 0) continue;

            if (TypeOf(token) is { } type) spec.RandomTypes.Add(type);
            else log($"unknown talent type '{token}' in key modifier");
        }
    }

    static TalentType? TypeOf(string name) => name.ToLowerInvariant() switch
    {
        "normal" => TalentType.Normal,
        "lazy" => TalentType.Lazy,
        "diligent" => TalentType.Diligent,
        "loser" => TalentType.Loser,
        "genius" => TalentType.Genius,
        "superhuman" or "super" => TalentType.Superhuman,
        _ => null,
    };

    /// <summary><c>race id|race name = default 0.9, farming 1.0, ...</c>; negative values read 0.</summary>
    void AddRace(string key, string value, Action<string> log)
    {
        var race = new RaceOverride();
        foreach (string raw in value.Split(','))
        {
            string token = Ini.Trim(raw);
            if (token.Length == 0) continue;

            if (token.Length > 8 && token.StartsWith("default ", StringComparison.OrdinalIgnoreCase))
                race.Default = MathF.Max(0, Ini.Number(token[8..]));
            else if (StatNames.FindWithNumber(token) is var (stat, number))
                race.Values[stat] = MathF.Max(0, number);
            else
                log($"bad race talent token '{token}'");
        }

        foreach (string part in key.Split('|'))
        {
            string name = Ini.Trim(part);
            if (name.Length > 0) Races[name] = race;
        }
    }

    void AddCorrelation(string key, string value)
    {
        int bar = key.IndexOf('|');
        if (bar < 0) return;

        var a = StatNames.Find(key[..bar]);
        var b = StatNames.Find(key[(bar + 1)..]);
        if (a != StatsEnumerated.None && b != StatsEnumerated.None) _correlation[CorrelationKey(a, b)] = Ini.Number(value);
    }
}

/// <summary>A <c>[Race]</c> entry: every stat at <see cref="Default"/> (1 unless given), the named ones at their own value.</summary>
sealed class RaceOverride
{
    public float Default = 1;
    public Dictionary<StatsEnumerated, float> Values { get; } = [];

    public float Of(StatsEnumerated stat) => Values.TryGetValue(stat, out float v) ? v : Default;
}
