using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Data.Gameplay.Dialogue;
using Meitou.Simulation.Dialogue;

namespace Meitou.QuestsContracts;

/// <summary>
/// The talk context is written before the conversation's first line is picked, as the original writes it on <c>Dialogue::startConversation</c>
/// (event 1) and <c>startPlayerConversation</c>: a Harmony prefix on <c>Meitou.Simulation.Dialogue.DialogueSystem.Start</c>, which runs in the
/// dialogue system's serial step. A missing method leaves the variables of the last context (logged).
/// </summary>
static class ConversationPatch
{
    static Action<string> s_log = _ => { };
    static bool s_faulted;

    internal static void Install(ModContext context)
    {
        s_log = context.Log;
        var start = typeof(DialogueSystem).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            [typeof(World), typeof(CharacterId), typeof(CharacterId), typeof(int), typeof(bool), typeof(bool)]);
        if (start is null)
        {
            context.Log("DialogueSystem.Start not found: the Ledger's talk context is not written (the port needs an update)");
            return;
        }

        context.Harmony.Patch(start, prefix: new HarmonyMethod(typeof(ConversationPatch), nameof(BeforeStart)));
    }

    static void BeforeStart(World world, CharacterId npc, CharacterId player, int ev, bool playerStarted)
    {
        if (s_faulted || ev != DialogueEvents.PlayerTalkToMe && !playerStarted) return;

        try
        {
            var table = world.Characters;
            if (!table.TryResolveNext(player, out int p) || table.Cold(p) is not { IsPlayer: true }) return;
            if (!table.TryResolveNext(npc, out int n) || table.Cold(n) is not { IsPlayer: false }) return;

            LedgerSystem.Of(world)?.ConversationStarts(world, npc, player);
        }
        catch (Exception e)
        {
            s_faulted = true;
            s_log($"the Ledger's talk context stopped after an error: {e}");
        }
    }
}
