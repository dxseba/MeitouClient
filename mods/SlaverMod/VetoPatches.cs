using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Kidnap;

namespace Meitou.SlaverMod;

/// <summary>
/// The original's vetoes on the game's own choices (it hooks the AI slave finders, <c>isEnemy</c> and <c>GunClassTurret::scoreTarget</c>), as
/// Harmony postfixes on Meitou's methods; each reads only the settled tick and the settings:
/// <list type="bullet">
/// <item><c>Meitou.Simulation.Kidnap.CaptiveSystem.FreeSlaveToProcess</c> and <c>CaptiveSystem.ProcessTarget</c>: another faction's slavers never
/// pick the player's slaves to process (<c>ProtectSlaves</c>);</item>
/// <item><c>Meitou.Simulation.Combat.CombatSystem.ChooseTurretTarget</c>: a turret of a faction not at war with the player never picks them
/// (<c>SparedByNeutrals</c>);</item>
/// <item><c>Meitou.Simulation.Dialogue.DialogueSystem.RecruitRefusal</c>: rebels never rejoin through dialogue (<c>RebelsDistrust</c>);</item>
/// <item><c>Meitou.Simulation.Dialogue.DialogueSystem.RunActions</c> (prefix): a line that both enslaves the carried person (DA 40) and pays the
/// player (DA 9), the traders' fixed offer for someone carried, does nothing (<c>DisableCarriedSale</c>).</item>
/// </list>
/// A method that is not found leaves its veto out (logged); a throw is logged once and the game's answer kept.
/// </summary>
static class VetoPatches
{
    static Action<string> s_log = _ => { };
    static bool s_faulted;

    internal static void Install(ModContext context)
    {
        s_log = context.Log;
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Patch(context, typeof(CaptiveSystem).GetMethod("FreeSlaveToProcess", any), nameof(AfterFreeSlave));
        Patch(context, typeof(CaptiveSystem).GetMethod("ProcessTarget", any), nameof(AfterProcessTarget));
        Patch(context, typeof(CombatSystem).GetMethod("ChooseTurretTarget", any), nameof(AfterTurret));
        Patch(context, typeof(DialogueSystem).GetMethod("RecruitRefusal", any), nameof(AfterRecruit));
        var actions = typeof(DialogueSystem).GetMethod("RunActions", any);
        if (actions is not null) context.Harmony.Patch(actions, prefix: new HarmonyMethod(typeof(VetoPatches), nameof(BeforeActions)));
        else context.Log("DialogueSystem.RunActions not found: traders still buy carried people (the port needs an update)");
    }

    static void Patch(ModContext context, MethodInfo? method, string postfix)
    {
        if (method is null)
        {
            context.Log($"{postfix}: the game method is not found; that veto is off (the port needs an update)");
            return;
        }

        context.Harmony.Patch(method, postfix: new HarmonyMethod(typeof(VetoPatches), postfix));
    }

    static bool Protects(World world, int slot, CharacterCold looker)
    {
        if (SlaverSystem.Running is not { Settings: { Enabled: true, ProtectSlaves: true } }) return false;

        return !looker.IsPlayer && SlaverSystem.PlayerSlave(world.Characters.Cold(slot)) && looker.Faction != world.Player.Faction;
    }

#pragma warning disable IDE1006 // Harmony injects the result by this name
    static void AfterFreeSlave(World world, CharacterCold me, ref int __result)
    {
        if (s_faulted || __result < 0) return;

        try
        {
            if (Protects(world, __result, me)) __result = -1;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void AfterProcessTarget(World world, CharacterCold cold, ref Simulation.Locks.Fixture? __result)
    {
        if (s_faulted || __result is not { } cage) return;

        try
        {
            var table = world.Characters;
            var spot = world.Fixtures.CageSpots(table).FirstOrDefault(c => c.Id == cage.Id);
            if (spot is { Occupant.IsNone: false } && table.TryResolveNext(spot.Occupant, out int s) && Protects(world, s, cold)) __result = null;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void AfterTurret(World world, CharacterCold gc, ref int __result)
    {
        if (s_faulted || __result < 0 || SlaverSystem.Running is not { Settings: { Enabled: true, SparedByNeutrals: true } } running) return;

        try
        {
            if (!SlaverSystem.PlayerSlave(world.Characters.Cold(__result)) || gc.IsPlayer || gc.Faction == world.Player.Faction) return;

            if (!AggroSystem.IsEnemy(running.Relations, world.Player.Faction, gc.Faction)) __result = -1;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void AfterRecruit(World world, CharacterId id, ref string? __result)
    {
        if (s_faulted || __result is not null) return;

        try
        {
            if (SlaverSystem.Running is { Settings.Enabled: true } running && running.IsRebel(world, id))
                __result = "They will never trust you again.";
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    const int GiveMoney = 9;

#pragma warning disable IDE1006 // Harmony injects the result by this name
    static bool BeforeActions(Data.Gameplay.Dialogue.DialogueNode line, CharacterCold? tc, ref bool __result)
    {
        if (s_faulted || SlaverSystem.Running is not { Settings: { Enabled: true, DisableCarriedSale: true } }) return true;

        try
        {
            bool sale = line.Effects.Any(e => e.Action == Data.Gameplay.Dialogue.DialogueActions.EnslaveTargetsCarriedPerson)
                && line.Effects.Any(e => e.Action == GiveMoney);
            if (!sale || tc is not { Carrying.IsNone: false }) return true;

            __result = true;
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }
#pragma warning restore IDE1006

    static void Fault(Exception e)
    {
        s_faulted = true;
        s_log($"the slave protection stopped after an error: {e.Message}");
    }
}
