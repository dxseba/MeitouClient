using System.Numerics;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Construction;

namespace Meitou.BuildModeGizmo;

/// <summary>
/// Moves or turns the player's unbuilt site <paramref name="Site"/> to <paramref name="Position"/> and <paramref name="Rotation"/>; with
/// <paramref name="Settle"/> its height is put back on the ground there (the same height over the ground it had).
/// </summary>
public sealed record MoveSite(string Site, Vector3 Position, Quaternion Rotation, bool Settle) : SimCommand;

/// <summary>
/// Applies <see cref="MoveSite"/> in the serial input step: the site keeps its id, record, materials delivered and progress, and only its place
/// changes. The registry is then marked changed, so the host takes the building out and puts it in again where it now stands (its model, collision,
/// lights and navmesh membership, as for any changed site), the builders' work spot follows the site (it is worked out from the site's place each
/// time), and the save writes the new place. A complete site, another faction's, a level building's, a wall piece or a site with furniture or
/// pieces on it is not moved (the original works on the player's own unbuilt sites only).
/// </summary>
sealed class MoveSystem(Action<string> log) : ITickSystem
{
    static readonly Action<BuildSite, Vector3>? SetPosition = Setter<Vector3>(nameof(BuildSite.Position));
    static readonly Action<BuildSite, Quaternion>? SetRotation = Setter<Quaternion>(nameof(BuildSite.Rotation));

    static Action<BuildSite, T>? Setter<T>(string property) =>
        AccessTools.PropertySetter(typeof(BuildSite), property) is { } setter ? AccessTools.MethodDelegate<Action<BuildSite, T>>(setter) : null;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is MoveSite move) Move(world, move);
        }
    }

    void Move(World world, MoveSite move)
    {
        var reg = world.Buildings;
        if (SetPosition is null || SetRotation is null || reg.Find(move.Site) is not { } site || !Movable(world, reg, site)) return;
        if (!Finite(move.Position) || !float.IsFinite(move.Rotation.X + move.Rotation.Y + move.Rotation.Z + move.Rotation.W)) return;

        var to = move.Position;
        if (move.Settle) to.Y = Settled(world, site, to);

        SetPosition(site, to);
        SetRotation(site, Quaternion.Normalize(move.Rotation));
        reg.Changed();
        Reassess(world, site);
        Reaim(world, site);
        log($"site moved: {site.Id} to {to.X:0.0} {to.Y:0.0} {to.Z:0.0}");
    }

    /// <summary>
    /// The player's own site, player-built, not complete, not a wall piece, with nothing inside, on or attached to it.
    /// </summary>
    static bool Movable(World world, BuildingRegistry reg, BuildSite site)
    {
        if (site.Owner != world.Player.Faction || site.Complete || site.LevelPlacement is not null || site.Destroyed) return false;
        if (site.Parent is not null || site.MountedOn is not null || site.WallParent is not null) return false;

        foreach (var other in reg.All)
        {
            if (other.Parent == site.Id || other.MountedOn == site.Id || other.WallParent == site.Id) return false;
        }

        return true;
    }

    /// <summary>
    /// The height at the new place that keeps the site's height over the ground (a ghost is placed on the ground, so this puts it back there).
    /// </summary>
    static float Settled(World world, BuildSite site, Vector3 to)
    {
        var walk = world.Walkability;
        float lift = site.Position.Y - walk.GroundHeight(site.Position.X, site.Position.Z, site.Position.Y);
        return walk.GroundHeight(to.X, to.Z, to.Y) + lift;
    }

    static readonly Action<World, PlayerTown?, ConstructionSystem?>? ReassessTown =
        AccessTools.TypeByName("Meitou.Simulation.Construction.TownRefit") is { } refit && AccessTools.Method(refit, "Reassess") is { } method
            ? AccessTools.MethodDelegate<Action<World, PlayerTown?, ConstructionSystem?>>(method)
            : null;

    /// <summary>
    /// The player's outposts are reassessed as after a placement (the town the site is in now, then the others: the engine's
    /// <c>TownRefit.Reassess</c>), so a site moved out of or into an outpost's radius changes town.
    /// </summary>
    static void Reassess(World world, BuildSite site)
    {
        var kinds = world.Systems.OfType<ConstructionSystem>().FirstOrDefault();
        ReassessTown?.Invoke(world, TownLookup.PlayerTownOf(world, site), kinds);
    }

    /// <summary>
    /// The builders, deliverers and fetchers of the site drop the way they walk, so their next task step aims at the site's new work spot.
    /// </summary>
    static void Reaim(World world, BuildSite site)
    {
        var table = world.Characters;
        for (int s = 0; s < table.HighWater; s++)
        {
            ref var n = ref table.Next[s];
            if (!n.Alive || table.Cold(s) is not { } cold || cold.BuildSite != site.Id) continue;

            n.Flags = (ushort)(n.Flags & ~(ushort)Simulation.Movement.MoveFlags.AnyPath);
            n.PathCursor = 0;
            n.TaskTime = 0;
            cold.PathRequest = 0;
        }
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
