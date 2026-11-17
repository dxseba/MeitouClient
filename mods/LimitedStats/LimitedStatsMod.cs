using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.LimitedStats;

/// <summary>
/// Caps trainable stats and scales their XP: a global cap from the settings, overridden per race and per character (by name) from the mod folder's
/// <c>limits.json</c> (<see cref="Limits"/>), as the original's ini does. The author's own race values are not shipped.
/// </summary>
public sealed class LimitedStatsMod : IMod
{
    internal static readonly ConditionalWeakTable<CharacterStats, float[]> XpMultipliers = new();

    public void Initialize(ModContext context)
    {
        var settings = new LimitedStatsSettings();
        context.AddSettings(settings);

        var limits = Limits.Load(Path.Combine(context.Folder, "limits.json"), context.Log);
        context.AddSystem("cap", _ => new StatCapSystem(settings, limits), SystemAnchor.End);

        var gain = AccessTools.Method(typeof(CharacterStats), nameof(CharacterStats.Gain)) ?? throw new MissingMethodException(nameof(CharacterStats), "Gain");
        context.Harmony.Patch(gain, prefix: new HarmonyMethod(typeof(LimitedStatsMod), nameof(BeforeGain)));
        context.Log("Limited Stats initialized");
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    /// <summary>Scales a gain by the character's multiplier for the stat (1 when none is set).</summary>
    static void BeforeGain(CharacterStats __instance, StatsEnumerated stat, ref float amount)
    #pragma warning restore IDE1006
    {
        if (XpMultipliers.TryGetValue(__instance, out var multipliers) && (int)stat < multipliers.Length) amount *= multipliers[(int)stat];
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
/// Clamps the stats in the serial slow phase every 15th tick and refreshes each character's XP multipliers: combat reads other characters' stats
/// in its parallel phases, so they are not written there (docs/modding.md "Determinism contract").
/// </summary>
sealed class StatCapSystem(LimitedStatsSettings settings, Limits limits) : ITickSystem
{
    static readonly StatsEnumerated[] Capped = [.. StatsInfo.RecordStats];
    static readonly int Count = Enum.GetValues<StatsEnumerated>().Max(s => (int)s) + 1;

    public void SlowWorld(World world)
    {
        if (world.Tick % 15 != 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Stats: { } stats } cold) continue;
            if (settings.PlayerOnly && !cold.IsPlayer)
            {
                LimitedStatsMod.XpMultipliers.Remove(stats);
                continue;
            }

            Apply(cold, stats);
        }
    }

    void Apply(CharacterCold cold, CharacterStats stats)
    {
        var race = limits.Race(cold.Race);
        var own = limits.Character(cold.Name);
        foreach (var stat in Capped)
        {
            float cap = own?.CapOf(stat) ?? race?.CapOf(stat) ?? settings.Cap;
            if (stats[stat] > cap) stats[stat] = cap;
        }

        if (race is null && own is null)
        {
            LimitedStatsMod.XpMultipliers.Remove(stats);
            return;
        }

        var multipliers = new float[Count];
        for (int i = 0; i < Count; i++) multipliers[i] = own?.XpOf((StatsEnumerated)i) ?? race?.XpOf((StatsEnumerated)i) ?? 1;
        LimitedStatsMod.XpMultipliers.AddOrUpdate(stats, multipliers);
    }
}
