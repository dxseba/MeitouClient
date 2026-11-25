using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;
using Meitou.Simulation.Production;

namespace Meitou.CheatMenu;

/// <summary>
/// Harmony patches of the base and research cheats (README "Harmony"):
/// <list type="bullet">
/// <item>Free research: a prefix on <c>ProductionSystem.Queue</c> marks the research paid before the game's cost test and payment (the original
/// makes its two research cost checks pass), a postfix takes the mark back when the research was not queued after all; a postfix on
/// <c>ProductionSystem.Affordable</c> makes the research window show every research as affordable;</item>
/// <item>Generators: a prefix on <c>ProductionSystem.BurnFuel</c> skips the player's generators (the original's fuel rate 0), a postfix on
/// <c>ProductionSystem.Output</c> gives them at least their rated output (the original's power output at its maximum).</item>
/// </list>
/// Each body is wrapped: a throw is logged once and the game's own rule goes on.
/// </summary>
internal static class BasePatches
{
    static ModContext? s_context;
    static int s_faulted;
    static FieldInfo? s_paid;

    public static void Install(ModContext context)
    {
        s_context = context;
        s_paid = AccessTools.Field(typeof(ResearchState), "_paid");
        if (s_paid is null) context.LogError("ResearchState._paid not found: Free research does nothing");

        Patch(context, "Queue", prefix: nameof(QueueBefore), postfix: nameof(QueueAfter));
        Patch(context, "Affordable", postfix: nameof(AffordableAfter));
        Patch(context, "BurnFuel", prefix: nameof(BurnBefore));
        Patch(context, "Output", postfix: nameof(OutputAfter));
    }

    static void Patch(ModContext context, string method, string? prefix = null, string? postfix = null)
    {
        if (AccessTools.Method(typeof(ProductionSystem), method) is not MethodInfo target)
        {
            context.LogError($"ProductionSystem.{method} not found: its cheat does nothing");
            return;
        }

        context.Harmony.Patch(target, prefix: Bind(prefix), postfix: Bind(postfix));
    }

    static HarmonyMethod? Bind(string? name) => name is null ? null : new HarmonyMethod(typeof(BasePatches), name);

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a base cheat patch failed, the game's own rule goes on: {e}");
    }

    static bool On(World world, Toggle which) => CheatSystem.Of(world) is { } cheats && cheats.On(which);

    /// <summary>Whether the machine stands in a complete building of the player's faction.</summary>
    static bool Own(World world, Machine m) => world.Buildings.Find(m.Site) is { Complete: true } site && site.Owner == world.Player.Faction;

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void QueueBefore(World world, string id, out bool __state)
    {
        __state = false;
        try
        {
            if (!On(world, Toggle.FreeResearch) || s_paid?.GetValue(world.Research) is not List<string> paid) return;
            if (paid.Contains(id, StringComparer.Ordinal)) return;

            paid.Add(id);
            __state = true;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void QueueAfter(World world, string id, bool __state)
    {
        try
        {
            if (!__state || world.Research.IsQueued(id) || s_paid?.GetValue(world.Research) is not List<string> paid) return;

            paid.Remove(id);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void AffordableAfter(World world, ref bool __result)
    {
        try
        {
            if (!__result && On(world, Toggle.FreeResearch)) __result = true;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static bool BurnBefore(World world, Machine m)
    {
        try
        {
            return !(On(world, Toggle.Generators) && Own(world, m));
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    static void OutputAfter(World world, Machine m, ref float __result)
    {
        try
        {
            if (On(world, Toggle.Generators) && m.Data.IsGenerator && Own(world, m)) __result = MathF.Max(__result, m.Data.PowerOutput);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006
}

/// <summary>
/// The game's own steps the base cheats call by reflection (no patch): a farm's catch-up (<c>ProductionSystem.TimeSkip</c>, internal) and a town
/// building's ruin state (<c>FixtureRegistry.SetRuin</c>, private, the step a town override uses), so seats, items, sight and drawing follow as in
/// the game. Serial steps only.
/// </summary>
internal static class BaseWorld
{
    static readonly MethodInfo? TimeSkipMethod =
        AccessTools.Method(typeof(ProductionSystem), "TimeSkip", [typeof(World), typeof(Machine), typeof(float), typeof(bool)]);

    static readonly MethodInfo? SetRuinMethod = AccessTools.Method(typeof(FixtureRegistry), "SetRuin",
        [typeof(World), typeof(PopulationData), typeof(int), typeof(TownBuilding), typeof(bool)]);

    /// <summary>The farm skips <paramref name="hours"/> with water ignored, as the original's <c>timeSkip(farm, hours, true)</c>.</summary>
    internal static bool TimeSkip(ProductionSystem production, World world, Machine m, float hours)
    {
        if (TimeSkipMethod is null) return false;

        TimeSkipMethod.Invoke(production, [world, m, hours, true]);
        return true;
    }

    /// <summary>
    /// Ruins (or makes whole) every building within <paramref name="radius"/> of the point: the player's built sites (a ruin is a destroyed site,
    /// which the game turns into a half-built ruin at its first touch; whole is complete again) and the towns' buildings (walls and gates are never
    /// ruined). Returns how many changed.
    /// </summary>
    internal static int SetRuins(World world, PopulationData data, Vector3 at, float radius, bool ruin) =>
        Sites(world, at, radius, ruin) + TownBuildings(world, data, at, radius, ruin);

    static int Sites(World world, Vector3 at, float radius, bool ruin)
    {
        var construction = world.Systems.OfType<ConstructionSystem>().FirstOrDefault();
        int changed = 0;
        foreach (var site in world.Buildings.All.ToList())
        {
            if (Vector3.Distance(site.Position, at) > radius) continue;

            if (ruin && site is { Complete: true, Destroyed: false })
            {
                site.Destroyed = true;
                changed++;
            }
            else if (!ruin && (site.Destroyed || site.Ruined) && construction is not null)
            {
                site.Destroyed = site.Ruined = false;
                site.Progress = site.Target;
                if (site.Complete || Finisher.Finish(world, construction, site)) changed++;
            }
        }

        if (changed > 0) world.Buildings.Changed();
        return changed;
    }

    static int TownBuildings(World world, PopulationData data, Vector3 at, float radius, bool ruin)
    {
        if (SetRuinMethod is null) return 0;

        int changed = 0;
        foreach (var site in data.Sites)
        {
            foreach (var b in site.Buildings.ToList())
            {
                if (Vector3.Distance(b.Position, at) > radius || world.Fixtures.IsDestroyed(b) == ruin) continue;
                if (ruin && (b.IsWall || b.IsGate)) continue;

                SetRuinMethod.Invoke(world.Fixtures, [world, data, site.Index, b, ruin]);
                changed++;
            }
        }

        return changed;
    }
}
