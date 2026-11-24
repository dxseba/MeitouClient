using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Kidnap;

namespace Meitou.OrganHarvesting;

/// <summary>
/// Port of Organ Harvesting (Rokeshi, Workshop 3778746024): "Harvest Organs" on the right-click menu of a downed, living human sends the selected
/// characters to kill it where it lies; the player character who lands the killing blow gets a random organ item of the original's data mod
/// (Heart, Lung, Kidney or Liver). The original's <c>.mod</c> holds those items and must be enabled (README "Facts").
/// </summary>
public sealed class OrganHarvestingMod : IMod
{
    /// <summary>The organ items of the original's <c>HarvestOrgans.mod</c> (the original's source, its <c>ORGAN_SIDS</c>).</summary>
    internal static readonly string[] Organs = ["10-HarvestOrgans.mod", "11-HarvestOrgans.mod", "12-HarvestOrgans.mod", "13-HarvestOrgans.mod"];

    public void Initialize(ModContext context)
    {
        int missing = Organs.Count(id => context.Data.Find(id) is null);
        if (missing > 0) context.LogError($"{missing} organ item(s) of HarvestOrgans.mod are missing: enable the original mod (Workshop 3778746024)");

        var harvesters = new HarvesterCache();
        context.AddFrame(harvesters.Update);
        context.AddUi(new HarvestMenu(harvesters));
        context.AddSystem("harvest", build => new HarvestSystem(context, build.Find<CombatSystem>(), build.Find<CaptiveSystem>()),
            SystemAnchor.End);
        context.Log("Organ Harvesting initialized");
    }
}
