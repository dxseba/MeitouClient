using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace Meitou.DirectControl;

/// <summary>
/// The direct step: while a direction is held the controlled character walks it with no path search, on a one-point path kept a lead ahead of it
/// every tick, so the movement system's own walk (its speed for the mode, turning, acceleration, wall slides and neighbours) carries it and it
/// never arrives. A click-to-move order, a job's walk or the fight footwork are overridden while the key is held, as in the original.
/// </summary>
sealed partial class ControlSystem
{
    /// <summary>
    /// How far ahead the steered point is kept (engine choice: far enough that the walk never brakes into it).
    /// </summary>
    const float Lead = 40;

    /// <summary>
    /// The one-point path of the steered character (written on its own slot only).
    /// </summary>
    readonly Vector3[] _point = new Vector3[1];

    /// <summary>
    /// Parallel: the controlled character's step, on its own slot only.
    /// </summary>
    public void Move(World world, Partition partition)
    {
        var table = world.Characters;
        if (Controlled.IsNone || !table.TryResolveNext(Controlled, out int slot) || slot < partition.Start || slot >= partition.End) return;
        if (table.Cold(slot) is not { } cold) return;

        bool wanted = _steer is { } steer && steer.Direction != Vector2.Zero;
        bool moving = wanted && Free(cold);
        Stuck = wanted && !moving && cold.Medical is { Incapacitated: false } && cold.CarriedBy.IsNone && !cold.IsImprisoned;
        if (moving)
        {
            Steer(table, slot, cold, _steer!);
            return;
        }

        if (_steering) StopSteering(table, slot);
        if (_steer is { Face: true } face && Free(cold)) Turn(table, slot, face.FaceYaw);
    }

    /// <summary>
    /// Whether the character can take a step of its own: awake, not carried, not caged, in a bed, a seat or chains.
    /// </summary>
    static bool Free(CharacterCold cold) =>
        cold.Medical is { Incapacitated: false } && cold.CarriedBy.IsNone && cold.InSomething == Simulation.Locks.HeldState.None;

    /// <summary>
    /// Keeps the one-point path a <see cref="Lead"/> ahead along the wanted direction, at the wanted mode (the character's own run speed by
    /// default) and, with the settings' cap off or a multiplier, at a speed of its own.
    /// </summary>
    void Steer(CharacterTable table, int slot, CharacterCold cold, SteerCommand steer)
    {
        ref var n = ref table.Next[slot];
        var at = n.Position;
        var ahead = new Vector3(at.X + steer.Direction.X * Lead, at.Y, at.Z + steer.Direction.Y * Lead);

        _point[0] = ahead;
        cold.Path = _point;
        cold.PathExact = 1;
        cold.PathRequest = 0;
        cold.SplicePath = null;
        cold.ArriveWithin = 0;
        cold.BlockedRepaths = 0;
        cold.OrderMove = false;
        cold.WalkEnd = WalkEnd.None;
        cold.GoalHeight = float.NaN;
        n.Goal = new Vector2(ahead.X, ahead.Z);
        n.PathCursor = 0;
        n.Mode = steer.Mode ?? cold.RunSpeed;
        n.Task = (byte)CharacterTask.GoTo;
        n.Flags = (ushort)((n.Flags & ~(ushort)MoveFlags.AnyPath) | (ushort)MoveFlags.HasPath);
        cold.SpeedOverride = OwnSpeed(n, n.Mode);
        _steering = true;
    }

    /// <summary>
    /// The speed the settings ask for (0: the movement system's own for the mode): the mode's cap times the multiplier, held under the character's
    /// real top speed with the speed cap on (injuries, loads, shackles).
    /// </summary>
    float OwnSpeed(in CharacterHot n, byte mode)
    {
        if (_tuning.SpeedCap && _tuning.SpeedMult == 1) return 0;

        float speed = SpeedMode.Cap(mode, n.WalkSpeed);
        if (speed > 900) speed = n.MaxSpeed;
        speed *= _tuning.SpeedMult;
        return _tuning.SpeedCap ? MathF.Min(speed, n.MaxSpeed) : speed;
    }

    /// <summary>
    /// The key was let go: the character stops at once (a crisp stop) if it is still on the step direct control gave it.
    /// </summary>
    void StopSteering(CharacterTable table, int slot)
    {
        var cold = table.Cold(slot)!;
        cold.SpeedOverride = 0;
        if (ReferenceEquals(cold.Path, _point)) MovementSystem.Stop(table, slot);

        _steering = false;
    }

    /// <summary>
    /// A standing character turns to the view (first person), unless it fights.
    /// </summary>
    static void Turn(CharacterTable table, int slot, float yaw)
    {
        ref var n = ref table.Next[slot];
        if (n.Task == (byte)CharacterTask.Attack) return;

        n.Yaw = yaw;
    }
}
