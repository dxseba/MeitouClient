using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Navigation;

namespace Meitou.Earthworks;

/// <summary>
/// Port of Earthworks, as far as Meitou allows: the original's three Earthworks buildings (its data) are built by engineers as usual, and each
/// clears the scenery round it as its construction goes on, in 16 steps, keeping iron and copper. The scenery goes from the drawn foliage and
/// the navmesh at once and comes back when the building is dismantled. Leveling and passages need terrain changes Meitou cannot make at runtime.
/// </summary>
public sealed class EarthworksMod : IMod
{
    internal static readonly EarthworksSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        Clearings.Log = context.LogError;
        Clearings.Towns = [.. context.Population.Sites.Select(s => (new System.Numerics.Vector2(s.Position.X, s.Position.Z), s.Town.NoFoliageRange))];
        int found = Clearings.Kinds.Keys.Count(id => context.Data.Find(id) is not null);
        if (found == 0)
        {
            context.Log("the original's Earthworks buildings are not in the data (enable the original Earthworks, Workshop 3829117994): nothing to do");
            return;
        }

        Patch(context);
        context.AddSystem("areas", _ => new AreaSystem(), SystemAnchor.End);
        context.AddUi(new AreaPanel());
        context.Log($"Earthworks: {found} building kinds found");
    }

    /// <summary>
    /// Prefix on <c>BuildingSync.UpdateFoliageTowns</c> (gives the towns again when the clearings changed) and on <c>NavSystem.SetPlayerTowns</c>
    /// (adds the clearings to the outposts' circles).
    /// </summary>
    static void Patch(ModContext context)
    {
        var sync = AccessTools.TypeByName("Meitou.Game.Host.WorldSync.BuildingSync");
        var update = sync is null ? null : AccessTools.Method(sync, "UpdateFoliageTowns");
        var give = AccessTools.Method(typeof(NavSystem), nameof(NavSystem.SetPlayerTowns));
        if (update is null || give is null)
        {
            context.LogError("the game's foliage towns were not found (BuildingSync.UpdateFoliageTowns, NavSystem.SetPlayerTowns): no clearing");
            return;
        }

        Clearings.Outposts = AccessTools.Field(sync, "_foliageOutposts");
        context.Harmony.Patch(update, prefix: new HarmonyMethod(typeof(Clearings), nameof(Clearings.UpdatePrefix)));
        context.Harmony.Patch(give, prefix: new HarmonyMethod(typeof(Clearings), nameof(Clearings.GivePrefix)));
    }
}

/// <summary>The settings.</summary>
public sealed class EarthworksSettings
{
    /// <summary>The original's planner limits on a side, game units (14.4 and 115.2 m).</summary>
    public const float Least = 144, Most = 1152;

    [Setting("Clear scenery", Hint = "Earthworks buildings clear the trees and rocks in their area as they are built (iron and copper stay).")]
    public bool Enabled { get; set; } = true;

    [Setting("Default width", Hint = "A new Earthworks area's width across the building, in game units (10 to a metre). Change one built area from its "
        + "building's panel.", Min = Least, Max = Most, Step = 72)]
    public float Width { get; set; } = 288;

    [Setting("Default depth", Hint = "A new Earthworks area's depth along the building's facing, in game units.", Min = Least, Max = Most, Step = 72)]
    public float Depth { get; set; } = 288;

    [Setting("Shovel", Hint = "Engineers working an Earthworks site hold the original's shovel.")]
    public bool Shovel { get; set; } = true;

    public System.Numerics.Vector2 DefaultSize => new(Width, Depth);
}
