using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;

namespace Meitou.LimitedStats;

/// <summary>
/// One character's caps and XP rates by stat number (a cap of -1: a stat the mod leaves alone), the race's own XP multipliers the hook divides out,
/// and whether a cap is the talent's own (for the colours). Made in the serial step; never changed after it is published.
/// </summary>
sealed class CapEntry
{
    public float[] Caps { get; } = Enumerable.Repeat(-1f, StatNames.Count).ToArray();
    public float[] Rates { get; } = Enumerable.Repeat(1f, StatNames.Count).ToArray();
    public float[] RaceXp { get; } = Enumerable.Repeat(1f, StatNames.Count).ToArray();
    public bool[] OwnCap { get; } = new bool[StatNames.Count];
    public TalentType Type { get; private init; }

    /// <summary>PlayerExpRate or NpcExpRate, which the XP hook applies on top of <see cref="Rates"/> (the talent's, which the window shows).</summary>
    public float XpRate { get; private init; } = 1;

    public bool IsPlayer { get; private init; }
    public bool IsAnimal { get; private init; }

    /// <summary>
    /// The entry for <paramref name="cold"/> (<b>Verified</b>, original source <c>GetCap</c>): the race cap is CapScale x the race's multiplier
    /// (a <c>[Race]</c> entry's, else the game's, outside 0..100 read 1) + CapShift, held in [CapMin, CapMax]; an absolute cap replaces it; a
    /// race-relative one is that cap times, at least or at most the value; a cap break is CapScale x multiplier x break + CapShift, held likewise.
    /// </summary>
    public static CapEntry For(CharacterCold cold, Talent talent, TalentConfig config, Rules rules)
    {
        var entry = new CapEntry
        {
            Type = talent.Type, IsPlayer = cold.IsPlayer, IsAnimal = cold.Animal is not null,
            XpRate = cold.IsPlayer ? rules.PlayerExpRate : rules.NpcExpRate,
        };
        var race = RaceOverrideOf(cold.Race, config);
        foreach (var stat in StatNames.Stats)
        {
            int i = (int)stat;
            entry.RaceXp[i] = GameMultiplier(cold.Race, stat);
            float x = MathF.Max(0, race?.Of(stat) ?? entry.RaceXp[i]);
            entry.Caps[i] = CapOf(talent, stat, x, rules);
            entry.Rates[i] = talent.Rates[i];
            entry.OwnCap[i] = talent.OwnCap(stat);
        }

        return entry;
    }

    static float CapOf(Talent talent, StatsEnumerated stat, float x, Rules rules)
    {
        if (talent.Caps.TryGetValue(stat, out float own)) return own;

        float raceCap = rules.Clamp(rules.CapScale * x + rules.CapShift);
        if (talent.RaceCaps.TryGetValue(stat, out var relative))
        {
            return relative.Mode switch
            {
                CapMode.RaceTimes => raceCap * relative.Value,
                CapMode.RaceMax => MathF.Max(raceCap, relative.Value),
                _ => MathF.Min(raceCap, relative.Value),
            };
        }

        return talent.Breaks.TryGetValue(stat, out float mult) ? rules.Clamp(rules.CapScale * x * mult + rules.CapShift) : raceCap;
    }

    /// <summary>The race's own multiplier for the stat as the original reads it: a value outside (0, 100) reads 1.</summary>
    static float GameMultiplier(RaceData? race, StatsEnumerated stat)
    {
        float v = race?.StatMultiplier(stat) ?? 1;
        return v > 0 && v < 100 ? v : 1;
    }

    /// <summary>The <c>[Race]</c> entry for the race's string id, else its name.</summary>
    static RaceOverride? RaceOverrideOf(RaceData? race, TalentConfig config)
    {
        if (race is null) return null;

        return config.Races.GetValueOrDefault(Ini.Trim(race.StringId)) ?? config.Races.GetValueOrDefault(Ini.Trim(race.Name));
    }
}

/// <summary>The last published entries (every character the scan covered) and rules, written in the serial step, read on any thread.</summary>
sealed class CapBoard
{
    volatile Published _now = new(new Dictionary<CharacterId, CapEntry>(), null);

    sealed record Published(IReadOnlyDictionary<CharacterId, CapEntry> Entries, Rules? Rules);

    public void Publish(IReadOnlyDictionary<CharacterId, CapEntry> entries, Rules? rules) => _now = new(entries, rules);

    public void Clear() => _now = new(new Dictionary<CharacterId, CapEntry>(), null);

    public CapEntry? Entry(CharacterId id) => _now.Entries.GetValueOrDefault(id);

    /// <summary>The entry and the rules it was made with, read together.</summary>
    public (CapEntry Entry, Rules Rules)? Read(CharacterId id) =>
        _now is { Rules: { } rules } now && now.Entries.GetValueOrDefault(id) is { } entry ? (entry, rules) : null;
}
