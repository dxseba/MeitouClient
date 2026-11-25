using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>
/// Port of Kenshi Cheat Menu 1.11.2 (Kandro78, Workshop 3833747223): a window opened with F8 (rebindable, also from the window) holding the
/// original's cheats by category. Toggles and multipliers are the cheat system's (<see cref="CheatSystem"/>), most one-shot buttons the game's
/// own debug commands; the original's engine hooks are filters of the mod API or Harmony patches (<see cref="Patches"/>, <see cref="Clock"/>).
/// Texts in the game's language come from the installed original (<see cref="OriginalTexts"/>).
/// </summary>
public sealed class CheatMenuMod : IMod
{
    /// <summary>The mod's random purpose (set in <see cref="Initialize"/>).</summary>
    internal static RngPurpose Purpose;

    public void Initialize(ModContext context)
    {
        Purpose = context.RngPurpose;
        OriginalTexts.Add(context);
        var rules = new GameRules(context, context.Population.Bodies.Constants);
        context.AddSystem("cheats", _ => NewSystem(context, rules), SystemAnchor.End);

        var window = new CheatWindow(context);
        window.MenuKey = context.AddKeyAction("menu", "Cheat menu", _ => window.Toggle(), Binding.Of(Key.F8));
        context.AddUi(window);
        context.AddFrame(window.Frame);
        Patches.Install(context);
        SquadPatches.Install(context);
        BasePatches.Install(context);
        WorldPatches.Install(context);
        Clock.Install(context);
        context.Log("Cheat Menu initialized");
    }

    /// <summary>A world's cheat system: every rule back to normal, the window pointed at it.</summary>
    static CheatSystem NewSystem(ModContext context, GameRules rules)
    {
        rules.Reset();
        var system = new CheatSystem(context.Population, rules);
        CheatWindow.Running = system;
        CheatSystem.Current = system;
        return system;
    }
}
