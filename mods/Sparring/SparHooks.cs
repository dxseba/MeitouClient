using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Sparring;

/// <summary>
/// Keeps a spar one on one: a Harmony prefix on the private <c>CombatSystem.Engage</c> (every fight start: a defence, a protect goal, a squad's
/// join, an attack job, and the player's queued pick) refuses a fight on a sparring character by one of its own faction other than its partner,
/// unless the player picked it. The engine's join finders already pass over a target of the joiner's faction; this holds should one of them not.
/// </summary>
static class SparHooks
{
    static readonly ConditionalWeakTable<World, SparSystem> Systems = [];

    public static void Install(Harmony harmony)
    {
        var engage = AccessTools.Method(typeof(CombatSystem), "Engage") ?? throw new MissingMethodException(nameof(CombatSystem), "Engage");
        harmony.Patch(engage, prefix: new HarmonyMethod(typeof(SparHooks), nameof(BeforeEngage)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result by name
    static bool BeforeEngage(World world, CharacterId attacker, AttackOrder order, ref bool __result)
    #pragma warning restore IDE1006
    {
        if (!SparringMod.Settings.Enabled || order.Picked || SystemOf(world) is not { } spars) return true;
        if (!spars.TryPartner(order.Target, out var partner) || partner == attacker || !SameFaction(world, attacker, order.Target)) return true;

        __result = false;
        return false;
    }

    static bool SameFaction(World world, CharacterId a, CharacterId b)
    {
        var table = world.Characters;
        return table.TryResolveNext(a, out int x) && table.TryResolveNext(b, out int y) && table.Cold(x)?.Faction == table.Cold(y)?.Faction;
    }

    static SparSystem? SystemOf(World world)
    {
        if (Systems.TryGetValue(world, out var known)) return known;

        if (world.Systems.OfType<SparSystem>().FirstOrDefault() is not { } found) return null;

        Systems.AddOrUpdate(world, found);
        return found;
    }
}
