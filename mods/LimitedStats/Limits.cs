using System.Text.Json;
using Meitou.Data.Gameplay.Bodies;

namespace Meitou.LimitedStats;

/// <summary>One race's or character's overrides: caps and XP multipliers by stat.</summary>
sealed class StatLimits(Dictionary<StatsEnumerated, float> caps, Dictionary<StatsEnumerated, float> xp)
{
    public float? CapOf(StatsEnumerated stat) => caps.TryGetValue(stat, out float cap) ? cap : null;

    public float? XpOf(StatsEnumerated stat) => xp.TryGetValue(stat, out float rate) ? rate : null;
}

/// <summary>
/// <c>limits.json</c>: <c>{ "races": { "Shek": { "cap": { "Strength": 110 }, "xp": { "Strength": 1.2 } } }, "characters": { "Beep": { ... } } }</c>.
/// Races match the race's name or string id, characters their name, both ignoring case; stats are <see cref="StatsEnumerated"/> names, spaces
/// allowed. A missing file means no overrides; a bad entry is logged and skipped.
/// </summary>
sealed class Limits(Dictionary<string, StatLimits> races, Dictionary<string, StatLimits> characters)
{
    public static Limits Load(string path, Action<string> log)
    {
        var races = new Dictionary<string, StatLimits>(StringComparer.OrdinalIgnoreCase);
        var characters = new Dictionary<string, StatLimits>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return new(races, characters);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            Read(document.RootElement, "races", races, log);
            Read(document.RootElement, "characters", characters, log);
        }
        catch (JsonException e)
        {
            log($"limits.json not read: {e.Message}");
        }

        return new(races, characters);
    }

    public StatLimits? Race(RaceData? race) =>
        race is null ? null : races.GetValueOrDefault(race.Name) ?? races.GetValueOrDefault(race.StringId);

    public StatLimits? Character(string name) => characters.GetValueOrDefault(name);

    static void Read(JsonElement root, string section, Dictionary<string, StatLimits> into, Action<string> log)
    {
        if (!root.TryGetProperty(section, out var entries) || entries.ValueKind != JsonValueKind.Object) return;

        foreach (var entry in entries.EnumerateObject())
            into[entry.Name] = new(Stats(entry.Value, "cap", log), Stats(entry.Value, "xp", log));
    }

    static Dictionary<StatsEnumerated, float> Stats(JsonElement entry, string key, Action<string> log)
    {
        var values = new Dictionary<StatsEnumerated, float>();
        if (!entry.TryGetProperty(key, out var stats) || stats.ValueKind != JsonValueKind.Object) return values;

        foreach (var stat in stats.EnumerateObject())
        {
            if (Enum.TryParse<StatsEnumerated>(stat.Name.Replace(" ", ""), ignoreCase: true, out var parsed) && stat.Value.TryGetSingle(out float value))
                values[parsed] = value;
            else
                log($"limits.json: skipped {key} entry '{stat.Name}'");
        }

        return values;
    }
}
