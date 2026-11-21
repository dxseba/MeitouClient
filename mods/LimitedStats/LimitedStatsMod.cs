using System.Collections.Frozen;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.LimitedStats;

/// <summary>
/// Caps trainable stats and scales their XP: a global cap from the settings, overridden per race and per character (by name) from the mod folder's
/// <c>limits.json</c> (<see cref="Limits"/>), as the original's ini does. The author's own race values are not shipped. The XP goes through the
/// engine's experience filter (docs/modding.md "Experience").
/// </summary>
public sealed class LimitedStatsMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new LimitedStatsSettings();
        context.AddSettings(settings);

        var limits = Limits.Load(Path.Combine(context.Folder, "limits.json"), context.Log);
        context.AddSystem("cap", _ => new StatCapSystem(settings, limits), SystemAnchor.End);
        context.Log("Limited Stats initialized");
    }
}

public sealed class LimitedStatsSettings
{
    [Setting("Stat cap", Hint = "No stat trains above this level unless limits.json sets another cap for the race or character.", Min = 1, Max = 100)]
    public int Cap { get; set; } = 100;

    [Setting("Player characters only", Hint = "Off: the caps and XP multipliers apply to every character in the world.")]
    public bool PlayerOnly { get; set; } = true;
}

/// <summary>
/// Clamps the stats in the serial slow phase every 15th tick and publishes each character's XP multipliers there; the experience filter only reads
/// the published table, so it is a pure read on any thread (combat trains in the parallel phases).
/// </summary>
sealed class StatCapSystem(LimitedStatsSettings settings, Limits limits) : IExperienceFilter
{
    static readonly StatsEnumerated[] Capped = [.. StatsInfo.RecordStats];
    static readonly int Count = Enum.GetValues<StatsEnumerated>().Max(s => (int)s) + 1;

    volatile FrozenDictionary<CharacterId, float[]> _multipliers = FrozenDictionary<CharacterId, float[]>.Empty;

    public float Gain(World world, in ExperienceGain gain)
    {
        if (!_multipliers.TryGetValue(gain.Character, out var multipliers) || (int)gain.Skill >= multipliers.Length) return gain.Amount;

        return gain.Amount * multipliers[(int)gain.Skill];
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % 15 != 0) return;

        var table = world.Characters;
        var multipliers = new Dictionary<CharacterId, float[]>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Stats: { } stats } cold) continue;
            if (settings.PlayerOnly && !cold.IsPlayer) continue;

            if (Apply(cold, stats) is { } rates) multipliers[table.IdOf(slot)] = rates;
        }

        _multipliers = multipliers.ToFrozenDictionary();
    }

    /// <summary>Clamps the character's stats to its caps; its XP multipliers, or null when no override names its race or itself.</summary>
    float[]? Apply(CharacterCold cold, CharacterStats stats)
    {
        var race = limits.Race(cold.Race);
        var own = limits.Character(cold.Name);
        foreach (var stat in Capped)
        {
            float cap = own?.CapOf(stat) ?? race?.CapOf(stat) ?? settings.Cap;
            if (stats.Stored(stat) > cap) stats[stat] = cap;
        }

        if (race is null && own is null) return null;

        var multipliers = new float[Count];
        for (int i = 0; i < Count; i++) multipliers[i] = own?.XpOf((StatsEnumerated)i) ?? race?.XpOf((StatsEnumerated)i) ?? 1;
        return multipliers;
    }
}
