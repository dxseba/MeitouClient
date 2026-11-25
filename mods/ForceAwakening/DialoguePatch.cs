using HarmonyLib;
using Meitou.Data.Gameplay.Dialogue;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;

namespace Meitou.ForceAwakening;

/// <summary>
/// The original's text conditions and actions on dialogue lines (its <c>DialogLineData::checkConditions</c> and <c>Dialogue::say</c> hooks):
/// a line whose string field <c>arc_bounty</c> is <c>none</c> / <c>has</c> passes only without / with a held contract, and a line with
/// <c>arc_open</c> said by the NPC side runs <c>bounty_new</c> or <c>bounty_drop</c> (<see cref="ForceSystem.BountyAction"/>). Harmony: a postfix
/// on <c>Meitou.Simulation.Dialogue.DialogueSystem.LastChecksPass</c> (the line check's last tests, also asked for previews: a pure read) and a prefix
/// on <c>Meitou.Simulation.Dialogue.DialogueSystem.Say</c> (the NPC side says a line, in the dialogue's serial step). Lines without these fields are
/// untouched. A failure logs once and leaves the vanilla answer.
/// </summary>
static class DialoguePatch
{
    static ModContext? s_context;
    static int s_failed;

    public static void Install(ModContext context)
    {
        s_context = context;
        var check = AccessTools.Method(typeof(DialogueSystem), "LastChecksPass");
        var say = LineSay();
        if (check is null || say is null)
        {
            context.LogError("DialogueSystem.LastChecksPass or Say not found: the bounty contracts are off (the port needs an update)");
            return;
        }

        context.Harmony.Patch(check, postfix: new HarmonyMethod(typeof(DialoguePatch), nameof(CheckPostfix)));
        context.Harmony.Patch(say, prefix: new HarmonyMethod(typeof(DialoguePatch), nameof(SayPrefix)));
    }

    /// <summary>The conversation overload of <c>DialogueSystem.Say</c> (the one taking the spoken line), not the ambient bark one.</summary>
    static System.Reflection.MethodInfo? LineSay() =>
        AccessTools.GetDeclaredMethods(typeof(DialogueSystem))
            .FirstOrDefault(m => m.Name == "Say" && m.GetParameters().Any(p => p.ParameterType == typeof(DialogueNode)));

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void CheckPostfix(DialogueNode node, ref bool __result)
    {
        if (!__result) return;

        try
        {
            string condition = node.Record.GetString("arc_bounty");
            if (condition.Length > 0 && ForceAwakeningMod.Running is { } system) __result = system.BountyLinePasses(condition);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void SayPrefix(World world, DialogueNode line)
    {
        try
        {
            string action = line.Record.GetString("arc_open");
            if (action.Length > 0) ForceAwakeningMod.Running?.BountyAction(world, action);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"dialogue hook failed ({e.GetType().Name}: {e.Message})");
    }
}
