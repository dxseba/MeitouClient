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
        context.AddSettings(Settings);
        context.AddSystem("jobboard", _ => new JobBoardSystem(), SystemAnchor.End);
        Window = new JobWindow(Settings, () => context.Tick, context.Enqueue, new StationScanner(context.Data),
            context.Interface.HoldPause);
        Hooks.Install(context);
        context.AddUi(Window);
        context.Log("Job Management initialized");
    }
}
