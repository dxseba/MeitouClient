using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.AreaGuard;

/// <summary>
/// The block record: a prefix on the private <c>CombatSystem.ApplyBlow</c> notes the blow's swing sequence (the block step does not take it), and a
/// postfix on the private <c>CombatSystem.ApplyBlocked</c> hands the block to that world's <see cref="AreaGuardSystem"/>. Both run in the serial
/// commit, on the tick thread.
/// </summary>
static class BlockHooks
{
    static readonly ConditionalWeakTable<CombatSystem, AreaGuardSystem> Systems = [];

    /// <summary>The swing sequence of the blow the tick thread is applying.</summary>
    [ThreadStatic]
    static int s_sequence;

    public static void Install(Harmony harmony)
    {
        var applyBlow = AccessTools.Method(typeof(CombatSystem), "ApplyBlow") ?? throw new MissingMethodException(nameof(CombatSystem), "ApplyBlow");
        var applyBlocked = AccessTools.Method(typeof(CombatSystem), "ApplyBlocked")
            ?? throw new MissingMethodException(nameof(CombatSystem), "ApplyBlocked");

        harmony.Patch(applyBlow, prefix: new HarmonyMethod(typeof(BlockHooks), nameof(BeforeApplyBlow)));
        harmony.Patch(applyBlocked, postfix: new HarmonyMethod(typeof(BlockHooks), nameof(AfterApplyBlocked)));
    }

    /// <summary>Ties a world's combat to its Area Guard system.</summary>
    public static void Bind(CombatSystem combat, AreaGuardSystem system) => Systems.AddOrUpdate(combat, system);

    /// <summary>A blow effect carries its swing sequence in <see cref="Effect.Amount"/>.</summary>
    static void BeforeApplyBlow(in Effect e) => s_sequence = (int)e.Amount;

    #pragma warning disable IDE1006 // Harmony binds __instance by name
    static void AfterApplyBlocked(CombatSystem __instance, World world, int a, int d, int blow)
    #pragma warning restore IDE1006
    {
        if (!Systems.TryGetValue(__instance, out var system)) return;

        var table = world.Characters;
        system.Blocked(new BlockRecord(table.IdOf(a), table.IdOf(d), s_sequence, blow, world.Tick));
    }
}
