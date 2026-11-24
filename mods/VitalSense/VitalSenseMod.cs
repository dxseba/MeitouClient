using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Bodies;

namespace Meitou.VitalSense;

/// <summary>
/// Port of Vital Sense (workshop 3813852288, GPL-3.0): while the Highlight key (Alt) is held, a state marker over each downed or dead character
/// near the camera (ZZ unconscious, RC recovery coma, DY dying, PD playing dead, DE dead) in its relation's colour, with a "$" before it coloured by
/// the bounty tier, and a body tint (the engine's <c>Tint</c>, as the original's shader colour override). Its settings show through the installed
/// original's catalogues (<c>vital_sense</c>: en_GB, ru_RU, zh_CN).
/// </summary>
public sealed class VitalSenseMod : IMod
{
    internal static readonly VitalSettings Settings = new();

    /// <summary>The running world's system, for the markers; null before a world starts.</summary>
    internal static volatile VitalSystem? Running;

    public void Initialize(ModContext context)
    {
        string? original = OriginalFolder.Find(context);
        if (original is not null) context.AddTranslations("vital_sense", original);

        context.AddSettings(Settings);
        context.AddSystem("vitals", build => Build(build, context.Log), SystemAnchor.End);
        VitalTint.Install(context);
        VitalMarkers.Install(context);
        string[] folders = original is not null ? [context.Folder, original] : [context.Folder];
        context.AddUi(new VitalMarkersUi(new VitalIcons(folders, context.Log)));
        context.Log("Vital Sense initialized");
    }

    static ITickSystem Build(SystemBuildContext build, Action<string> log) =>
        build.Find<AggroSystem>() is { } aggro ? (Running = new VitalSystem(aggro.Relations, build.Find<BodySystem>()?.Constants, log))! : null!;
}
