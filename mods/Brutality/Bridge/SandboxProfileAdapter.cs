using System.Globalization;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Brutality;

/// <summary>Opt-in developer fixture matching the integrated sandbox hero. Ordinary new games never call this hook.</summary>
internal static class SandboxProfileAdapter
{
    internal const string EnvironmentName = "MEITOU_BRUTALITY_SANDBOX_LEVEL";
    static float level;

    internal static bool TryLevel(bool developerTools, string? requested, out float value)
    {
        value = 0;
        if (!developerTools || !float.TryParse(requested, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            || !float.IsFinite(parsed)) return false;
        value = Math.Clamp(parsed, 0, 100); return true;
    }

    public static void Install(ModContext context, bool developerTools) =>
        Install(context.Harmony, developerTools, Environment.GetEnvironmentVariable(EnvironmentName));

    internal static bool Install(Harmony harmony, bool developerTools, string? requested)
    {
        if (!TryLevel(developerTools, requested, out float configured)) return false;
        level = configured;
        harmony.Patch(AccessTools.Method(typeof(PopulationSystem), nameof(PopulationSystem.SpawnSandbox)),
            postfix: new HarmonyMethod(typeof(SandboxProfileAdapter), nameof(SpawnPostfix)));
        return true;
    }

    static void SpawnPostfix(World world, bool player, int variant, int? faction, CharacterId __result)
    {
        // The extra crowd actors and F10 spawns keep their real data. This is only the primary player sandbox spawn.
        if (!player || variant != 0 || faction is not null || !world.Characters.TryResolveNext(__result, out int slot)
            || world.Characters.Cold(slot) is not { IsPlayer: true, Stats: { } stats } hero
            || BrutalityRuntime.For(world) is not { } gameplay) return;
        for (int i = 1; i < StatsInfo.Count; i++)
        {
            var stat = (StatsEnumerated)i;
            if (StatsInfo.StorageOf(stat) == stat) stats[stat] = level;
        }
        // Like the source sandbox, raw stats are read directly by combat; strength also changes initial blood capacity.
        if (hero.Medical is { } medical && hero.Race is { } race)
            medical.Blood = MedicalState.BloodCapacity(race, stats.Strength, medical.HasStatsObject, medical.ScaleK, medical.ScaleZ);
        gameplay.SetLevel(world, __result, level);
        Console.WriteLine($"Brutality developer sandbox hero: stats={level.ToString(CultureInfo.InvariantCulture)}, brutality={level.ToString(CultureInfo.InvariantCulture)}");
    }
}
