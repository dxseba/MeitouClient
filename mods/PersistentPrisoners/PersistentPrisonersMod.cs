using HarmonyLib;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// Port of PersistentPrisoners 0.2.8 (workshop 3798215878, GPL-3.0-or-later; rules from its DLL and README): a platoon holding a prisoner in a
/// player-owned cage is always kept when it leaves the loaded area, whatever the keep switches say; an overview of the occupants
/// (Ctrl+Shift+P); with starvation off the game feeds them towards Minimum Nu.
/// </summary>
public sealed class PersistentPrisonersMod : IMod
{
    internal static readonly PrisonerSettings Settings = new();

    /// <summary>The running world's system; null before a world starts.</summary>
    internal static volatile PrisonerSystem? Running;

    internal static PrisonerWindow Window { get; private set; } = null!;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("prisoners", _ => Running = new PrisonerSystem(Settings, context.Population, context.Data), SystemAnchor.End);
        Window = new PrisonerWindow();
        context.AddUi(Window);

        // The original's Ctrl+Shift+P: a binding is one key, so the action acts only with Ctrl and Shift held.
        context.AddKeyAction("overview", "Prisoner overview (Ctrl+Shift)", key =>
        {
            if (key.Ctrl && key.Shift) Window.Toggle();
        }, Key.P);
        KeepHook.Install(context);
        context.Log("PersistentPrisoners initialized");
    }
}

public sealed class PrisonerSettings
{
    [Setting("Keep caged prisoners", Hint = "A prisoner in one of your cages is never despawned when you leave the area.")]
    public bool Protect { get; set; } = true;

    [Setting("Allow starvation",
        Hint = "AllowStarvation. Off: NPCs are fed gradually towards Minimum Nu. Low values may be fatal. Player squad hunger stays native.")]
    public bool Starvation { get; set; } = true;

    [Setting("Minimum Nu", Hint = "MinimumNu (1 - 300): with starvation off, the game feeds caged NPCs towards this.", Min = 1, Max = 300)]
    public int MinimumNutrition { get; set; } = 100;
}

/// <summary>
/// A postfix on the population's keep test (<c>PopulationSystem.Kept</c>) keeps a platoon with a prisoner in a player cage. Harmony: the removal
/// vetoes (<see cref="Simulation.Bodies.IRemovalVeto"/>) cover corpses only, not a platoon's despawn or the off-screen orphan timer.
/// </summary>
static class KeepHook
{
    public static void Install(ModContext context)
    {
        var kept = AccessTools.Method(typeof(Simulation.Population.PopulationSystem), "Kept", [typeof(Platoon)])
            ?? throw new MissingMethodException("PopulationSystem", "Kept");
        context.Harmony.Patch(kept, postfix: new HarmonyMethod(typeof(KeepHook), nameof(AfterKept)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result by name
    static void AfterKept(Platoon p, ref bool __result)
    #pragma warning restore IDE1006
    {
        if (!__result && PersistentPrisonersMod.Settings.Protect && PersistentPrisonersMod.Running?.Protects(p) == true) __result = true;
    }
}
