using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;

namespace Meitou.Disarm;

/// <summary>
/// AI rearm (beta, off by default): a character this mod disarmed, once conscious and awake again, is ordered to pick up the nearest weapon on the
/// ground. The engine has no "find weapon" task, so this is a plain pick-up order; the AI draws a weapon from its inventory when it next fights.
/// </summary>
sealed partial class DisarmSystem
{
    /// <summary>World units a rearming character looks for a weapon.</summary>
    const float RearmRadius = 200;

    /// <summary>Seconds a disarmed character is watched before it is given up on.</summary>
    const float RearmSeconds = 600;

    readonly Dictionary<CharacterId, long> _rearm = [];

    void Track(CharacterId id, long tick) => _rearm[id] = tick;

    void Rearm(World world)
    {
        if (!settings.Rearm)
        {
            _rearm.Clear();
            return;
        }

        foreach (var (id, since) in _rearm.ToArray())
        {
            if (RearmDone(world, id, since)) _rearm.Remove(id);
        }
    }

    bool RearmDone(World world, CharacterId id, long since)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { Medical.Dead: false } cold) return true;
        if (cold.Medical!.Incapacitated || CarrySystem.Asleep(table, slot)) return world.Tick - since > RearmSeconds / world.TickSeconds;
        if (DisarmRules.Wielded(cold) is not null) return true;

        if (NearestWeapon(world, table.Next[slot].Position) is { } item)
            enqueue(new PickUpItemOrder([id], item.Id) { Tick = world.Tick + 1 });
        return true;
    }

    static GroundItem? NearestWeapon(World world, Vector3 at) =>
        world.GroundItems.All
            .Where(g => g.Item.Type is FcsRecordType.Weapon or FcsRecordType.Crossbow && Distance(at, g.Position) <= RearmRadius)
            .MinBy(g => Distance(at, g.Position));
}
