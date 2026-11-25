using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Production;
using Meitou.Simulation.Weather;

namespace Meitou.CheatMenu;

/// <summary>
/// The original's engine hooks that the mod API has no filter for, as Harmony patches on Meitou's own methods (README "Harmony"):
/// <list type="bullet">
/// <item>run speed (<c>calculateMaxRunSpeed</c>): a postfix on <c>BodySystem.ActOn</c> scales the mover's top speed it just set, by Move speed
/// for the player's characters and by 3 for the rest under Fast NPCs;</item>
/// <item>production and research (<c>updateOutput</c>, <c>updateInputs</c>, <c>operate</c>): a prefix on <c>ProductionSystem.WorkStep</c> scales
/// an operator's time step at a machine by Production speed, at a research bench by Research speed (Instant research: a step long enough to
/// finish);</item>
/// <item>construction (<c>addConstructionProgress</c>): a prefix on <c>ConstructionSystem.BuildStep</c> scales the builder's time step;</item>
/// <item>weather (<c>gettingAcidRain</c> and the rest): prefixes on <c>WeatherSystem.RegionWeather</c>, <c>LocalEffects</c> and
/// <c>AcidUnderfoot</c> skip a player character under Weather immunity.</item>
/// </list>
/// Each body is wrapped: a throw is logged once and the game's own behaviour goes on.
/// </summary>
internal static class Patches
{
    /// <summary>Instant research's time factor: enough for any research to finish on the first step.</summary>
    const float Instant = 1_000_000;

    static ModContext? s_context;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        Patch(context, typeof(BodySystem), "ActOn", postfix: nameof(SpeedAfter));
        Patch(context, typeof(ProductionSystem), "WorkStep", prefix: nameof(WorkBefore));
        Patch(context, typeof(ConstructionSystem), "BuildStep", prefix: nameof(BuildBefore));
        foreach (string name in new[] { "RegionWeather", "LocalEffects", "AcidUnderfoot" })
            Patch(context, typeof(WeatherSystem), name, prefix: nameof(WeatherBefore));
    }

    static void Patch(ModContext context, Type type, string method, string? prefix = null, string? postfix = null)
    {
        if (AccessTools.Method(type, method) is not MethodInfo target)
        {
            context.LogError($"{type.Name}.{method} not found: its cheat does nothing");
            return;
        }

        context.Harmony.Patch(target, prefix: Bind(prefix), postfix: Bind(postfix));
    }

    static HarmonyMethod? Bind(string? name) => name is null ? null : new HarmonyMethod(typeof(Patches), name);

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a cheat patch failed, the game's own rule goes on: {e}");
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    /// <summary>After a body step: the top speed it set (still the delayed mover's value) times the cheat's factor.</summary>
    static void SpeedAfter(World world, int i)
    {
        try
        {
            if (CheatSystem.Of(world) is not { } cheats || world.Characters.Cold(i) is not { } cold) return;

            float factor = cold.IsPlayer ? cheats.Value(Tier.MoveSpeed) : cheats.On(Toggle.FastNpcs) ? CheatSystem.FastNpcSpeed : 1;
            ref var n = ref world.Characters.Next[i];
            if (factor != 1 && n.MaxSpeed == TopSpeedDelay.Mover(cold)) n.MaxSpeed *= factor;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void WorkBefore(World world, Machine m, ref float dt)
    {
        try
        {
            if (CheatSystem.Of(world) is not { } cheats) return;

            if (!m.Data.IsResearch) dt *= cheats.Value(Tier.Production);
            else dt *= cheats.On(Toggle.InstantResearch) ? Instant : cheats.Value(Tier.Research);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void BuildBefore(World world, ref float dt)
    {
        try
        {
            if (CheatSystem.Of(world) is { } cheats) dt *= cheats.Value(Tier.Construction);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static bool WeatherBefore(World world, CharacterCold cold)
    {
        try
        {
            return !(cold.IsPlayer && CheatSystem.Of(world) is { } cheats && cheats.On(Toggle.WeatherImmunity));
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }
#pragma warning restore IDE1006
}
