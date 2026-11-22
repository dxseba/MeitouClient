using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Disarm;

/// <summary>
/// Kick: the actor walks to a loose ground item and moves it <see cref="DisarmSettings.KickDistance"/> plus up to <see cref="KickVary"/> further
/// along the line from the actor through the item, stopping short of unwalkable ground or a step (a wall or another floor).
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

        var from = world.Characters.Next[actor].Position;
        if (Distance(from, item.Position) <= KickReach)
        {
            Kick(world, from, item);
            return true;
        }

        return world.Tick - kick.Tick > KickSeconds / world.TickSeconds;
    }

    void Kick(World world, Vector3 from, Meitou.Simulation.Items.GroundItem item)
    {
        var flat = new Vector2(item.Position.X - from.X, item.Position.Z - from.Z);
        var direction = flat.LengthSquared() > 1e-4f ? Vector2.Normalize(flat) : Vector2.UnitY;
        ulong key = Rng.Hash(world.Seed, (ulong)item.Id, purpose, (ulong)world.Tick);
        float distance = settings.KickDistance + Rng.Float(key) * KickVary;

        var to = Landing(world, item.Position, direction, distance);
        if (world.GroundItems.Take(item.Id) is not { } taken) return;

        float yaw = MathF.Atan2(direction.X, direction.Y);
        world.GroundItems.Place(taken.Item, to, yaw, taken.Slot, taken.OwnedBy, taken.Camp);
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
