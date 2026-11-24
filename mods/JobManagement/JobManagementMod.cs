using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>
/// Port of Kenshi Job Management (SirixKC, MCM fork by workshop 3818460323): one window for the squads' job lists and the base's work stations.
/// </summary>
public sealed class JobManagementMod : IMod
{
    internal static readonly JobSettings Settings = new();

    internal static JobWindow Window { get; private set; } = null!;

    public void Initialize(ModContext context)
    {
        var original = new OriginalFiles(context);

        // The window's texts in the game's language, from the installed original's kenshi_job_management catalogues.
        if (original.Original is { } folder) context.AddTranslations("kenshi_job_management", folder);

        context.AddSettings(Settings);
        context.AddSystem("jobboard", _ => new JobBoardSystem(), SystemAnchor.End);
        Window = new JobWindow(Settings, () => context.Tick, context.Enqueue, new StationScanner(context.Data),
            context.Interface.HoldPause, new StationIcons(original), context.Log, context.Tr);
        Hooks.Install(context);
        context.AddUi(Window);
        context.Log("Job Management initialized");
    }
}
