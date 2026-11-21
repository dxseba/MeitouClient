using HarmonyLib;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// Port of PersistentPrisoners (workshop 3798215878, GPL-3.0-or-later; rebuilt from its description): a platoon holding a prisoner in a
/// player-owned cage is always kept when it leaves the loaded area, whatever the keep switches say; an overview of the prisoners (a key action,
/// O by default); optional feeding instead of starvation.
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
        context.AddSystem("prisoners", _ => Running = new PrisonerSystem(Settings, context.Population), SystemAnchor.End);
        Window = new PrisonerWindow();
        context.AddUi(Window);

        // The original's Ctrl+Shift+P cannot be a key action (one key, no modifiers), and P is the photo mode's key.
        context.AddKeyAction("overview", "Prisoner overview", _ => Window.Toggle(), Key.O);
        KeepHook.Install(context);
        context.Log("PersistentPrisoners initialized");
    }
}

public sealed class PrisonerSettings
{
    [Setting("Keep caged prisoners", Hint = "A prisoner in one of your cages is never despawned when you leave the area.")]
    public bool Protect { get; set; } = true;

    [Setting("Starvation", Hint = "Off: your caged prisoners are slowly fed up to the minimum below instead of starving.")]
    public bool Starvation { get; set; } = true;

    [Setting("Minimum nutrition", Hint = "With starvation off, prisoners are fed up to this (the game's 0-300 hunger scale).", Min = 1, Max = 300)]
    public int MinimumNutrition { get; set; } = 100;
}

/// <summary>A postfix on the population's keep test (<c>PopulationSystem.Kept</c>) keeps a platoon with a prisoner in a player cage.</summary>
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
