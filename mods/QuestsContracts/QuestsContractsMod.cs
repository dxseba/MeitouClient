using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.QuestsContracts;

/// <summary>
/// Port of Quests &amp; Contracts (Workshop 3797459646, its plugin Ledger.dll and BFrizz's Extra Extensions variables it needs): barmen's
/// delivery, package, debt and thug contracts, the law desk's posters, reputation per NPC, town and faction, and the Ledger (J). The original's
/// QuestsAndContracts.mod carries the dialogue; this mod rebuilds the variable tests and signal lines on Meitou's dialogue codes
/// (<see cref="DataBridge"/>) and keeps the numbers (<see cref="LedgerSystem"/>). README "Facts" says what follows the original.
/// </summary>
public sealed class QuestsContractsMod : IMod, IModDataPhase
{
    static LedgerTables? s_tables;

    public void EditData(ModDataContext context)
    {
        if (OriginalFiles.Folder(context.Folder) is not { } folder)
        {
            context.Log($"the original (Workshop {OriginalFiles.Item}) is not installed: nothing to run");
            return;
        }

        s_tables = LedgerTables.Read(folder);
        DataBridge.Apply(context, s_tables);
    }

    public void Initialize(ModContext context)
    {
        if (s_tables is not { } tables) return;

        for (int i = 0; i < tables.Variables.Count; i++)
        {
            int index = i;
            context.AddDialogueCondition(DataBridge.VariableCode + i, check => check.Compare(LedgerSystem.Of(check.World)?.Variable(index) ?? 0));
        }

        context.AddDialogueEffect(DataBridge.SignalCode, Signal);
        context.AddSystem("ledger", _ => new LedgerSystem(tables, context.Population, context.RngPurpose, context.Log), SystemAnchor.End);
        ConversationPatch.Install(context);
        BountyPatch.Install(context);
        PickpocketPatch.Install(context);
        context.AddKeyAction("ledger", "Open the Ledger", _ => LedgerWindow.Toggle(), Binding.Of(Key.J));
        context.AddUi(new LedgerWindow(context));

        var overlay = new LedgerOverlay(context, tables);
        overlay.Bind(context.AddKeyAction("overlay", "Ledger overlay (hold)", _ => { }, Binding.Of(Key.H)));
        context.AddKeyAction("readout", "Ledger readout", _ => overlay.ToggleReadout());
        context.AddUi(overlay);
        context.AddFrame(overlay.Frame);
        context.Log($"Quests & Contracts: {tables.Variables.Count} variables, {tables.Signals.Count} signals");
    }

    static void Signal(DialogueAct act) => LedgerSystem.Of(act.World)?.Signalled(act.World, act.Effect.Value, act.Speaker, act.Target);
}
