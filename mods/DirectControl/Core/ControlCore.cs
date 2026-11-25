using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.DirectControl;

/// <summary>
/// Wires direct control into a mod: the simulation system (before the movement system, for the step), the key actions, the settings, the frame
/// callback, the overlay and the Harmony patches. Shared by the Direct Control port and the Action RPG port (its fork), which differ in
/// <see cref="ControlSettings.Classic"/>.
/// </summary>
public static class ControlCore
{
    public static void Start(ModContext context, ControlSettings settings)
    {
        var constants = CombatConstants.FromDatabase(context.Data);
        var pilot = new Pilot(context, settings);
        Pilot.Current = pilot;
        context.AddSystem("control", _ => new ControlSystem(context.RngPurpose, constants), SystemAnchor.Before(StandardParts.Movement));
        pilot.AddKeys();
        context.AddSettings(settings).Changed += _ => Resend(pilot);
        context.AddFrame(pilot.Frame);
        context.AddCombatHitFeed(pilot.Hits);
        context.AddUi(new ControlOverlay(context.Id + ".overlay"));
        if (!HostAccess.Complete) context.LogError("some host members were not found: the camera parts of direct control may not work");

        Patches.Install(context, !settings.Classic);
        AttackGate.Install(context);
    }

    static void Resend(Pilot pilot)
    {
        if (pilot.On) pilot.SendControl();
    }
}
