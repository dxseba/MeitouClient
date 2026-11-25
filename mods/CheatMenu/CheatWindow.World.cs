using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>
/// The World page's Unlock doors and Next weather, the Experimental page's town, season, free camera and level editor buttons, and Restock
/// selected trader (its button is on the Selected page).
/// </summary>
internal sealed partial class CheatWindow
{
    /// <summary>Rows added at the end of the World page.</summary>
    IEnumerable<UiNode> WorldRows()
    {
        yield return Do("Next weather here");
        yield return Do("Unlock doors near camera");
    }

    /// <summary>Rows added at the end of the Experimental page.</summary>
    IEnumerable<UiNode> ExperimentalRows()
    {
        yield return Do("Level editor (unstable)");
        string box = HostBridge.FreeCamera ? "[X] " : "[  ] ";
        yield return Kit.Button("cheat-act-Free camera", box + Tr("Free camera"), _styles.Button, _actions.Act, "Free camera");
        yield return Do("Next season here");
        yield return Head("Towns and buildings");
        yield return Do("Take over town of selected");
        yield return Do("Town alarm on (selected)");
        yield return Do("Town alarm off (selected)");
    }

    /// <summary>A button of these rows by its English text; false when it is not one of them.</summary>
    bool WorldAct(string english)
    {
        switch (english)
        {
            case "Next weather here": Say(HostBridge.NextWeather() is { } w ? Tr("CheatMenu: weather ") + w : Tr("not changed")); break;
            case "Next season here": Say(HostBridge.NextSeason(_camera) is { } s ? Tr("CheatMenu: season ") + s : Tr("not changed")); break;
            case "Free camera": Say(HostBridge.ToggleFreeCamera() ? Tr("Free camera") : Tr("not changed")); break;
            case "Level editor (unstable)": Say(HostBridge.OpenLevelEditor() ? Tr("Level editor (unstable)") : Tr("not changed")); break;
            case "Unlock doors near camera": SendWorld(new UnlockDoorsNear(_camera) { Tick = T }, english); break;
            case "Restock selected trader":
                OnNpc(id => new RestockTrader(id) { Tick = T }, english, "CheatMenu: restock: click a trader first");
                break;
            case "Take over town of selected": OnFirst(id => new TakeOverTown(id) { Tick = T }, english); break;
            case "Town alarm on (selected)": OnFirst(id => new SetTownAlarm(id, true) { Tick = T }, english); break;
            case "Town alarm off (selected)": OnFirst(id => new SetTownAlarm(id, false) { Tick = T }, english); break;
            default: return false;
        }

        return true;
    }

    void SendWorld(SimCommand command, string english)
    {
        Send(command);
        Say(Tr(english));
    }

    /// <summary>The command for the first selected (else looked-at) character.</summary>
    void OnFirst(Func<CharacterId, SimCommand> make, string english)
    {
        if (Target.Count == 0)
        {
            Say(Tr("Skills: select a character"));
            return;
        }

        SendWorld(make(Target[0]), english);
    }

    /// <summary>The command for the NPC the player clicked (selected or looked at).</summary>
    void OnNpc(Func<CharacterId, SimCommand> make, string english, string none)
    {
        var npc = _snapshot?.Characters.FirstOrDefault(c => !c.IsPlayer && (c.Selected || c.Inspected));
        if (npc is null)
        {
            Say(Tr(none));
            return;
        }

        SendWorld(make(npc.Id), english);
    }
}
