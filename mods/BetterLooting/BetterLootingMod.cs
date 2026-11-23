using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace Meitou.BetterLooting;

/// <summary>
/// Port of Better Looting (workshop 3816121198, GPL-3.0): with a character's inventory window open, the looting key action loots the bodies within the
/// radius into that character by the value, weight, category and theft rules; with Alt it stores its items in the player's storage. The press travels
/// as a <see cref="SeenDiscoveriesCommand"/> tagged in a side table, one of the interface commands the paused world still applies
/// (World.ApplyPausedInterfaceCommands), so it works paused; a prefix on <c>PlayerSystem.ApplyViewCommand</c> runs it instead of the command's own
/// effect (no mod command reaches a system while paused).
/// </summary>
public sealed class BetterLootingMod : IMod
{
    internal static readonly LootSettings Settings = new();

    static readonly ConditionalWeakTable<SeenDiscoveriesCommand, LootRequest> Requests = new();

    /// <summary>
    /// The mod's log.
    /// </summary>
    static Action<string> s_log = _ => { };

    public void Initialize(ModContext context)
    {
        s_log = context.Log;
        context.AddSettings(Settings);
        context.AddSystem("looting", build => new LootSystem(build.Data), SystemAnchor.End);
        context.AddKeyAction("loot", "Loot nearby bodies (Alt: store items)", Pressed, Key.O);
        PatchCommand(context);
        context.Log("Better Looting initialized");
    }

    static void PatchCommand(ModContext context)
    {
        var apply = AccessTools.Method(typeof(PlayerSystem), "ApplyViewCommand")
            ?? throw new MissingMethodException("PlayerSystem", "ApplyViewCommand");
        context.Harmony.Patch(apply, prefix: new HarmonyMethod(typeof(BetterLootingMod), nameof(BeforeView)));
    }

    /// <summary>
    /// A press of the looting key: with the inventory window open, a request for its owner (Alt: deposit). Key actions never fire while a text field
    /// has the keys.
    /// </summary>
    static void Pressed(ModKeyContext press)
    {
        if (!Settings.Enabled || press.Frame is not { } frame || !frame.IsOpen("inventory")) return;
        if (InventoryOwner(press) is not { } lead) return;

        var request = new LootRequest(lead, press.Alt, RequiredItems.Parse(Settings.RequiredItems, s_log));
        var command = new SeenDiscoveriesCommand { Tick = press.Tick };
        Requests.Add(command, request);
        press.Enqueue(command);
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

    #pragma warning disable IDE1006 // Harmony binds __result by name
    static bool BeforeView(World world, SimCommand command, ref bool __result)
    #pragma warning restore IDE1006
    {
        if (command is not SeenDiscoveriesCommand seen || !Requests.TryGetValue(seen, out var request)) return true;

        world.Systems.OfType<LootSystem>().FirstOrDefault()?.Run(world, request);
        __result = true;
        return false;
    }
}
