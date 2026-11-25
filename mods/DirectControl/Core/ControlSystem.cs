using System.Numerics;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;

namespace Meitou.DirectControl;

/// <summary>
/// The simulation side of direct control: which character the player drives, its wanted step, and the manual combat presses. The commands come
/// from the host (<see cref="Pilot"/>) and are taken in <see cref="Inputs"/>; the step is written in <see cref="Move"/> right before the movement
/// system's (<see cref="SystemAnchor.Before"/> Movement), the swings are let through by <see cref="AttackGate"/> and the defences picked by
/// <see cref="Defence"/>. Not saved: the original keeps none of this in a save either (a load starts with direct control off).
/// </summary>
sealed partial class ControlSystem(RngPurpose purpose, CombatConstants constants) : ITickSystem, IStateHashed
{
    /// <summary>
    /// A tick long before any press.
    /// </summary>
    const long Never = long.MinValue / 4;

    /// <summary>
    /// The driven character; none while direct control is off.
    /// </summary>
    public CharacterId Controlled { get; private set; } = CharacterId.None;

    /// <summary>
    /// Manual combat is on: the character swings only on the Attack press and defends only on the Block and Dodge presses (or as the settings let
    /// it on its own).
    /// </summary>
    public bool Manual { get; private set; }

    Tuning _tuning = new(400, 1500, 400, 800, 1000, 1200, 600, 300, 400, 20, false, false, true, 1, false);

    /// <summary>
    /// The latest steer command (its direction zero while standing).
    /// </summary>
    SteerCommand? _steer;

    /// <summary>
    /// The character's step is ours: its one-point path was written by <see cref="Move"/> (so letting go stops it).
    /// </summary>
    bool _steering;

    /// <summary>
    /// The tick the latest presses came at (<see cref="Never"/>: none waiting).
    /// </summary>
    long _attackTick = Never, _blockTick = Never, _dodgeTick = Never;

    /// <summary>
    /// The last attack press asked for the step-in time.
    /// </summary>
    bool _attackApproach;

    /// <summary>
    /// The Action RPG scheme's chosen direction of the last attack and of the held block (0: any).
    /// </summary>
    int _attackDirection, _blockDirection;

    /// <summary>
    /// Block is held.
    /// </summary>
    bool _blockHeld;

    /// <summary>
    /// The first tick a dodge press may count again, and until when the perfect-block reward lasts.
    /// </summary>
    long _dodgeReady = Never, _rewardUntil = Never;

    /// <summary>
    /// The attack (attacker slot and its attack number) a perfect block met: its later blows are met too.
    /// </summary>
    (int Attacker, int Seq) _perfect = (-1, -1);

    /// <summary>
    /// For the host: the perfect-block reward is running (the weapon glows blue). Written in the tick, read on the host's thread.
    /// </summary>
    public volatile bool RewardShown;

    /// <summary>
    /// For the host: the tick of the last perfect block, to light the weapon once per block.
    /// </summary>
    public long LastPerfect = Never;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case ControlCommand control:
                    TakeControl(world, control);
                    break;
                case SteerCommand steer when steer.Character == Controlled:
                    _steer = steer;
                    break;
                case CombatPress press when press.Character == Controlled:
                    Press(world, press);
                    break;
                case StanceCommand stance when stance.Character == Controlled:
                    Stance(world, stance);
                    break;
                case SwapWeaponCommand swap when swap.Character == Controlled:
                    Swap(world);
                    break;
            }
        }

        FollowStance(world);

        RewardShown = world.Tick < _rewardUntil;
    }

    /// <summary>
    /// Direct control moves to another character (or ends): the old one's step is let go and the presses forgotten.
    /// </summary>
    void TakeControl(World world, ControlCommand control)
    {
        if (control.Character != Controlled) Release(world);

        Controlled = control.Character;
        Manual = control.Manual && !control.Character.IsNone;
        _tuning = control.Tuning;
    }

    /// <summary>
    /// Stops the step direct control gave and clears every press.
    /// </summary>
    void Release(World world)
    {
        var table = world.Characters;
        if (_steering && table.TryResolveNext(Controlled, out int slot)) StopSteering(table, slot);

        _steer = null;
        _steering = false;
        _sheathed = false;
        Stuck = false;
        (_attackTick, _blockTick, _dodgeTick) = (Never, Never, Never);
        _blockHeld = false;
        _perfect = (-1, -1);
    }

    void Press(World world, CombatPress press)
    {
        long now = world.Tick;
        switch (press.Kind)
        {
            case PressKind.Attack:
                (_attackTick, _attackApproach, _attackDirection) = (now, press.Approach, press.Direction);
                break;
            case PressKind.Block:
                (_blockTick, _blockHeld, _blockDirection) = (now, true, press.Direction);
                break;
            case PressKind.BlockUp:
                _blockHeld = false;
                break;
            case PressKind.Dodge:
                _dodgeTick = now;
                break;
        }
    }

    /// <summary>
    /// Ticks in <paramref name="ms"/> milliseconds of game time (at least one).
    /// </summary>
    static long Ticks(World world, int ms) => Math.Max(1, (long)MathF.Ceiling(ms / 1000f / world.TickSeconds));

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(Controlled);
        hasher.Add(Manual);
        hasher.Add(_steering);
        hasher.Add(_steer?.Direction ?? Vector2.Zero);
        hasher.Add(_attackTick);
        hasher.Add(_blockTick);
        hasher.Add(_dodgeTick);
        hasher.Add(_blockHeld);
        hasher.Add(_sheathed);
        hasher.Add(_dodgeReady);
        hasher.Add(_rewardUntil);
        hasher.Add(_perfect.Attacker);
        hasher.Add(_perfect.Seq);
    }
}
