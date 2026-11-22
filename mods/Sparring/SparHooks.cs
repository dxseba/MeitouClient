using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Sparring;

/// <summary>
/// Keeps a spar one on one, as the original's <c>isEnemy</c> / <c>isAlly</c> hooks do (a sparring character is nobody's enemy but its partner's,
/// and takes nobody else for one): a Harmony prefix on the private <c>CombatSystem.Engage</c> (every fight start: a defence, a protect goal, a
/// squad's join, an attack job, and the player's queued pick) refuses a fight between a sparring character and anyone but its partner, unless the
/// player picked it.
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
        if (!Outside(spars, attacker, order.Target)) return true;

        __result = false;
        return false;
    }

    /// <summary>
    /// Whether a fight of <paramref name="attacker"/> on <paramref name="target"/> involves a sparring character and is not the spar itself.
    /// </summary>
    static bool Outside(SparSystem spars, CharacterId attacker, CharacterId target)
    {
        if (spars.TryPartner(target, out var partner) && partner != attacker) return true;

        return spars.TryPartner(attacker, out var own) && own != target;
    }

    static SparSystem? SystemOf(World world)
    {
        if (Systems.TryGetValue(world, out var known)) return known;

        if (world.Systems.OfType<SparSystem>().FirstOrDefault() is not { } found) return null;

        Systems.AddOrUpdate(world, found);
        return found;
    }
}
