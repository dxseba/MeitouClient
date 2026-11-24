using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.BetterLooting;

/// <summary>
/// Port of Better Looting (workshop 3816121198, GPL-3.0): with a character's inventory window open, the looting key action loots the bodies within the
/// radius into that character by the value, weight, category and theft rules; with Alt it stores its items in the player's storage. The press is a
/// <see cref="LootRequest"/>, a paused command, so <see cref="LootSystem"/> runs it between ticks while paused too.
/// </summary>
public sealed class BetterLootingMod : IMod
{
    internal static readonly LootSettings Settings = new();

    /// <summary>
    /// The mod's log.
    /// </summary>
    static Action<string> s_log = _ => { };

    /// <summary>
    /// A line in the mod log when <see cref="LootSettings.DebugLogging"/> is on (the original's "Write every looting step"); the text is built
    /// only then.
    /// </summary>
    internal static void Trace(Func<string> line)
    {
        if (Settings.DebugLogging) s_log("BetterLooting: " + line());
    }

    public void Initialize(ModContext context)
    {
        s_log = context.Log;
        context.AddSettings(Settings);
        context.AddSystem("looting", build => new LootSystem(build.Data), SystemAnchor.End);
        context.AddKeyAction("loot", "Loot nearby bodies (Alt: store items)", Pressed, Key.O);
        context.Log("Better Looting initialized");
    }

    /// <summary>
    /// A press of the looting key: with the inventory window open, a request for its owner (Alt: deposit). Key actions never fire while a text field
    /// has the keys.
    /// </summary>
    static void Pressed(ModKeyContext press)
    {
        if (!Settings.Enabled || press.Frame is not { } frame || !frame.IsOpen("inventory")) return;
        if (InventoryOwner(press) is not { } lead) return;

        press.Enqueue(new LootRequest(lead, press.Alt, RequiredItems.Parse(Settings.RequiredItems, s_log)) { Tick = press.Tick });
    }

    /// <summary>
    /// The character the inventory window shows, as the interface picks it: the selected character who opened the loot or container window, else the
    /// first selected; only one of the player's.
    /// </summary>
    static CharacterId? InventoryOwner(ModKeyContext press)
    {
        var snapshot = press.Snapshot;
        var opener = snapshot.OpenLoot.IsNone ? snapshot.ContainerOpener : snapshot.LootOpener;
        var id = press.Selected.Contains(opener) ? opener : press.Selected.FirstOrDefault(CharacterId.None);
        if (id.IsNone) return null;

        return snapshot.Characters.FirstOrDefault(c => c.Id == id) is { IsPlayer: true } owner ? owner.Id : null;
    }
}
