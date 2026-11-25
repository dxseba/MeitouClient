using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.DynamicWasteland;

/// <summary>
/// Port of Dynamic Wasteland (Workshop 3788937435): the in-game side of its geopolitical simulation. Nations (the factions that hold towns)
/// declare wars by the original's formula, send real campaigns against enemy towns, win or lose the battle when the army arrives, take towns
/// (an occupied version of the TOWN put in its place), garrison them, sign peace with truces and swear alliances; a treasury pays for it all.
/// The world panel lists nations, wars and news; the world map shows who holds each town. The original's external AI service is not used.
/// </summary>
public sealed class DynamicWastelandMod : IMod, IModDataPhase
{
    internal static readonly DwSettings Settings = new();

    /// <summary>The running world's simulation; null before a world starts.</summary>
    internal static volatile WorldSim? Running;

    /// <summary>Whether the territory picture is in the mod's folder.</summary>
    internal static bool TerritoryReady;

    public void EditData(ModDataContext context) => TownVersions.Add(context);

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        TerritoryReady = TerritoryImage.Ensure(context.Folder);
        if (!TerritoryReady) context.Log("Dynamic Wasteland: the territory picture could not be written; towns only");
        Func<string, string> tr = text => context.Tr(text);
        context.AddSystem("world", _ => Running = new WorldSim(context.Id, Settings, context.RngPurpose, tr), SystemAnchor.End);

        var panel = new DwPanel(tr, context.Enqueue, () => context.Tick);
        context.AddUi(panel);

        // The original's PanelKey is F4, which the game's speed 3 holds; Ctrl+F4 by default, rebindable on the Controls page.
        context.AddKeyAction("panel", "Dynamic Wasteland panel", _ => panel.Open = !panel.Open,
            Binding.Of(Key.F4).With(KeyModifiers.Ctrl));
        context.Log("Dynamic Wasteland initialized");
    }
}
