using Meitou.Simulation.Population;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Opt-in developer fixture matching the integrated sandbox hero. Ordinary new games never call this hook.</summary>
internal static class StockSandboxProfile
{
    internal const string EnvironmentName = "MEITOU_BRUTALITY_SANDBOX_LEVEL";
    internal const string DamageEnvironment = "MEITOU_BRUTALITY_SANDBOX_DAMAGE";
    static readonly ConditionalWeakTable<World, TrainingHero> heroes = new();
    sealed record TrainingHero(CharacterId Id);
    static float level, outgoing = 1;

    internal static float Outgoing(World world, CharacterId attacker) =>
        heroes.TryGetValue(world, out var hero) && attacker == hero.Id ? Volatile.Read(ref outgoing) : 1;

    internal static bool TryLevel(bool developerTools, string? requested, out float value)
    {
        value = 0;
        if (!developerTools || !float.TryParse(requested, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            || !float.IsFinite(parsed)) return false;
        value = Math.Clamp(parsed, 0, 100); return true;
    }

    public static void Install(ModContext context, bool developerTools)
    {
        if (!Install(context.Harmony, developerTools, Environment.GetEnvironmentVariable(EnvironmentName))
            || !Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)) return;
        if (TryDamage(Environment.GetEnvironmentVariable(DamageEnvironment), out float scale)) outgoing = scale;
        context.Harmony.Patch(AccessTools.Method(typeof(HitResolver), nameof(HitResolver.Resolve)),
            prefix: new HarmonyMethod(typeof(StockTrainingHook), nameof(StockTrainingHook.SandboxDamagePrefix)));
        context.AddDebugCommand("Sandbox hero melee damage (1-12)", command =>
        {
            if (!TryDamage(command.Field, out float damage)) return "Enter a damage multiplier between 1 and 12.";
            Volatile.Write(ref outgoing, damage);
            return FormattableString.Invariant($"Sandbox hero melee damage: x{damage:G}");
        }, hasField: true);

        static bool TryDamage(string? value, out float scale) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) && scale is >= 1 and <= 12;
    }

    internal static bool Install(Harmony harmony, bool developerTools, string? requested)
    {
        if (!TryLevel(developerTools, requested, out float configured)) return false;
        level = configured;
        harmony.Patch(AccessTools.Method(typeof(PopulationSystem), nameof(PopulationSystem.SpawnSandbox)),
            postfix: new HarmonyMethod(typeof(StockSandboxProfile), nameof(SpawnPostfix)));
        return true;
    }

    static void SpawnPostfix(World world, bool player, int variant, int? faction, CharacterId __result)
    {
        // The extra crowd actors and F10 spawns keep their real data. This is only the primary player sandbox spawn.
        if (!player || variant != 0 || faction is not null || !world.Characters.TryResolveNext(__result, out int slot)
            || world.Characters.Cold(slot) is not { IsPlayer: true, Stats: { } stats } hero
            || StockProgression.For(world) is not { } gameplay) return;
        for (int i = 1; i < StatsInfo.Count; i++)
        {
            var stat = (StatsEnumerated)i;
            if (StatsInfo.StorageOf(stat) == stat) stats[stat] = level;
        }
        // Like the source sandbox, raw stats are read directly by combat; strength also changes initial blood capacity.
        if (hero.Medical is { } medical && hero.Race is { } race)
            medical.Blood = MedicalState.BloodCapacity(race, stats.Strength, medical.HasStatsObject, medical.ScaleK, medical.ScaleZ);
        gameplay.SetLevel(world, __result, level);
        heroes.GetValue(world, _ => new(__result));
        Console.WriteLine(FormattableString.Invariant($"Brutality developer sandbox hero: stats={level}, brutality={level}, outgoing melee x{outgoing:G}"));
    }
}
