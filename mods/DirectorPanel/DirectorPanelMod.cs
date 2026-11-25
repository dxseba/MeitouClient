using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Population;

namespace Meitou.DirectorPanel;

/// <summary>
/// Port of Lammy's Director Panel 1.6.6 (Workshop 3805040918, <c>DirectorPanel.dll</c>): a panel for filming and testing, opened with Ctrl+L, with
/// the original's tabs (Items, Stats, NPC Spawn, Relations, Research, Behaviour, Barbershop, Body, Effects, Teleport, World) and its RU/EN
/// switch. The window is the mod's own overlay; what it does to the world goes through <see cref="DirectorSystem"/>'s commands, applied while
/// paused too. README "Facts" lists what follows the original and what the port chose.
/// </summary>
public sealed class DirectorPanelMod : IMod
{
    internal static readonly PanelSettings Settings = new();

    /// <summary>The running world's system, for the window; null before a world starts.</summary>
    internal static volatile DirectorSystem? Running;

    public void Initialize(ModContext context)
    {
        DirectorSystem.Purpose = context.RngPurpose;
        Settings.Language = context.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? PanelSettings.Russian : PanelSettings.English;
        var group = context.AddSettings(Settings);
        var texts = new Texts(OriginalFiles.RussianTexts(context));
        if (!texts.HasRussian) context.Log($"the original (Workshop {OriginalFiles.OriginalItem}) is not installed: the panel shows English only");

        var catalog = new Catalog(context.Data, context.Population);
        context.AddSystem("director", build => Running = new DirectorSystem(build.Data, build.Find<PopulationSystem>(), build.Find<CrimeSystem>()) { Log = context.Log },
            SystemAnchor.End);

        var window = new DirectorWindow(context, catalog, texts, new Points(context), group);
        window.Key = context.AddKeyAction("panel", "Open the director panel", _ => window.Toggle(), Binding.Of(Key.L).With(KeyModifiers.Ctrl));
        context.AddFrame(window.Frame);
        context.AddUi(window);
        ClockPatch.Install(context);
        Patches.Install(context);
        context.Log($"Director Panel initialized: {catalog.Items.Count} items, {catalog.People.Count + catalog.Animals.Count} characters");
    }
}

/// <summary>The original's <c>DirectorPanel.ini</c> <c>Language</c> (RU or EN), kept under Mod settings; the panel's switch sets it too.</summary>
public sealed class PanelSettings
{
    public const string English = "English", Russian = "Russian";

    [Setting("Panel language", Hint = "The panel's own RU/EN switch sets it too.", Options = [English, Russian])]
    public string Language { get; set; } = Russian;
}
