using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation.Population;

namespace Meitou.QuickStartSelect;

/// <summary>
/// Port of Quick Start Select: the "Choose your beginning" window as a sortable, searchable list with a race filter, the start's details, its town
/// choice and relations, the game's advanced options beside it, Random start and Begin with a confirmation. Its translations are the original's
/// catalogues, read from the installed original.
/// </summary>
public sealed class QuickStartSelectMod : IMod
{
    const string OriginalItem = "3813761725";

    public void Initialize(ModContext context)
    {
        var settings = new QssSettings();
        var group = context.AddSettings(settings);
        AddTranslations(context);

        if (!GameAccess.StartReady)
        {
            context.LogError("the game's start window was not found (FrontendScreens members); the game's own window stays");
            return;
        }

        if (!GameAccess.OptionsReady) context.LogError("the game's advanced options were not found; the window shows no options");

        Patch(context);
        var starts = StartList.Build(context.Data, context.Tr, settings.CustomLevels(), settings.Debug ? context.Log : null);
        if (settings.Debug) LogStarts(context, starts);
        if (starts.Count == 0) context.Log("the game has no starts, keeping its own window");

        context.AddUi(new StartWindow(settings, group, starts, context.Tr));
    }

    /// <summary>The original's catalogues (quick_start_select.mo), from this folder or the installed original's.</summary>
    static void AddTranslations(ModContext context)
    {
        if (context.AddTranslations("quick_start_select")) return;

        string? beside = Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent ? Path.Combine(parent, OriginalItem) : null;
        if (beside is null || !Directory.Exists(beside) || !context.AddTranslations("quick_start_select", beside))
            context.Log($"no translations (the original, Workshop {OriginalItem}, is not installed beside this mod)");
    }

    /// <summary>
    /// Postfix on <c>FrontendScreens.BindNewGame</c> (the window's instance) and prefix on <c>PopulationSystem.StartPlace</c> (the town pick).
    /// </summary>
    static void Patch(ModContext context)
    {
        context.Harmony.Patch(GameAccess.BindNewGame, postfix: new HarmonyMethod(typeof(GameAccess), nameof(GameAccess.Captured)));
        TownPick.Log = context.LogError;
        PointerPatch.Patch(context);
        var place = AccessTools.Method(typeof(PopulationSystem), nameof(PopulationSystem.StartPlace));
        if (place is null) context.LogError("PopulationSystem.StartPlace not found: the town choice does nothing");
        else context.Harmony.Patch(place, prefix: new HarmonyMethod(typeof(TownPick), nameof(TownPick.Prefix)));
    }

    static void LogStarts(ModContext context, List<StartInfo> starts)
    {
        foreach (var s in starts)
            context.Log($"start '{s.Name}' | rank {s.Rank} | money {s.Start.Money} | races {string.Join(", ", s.Races)}");
    }
}
