using System.Collections.Frozen;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;

namespace Meitou.Disarm;

/// <summary>
/// AI rearm (beta, off by default): a character this mod disarmed is watched for <see cref="RearmSeconds"/>; while it is up and awake, holds no
/// weapon and has no order in hand it is ordered again and again to pick up the nearest weapon on the ground, as the original re-forces its
/// native FIND_WEAPON order every 500 ms. A more urgent goal (<see cref="Urgent"/>, by the goal's text as the original reads it) or a fight defers
/// it for up to <see cref="DeferSeconds"/>; one better with fists than a blade is not sent.
/// The engine has no "find weapon" task, so this is a plain pick-up order.
/// </summary>
sealed partial class DisarmSystem
{
    /// <summary>World units a rearming character looks for a weapon.</summary>
    const float RearmRadius = 200;

    /// <summary>
    /// The original's timings: a disarmed character is watched 300 s, and a higher-priority goal (here: a fight) defers the order for at most
    /// the first 45 s.
    /// </summary>
    const float RearmSeconds = 300, DeferSeconds = 45;

    /// <summary>
    /// The goals the original lets finish before it forces a rearm, by their HUD goal text (its <c>getCurrentGoalString</c>).
    /// </summary>
    static readonly FrozenSet<string> Urgent = FrozenSet.Create(StringComparer.Ordinal,
        "Medic", "Fixing Legs", "Protect close allies", "Attacking others", "Attacking enemies", "Self preservation", "Find and rescue");

    readonly Dictionary<CharacterId, long> _rearm = [];

    void Track(CharacterId id, long tick) => _rearm[id] = tick;

    void Rearm(World world)
    {
        if (!settings.Rearm)
        {
            _rearm.Clear();
            return;
        }

        foreach (var (id, since) in _rearm.OrderBy(r => r.Key.Slot).ThenBy(r => r.Key.Generation).ToArray())
        {
            if (RearmDone(world, id, since)) _rearm.Remove(id);
        }
    }

    /// <summary>
    /// One pass over a watched character; true when it is no longer watched (gone, dead, armed, better unarmed, or out of time).
    /// </summary>
    bool RearmDone(World world, CharacterId id, long since)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { Medical.Dead: false } cold) return true;

        float watched = (world.Tick - since) * world.TickSeconds;
        if (watched > RearmSeconds || Carries(cold) || !DisarmRules.BetterArmed(cold)) return true;
        if (DisarmRules.Down(cold) || CarrySystem.Asleep(table, slot) || cold.OrderQueue.Front is not null) return false;
        if (watched < DeferSeconds && Busy(world, slot, cold)) return false;

        if (NearestWeapon(world, table.Next[slot].Position) is { } item)
            enqueue(new PickUpItemOrder([id], item.Id) { Tick = world.Tick + 1 });
        return false;
    }

    /// <summary>
    /// Whether a more urgent goal holds the character: its HUD goal text is one of <see cref="Urgent"/>, or it fights (a fight the AI did not start
    /// has no goal text in Meitou; the original's fights show as Attacking others or enemies).
    /// </summary>
    static bool Busy(World world, int slot, CharacterCold cold) =>
        cold.InCombat || AiSystem.ActivityText(world.Characters.Next[slot], cold, world.Tick) is { } goal && Urgent.Contains(goal);

    /// <summary>
    /// Whether the character holds a weapon again: equipped, or picked up into its inventory (the AI draws it when it next fights).
    /// </summary>
    static bool Carries(CharacterCold cold) =>
        DisarmRules.Wielded(cold) is not null || cold.Inventory?.Items.Exists(i => i.Type is FcsRecordType.Weapon or FcsRecordType.Crossbow) == true;

    static GroundItem? NearestWeapon(World world, Vector3 at) =>
        world.GroundItems.All
            .Where(g => g.Item.Type is FcsRecordType.Weapon or FcsRecordType.Crossbow && Distance(at, g.Position) <= RearmRadius)
            .MinBy(g => Distance(at, g.Position));
}
