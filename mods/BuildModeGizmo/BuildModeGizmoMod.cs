using Meitou.Modding;

namespace Meitou.BuildModeGizmo;

/// <summary>
/// Port of Build Mode Gizmo release 5 (workshop 3813761881): select one of your own unbuilt building sites and move or turn it with arrows and the
/// transform window (typed coordinates, steps, reset), keeping its progress; the site is rebuilt where it now stands (README "Facts").
/// </summary>
public sealed class BuildModeGizmoMod : IMod
{
    public void Initialize(ModContext context)
    {
        if (OriginalFolder.Find(context) is { } original) context.AddTranslations("build_mode_gizmo", original);

        var settings = new GizmoSettings();
        context.AddSettings(settings);
        context.AddSystem("move-site", _ => new MoveSystem(context.Log), Simulation.SystemAnchor.Before(Simulation.StandardParts.Movement));
        var window = new GizmoWindow(context, settings);
        context.AddUi(window);
        context.AddFrame(window.Frame);
        context.Log("Build Mode Gizmo initialized");
    }
}
