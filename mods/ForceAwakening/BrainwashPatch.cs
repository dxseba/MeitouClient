using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace Meitou.ForceAwakening;

/// <summary>
/// Brainwash to another faction (the grimoire's faction button, the original's <c>brainwash_faction</c> list) through the game's own paths: the
/// target joins the caster's squad (<c>PopulationSystem.JoinPlayerSquad</c>), then is dismissed as the squad window's Dismiss does
/// (<c>Meitou.Simulation.Factions.SquadManagement.Dismiss</c>, by reflection), which makes it a squad of its own of the dismiss faction; for the
/// length of that one call a Harmony prefix on <c>Meitou.Simulation.Population.PopulationData.FactionIndex</c> answers the chosen faction for
/// the dismiss faction's id (<c>SquadManagement.DismissFaction</c>), so the new squad is the chosen faction's. The prefix acts only inside that
/// serial call (no parallel phase runs then); everywhere else it passes. A failure logs once and leaves the target where the game put it.
/// </summary>
static class BrainwashPatch
{
    static MethodInfo? s_dismiss;
    static int s_redirect = -1;
    static ModContext? s_context;

    public static void Install(ModContext context)
    {
        s_context = context;
        s_dismiss = AccessTools.Method(typeof(SquadManagement), "Dismiss", [typeof(World), typeof(PopulationData), typeof(CharacterId)]);
        var index = AccessTools.Method(typeof(PopulationData), nameof(PopulationData.FactionIndex), [typeof(string)]);
        if (s_dismiss is null || index is null)
        {
            context.LogError("SquadManagement.Dismiss or PopulationData.FactionIndex not found: Brainwash only to your own faction");
            s_dismiss = null;
            return;
        }

        context.Harmony.Patch(index, prefix: new HarmonyMethod(typeof(BrainwashPatch), nameof(Prefix)));
    }

    /// <summary>Moves a character that just joined the player into a squad of its own of <paramref name="faction"/>; false when it did not.</summary>
    public static bool Send(World world, PopulationData data, CharacterId who, int faction)
    {
        if (s_dismiss is null || faction < 0) return false;

        try
        {
            s_redirect = faction;
            s_dismiss.Invoke(null, [world, data, who]);
        }
        catch (TargetInvocationException e)
        {
            s_context?.LogError($"brainwash to another faction failed: {e.InnerException?.Message}");
        }
        finally
        {
            s_redirect = -1;
        }

        return world.Characters.TryResolveNext(who, out int slot) && world.Characters.Cold(slot) is { IsPlayer: false } cold && cold.Faction == faction;
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool Prefix(string id, ref int __result)
    {
        if (s_redirect < 0 || id != SquadManagement.DismissFaction) return true;

        __result = s_redirect;
        return false;
    }
#pragma warning restore IDE1006
}
