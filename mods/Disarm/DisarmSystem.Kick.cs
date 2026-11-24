using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Disarm;

/// <summary>
/// Kick: the actor walks to a loose ground item and moves it <see cref="DisarmSettings.KickDistance"/> plus up to <see cref="KickVary"/> further
/// the way the actor faces (the original's movement facing direction), stopping short of unwalkable ground or a step (a wall or another floor).
/// The item keeps its turn, as the original keeps its rotation.
/// </summary>
sealed partial class DisarmSystem
{
    /// <summary>World units the actor must be within (the original's 6, with slack for where the walk ends).</summary>
    const float KickReach = 10;

    /// <summary>The random extra distance of a kick (the original's <c>DISARM_KICK_DISTANCE_VARY</c>).</summary>
    const float KickVary = 6;

    /// <summary>Seconds an actor has to reach the item (the original's 8 s timeout).</summary>
    const float KickSeconds = 8;

    /// <summary>The largest height change between steps that a kicked item may cross.</summary>
    const float KickStep = 2;

    readonly List<(CharacterId Actor, int Item, long Tick)> _kicks = [];

    void QueueKick(KickOrder order)
    {
        if (!settings.Kick) return;

        _kicks.RemoveAll(k => k.Actor == order.Actor);
        _kicks.Add((order.Actor, order.Item, order.Tick));
    }

    void ApproachKicks(World world)
    {
        foreach (var kick in _kicks.ToArray())
        {
            if (KickStepDone(world, kick)) _kicks.Remove(kick);
        }
    }

    bool KickStepDone(World world, (CharacterId Actor, int Item, long Tick) kick)
    {
        if (!world.Characters.TryResolveNext(kick.Actor, out int actor) || world.GroundItems.Find(kick.Item) is not { } item) return true;
        if (world.Characters.Cold(actor) is not { Animal: null }) return true;   // the original lets no animal kick

        ref readonly var hot = ref world.Characters.Next[actor];
        if (Distance(hot.Position, item.Position) <= KickReach)
        {
            Kick(world, Facing(hot.Yaw), item);
            return true;
        }

        return world.Tick - kick.Tick > KickSeconds / world.TickSeconds;
    }

    void Kick(World world, Vector2 direction, Meitou.Simulation.Items.GroundItem item)
    {
        ulong key = Rng.Hash(world.Seed, (ulong)item.Id, purpose, (ulong)world.Tick);
        float distance = settings.KickDistance + Rng.Float(key) * KickVary;

        var to = Landing(world, item.Position, direction, distance);
        if (world.GroundItems.Take(item.Id) is not { } taken) return;

        world.GroundItems.Place(taken.Item, to, YawOf(taken.Rotation), taken.Slot, taken.OwnedBy, taken.Camp);
    }

    /// <summary>
    /// The ground direction (x, z) of a heading (0 along +Z, turning towards +X).
    /// </summary>
    static Vector2 Facing(float yaw) => new(MathF.Sin(yaw), MathF.Cos(yaw));

    /// <summary>
    /// The heading about Y of a lying item's rotation (exact for a dropped item, which is turned about Y only).
    /// </summary>
    static float YawOf(Quaternion rotation)
    {
        var forward = Vector3.Transform(Vector3.UnitZ, rotation);
        return MathF.Atan2(forward.X, forward.Z);
    }

    /// <summary>Walks one unit at a time from <paramref name="start"/>; the last walkable point before a wall, a gap or a step.</summary>
    static Vector3 Landing(World world, Vector3 start, Vector2 direction, float distance)
    {
        var last = start;
        for (float travelled = 1; travelled <= distance; travelled++)
        {
            float x = start.X + direction.X * travelled;
            float z = start.Z + direction.Y * travelled;
            if (!world.Walkability.IsWalkable(x, z, last.Y)) break;

            float y = world.Walkability.GroundHeight(x, z, last.Y);
            if (MathF.Abs(y - last.Y) > KickStep) break;

            last = new(x, y, z);
        }

        return last;
    }
}
