using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

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
        var group = context.AddSettings(Settings);
        context.AddSystem("prisoners", _ => Running = new PrisonerSystem(Settings, context.Population, context.Data, context.Log), SystemAnchor.End);
        Window = new PrisonerWindow(group, OriginalLayout.Widths(context), context.Log);
        context.AddUi(Window);

        // The original's Ctrl+Shift+P, as a chord: it is taken before photo mode's P.
        context.AddKeyAction("overview", "Prisoner overview", _ => Window.Toggle(), Binding.Of(Key.P).With(KeyModifiers.Ctrl | KeyModifiers.Shift));
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

    [Setting("Diagnostic logging", Hint = "Diagnostics: write the occupants found and the protection added or removed to the mod log.")]
    public bool Diagnostics { get; set; }
}
