using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Bodies;

namespace Meitou.VitalSense;

/// <summary>
/// Port of Vital Sense (workshop 3813852288, GPL-3.0): while the Highlight key (Alt) is held, a state marker over each downed or dead character
/// near the camera (ZZ unconscious, RC recovery coma, DY dying, PD playing dead, DE dead) in its relation's colour, with a "$" before it coloured by
/// the bounty tier; downed characters also join the game's hover outline, as the stand-in for the original's shader tint.
/// </summary>
public sealed class VitalSenseMod : IMod
{
    internal static readonly VitalSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("vitals", Build, SystemAnchor.End);
        VitalMarkers.Install(context);
        VitalOutline.Install(context);
        context.AddUi(new VitalMarkersUi());
        context.Log("Vital Sense initialized");
    }

    static ITickSystem Build(SystemBuildContext build) =>
        build.Find<AggroSystem>() is { } aggro ? new VitalSystem(aggro.Relations, build.Find<BodySystem>()?.Constants) : null!;
}
