using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The Harmony patches on Meitou's ranged combat (README "Harmony"), in place of the original's hooks on <c>GunClass::shoot</c>,
/// <c>RangedCombatClass::updateT</c> and the reload checks:
/// <list type="bullet">
/// <item>a prefix on <c>Meitou.Simulation.Combat.CombatSystem.Aim</c> (the parallel aim step, read only): a Focus holder whose spell's mana gate is
/// shut keeps aiming and does not release (the original's "mana gate hold");</item>
/// <item>a prefix and postfix on <c>Meitou.Simulation.Combat.CombatSystem.ApplyFire</c> (serial): a Missile shot is the game's bolt, then the
/// volley (<see cref="ArcaneSystem.Volley"/>); any other spell replaces the bolt with its cast (<see cref="ArcaneSystem.Cast"/>);</item>
/// <item>a postfix on <c>Meitou.Simulation.Combat.CombatSystem.Schedule</c> (serial, after the bolts flew): the spells' queued blows land as bolt
/// hits (<see cref="ArcaneSystem.ApplyHits"/>).</item>
/// </list>
/// Every body is wrapped: a failure logs once and leaves the vanilla shot.
/// </summary>
static class FocusPatches
{
    static ModContext? s_context;
    static int s_failed;

    public readonly record struct Shot(ArcaneSystem? System, int Before);

    public static bool Install(ModContext context)
    {
        s_context = context;
        const string combat = "Meitou.Simulation.Combat.CombatSystem";
        var aim = AccessTools.Method($"{combat}:Aim");
        var fire = AccessTools.Method($"{combat}:ApplyFire");
        var schedule = AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Schedule), [typeof(World)]);
        if (aim is null || fire is null || schedule is null)
        {
            context.LogError($"{combat}.Aim, ApplyFire or Schedule not found: the Focus shoots plain bolts (the port needs an update)");
            return false;
        }

        if (!BoltAccess.Bind(out string missing))
        {
            context.LogError($"{missing} not found: the Focus shoots plain bolts (the port needs an update)");
            return false;
        }

        context.Harmony.Patch(aim, prefix: new HarmonyMethod(typeof(FocusPatches), nameof(AimPrefix)));
        context.Harmony.Patch(fire, prefix: new HarmonyMethod(typeof(FocusPatches), nameof(FirePrefix)),
            postfix: new HarmonyMethod(typeof(FocusPatches), nameof(FirePostfix)));
        context.Harmony.Patch(schedule, postfix: new HarmonyMethod(typeof(FocusPatches), nameof(SchedulePostfix)));
        return true;
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool AimPrefix(CombatSystem __instance, World world, int i, int t, CharacterCold cold)
    {
        if (s_failed != 0 || !ArcaneSystem.HoldsFocus(cold)) return true;

        try
        {
            return ArcaneSystem.Of(__instance) is not { } system || system.MayRelease(world, i, cold, t);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
            return true;
        }
    }

    static bool FirePrefix(CombatSystem __instance, World world, in Effect e, out Shot __state)
    {
        __state = default;
        if (s_failed != 0) return true;

        try
        {
            return Fire(__instance, world, e.Source, e.Target, out __state);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed(ex);
            return true;
        }
    }

    static void FirePostfix(CombatSystem __instance, World world, in Effect e, Shot __state)
    {
        if (__state.System is not { } system) return;

        try
        {
            int added = BoltAccess.Count(__instance) - __state.Before;
            if (added > 0 && world.Characters.Cold(e.Source) is { } cold) system.Volley(world, __instance, e.Source, cold, __state.Before, added);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed(ex);
        }
    }

    static void SchedulePostfix(CombatSystem __instance, World world)
    {
        if (s_failed != 0) return;

        try
        {
            ArcaneSystem.Of(__instance)?.ApplyHits(world, __instance);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed(ex);
        }
    }
#pragma warning restore IDE1006

    /// <summary>
    /// A Focus shot: the Missile lets the game's bolt go and notes the list's length for the volley; another spell is cast instead of the bolt
    /// (false skips the game's shot). A holder whose gate shut since its aim releases nothing.
    /// </summary>
    static bool Fire(CombatSystem combat, World world, int source, int target, out Shot shot)
    {
        shot = default;
        var cold = world.Characters.Cold(source);
        if (!ArcaneSystem.HoldsFocus(cold) || ArcaneSystem.Of(combat) is not { } system) return true;

        var caster = system.Find(world.Characters.IdOf(source));
        var spell = system.SpellFor(world, source, cold!, caster, target);
        if (spell != Spell.Missile)
        {
            system.Cast(world, source, target, cold!, spell);
            return false;
        }

        if (!system.Gate(world, source, cold!, caster, Spell.Missile, target)) return false;

        shot = new Shot(system, BoltAccess.Count(combat));
        return true;
    }

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"Arcane Focus step failed, plain bolts from now on ({e.GetType().Name}: {e.Message})");
    }
}
