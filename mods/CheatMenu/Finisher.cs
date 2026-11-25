using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Construction;

namespace Meitou.CheatMenu;

/// <summary>
/// Finish buildings near camera: the construction system's own completion (<c>ConstructionSystem.Finish</c>, private: the step a last builder
/// runs), called by reflection so the site's pieces, machine and the player's message follow as in the game. False when the method is gone.
/// </summary>
internal static class Finisher
{
    static readonly MethodInfo? FinishMethod = AccessTools.Method(typeof(ConstructionSystem), "Finish", [typeof(World), typeof(BuildSite)]);

    internal static bool Finish(World world, ConstructionSystem construction, BuildSite site)
    {
        if (FinishMethod is null) return false;

        FinishMethod.Invoke(construction, [world, site]);
        return true;
    }
}
