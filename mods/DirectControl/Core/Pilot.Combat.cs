using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Player;

namespace Meitou.DirectControl;

/// <summary>
/// The manual combat keys, read raw each control tick while direct control is on (the original reads its own keys the same way): the Direct
/// Control scheme's Attack / Block / Dodge keys (settings), the Action RPG scheme's mouse (left: draw a direction and release to swing, right:
/// hold to block in the direction last drawn, middle: lock on) and its F view key. Times are the host's control ticks (game-independent, as the
/// original's are wall-clock milliseconds); each press goes to the simulation as a command.
/// </summary>
/// <summary>
/// A drawn mouse direction (the Action RPG scheme).
/// </summary>
enum Gesture : byte
{
    None,
    Up,
    Down,
    Left,
    Right,
}

sealed partial class Pilot
{
    /// <summary>
    /// How long the last drawn direction is kept for the next press (the fork's 0.8 s).
    /// </summary>
    const double KeepGestureSeconds = 0.8;

    /// <summary>
    /// The mouse movement that picks a direction, in pixels (engine choice).
    /// </summary>
    const float GestureDeadZone = 12;

    double _clock;
    double _attackReady, _blockReady, _blockSince = -1, _gestureAt = -1;
    readonly Dictionary<Key, double> _lastPress = [];
    bool _blockOrderWasOn;
    Vector2 _gesture;
    CharacterId _lock = CharacterId.None;

    /// <summary>
    /// The Action RPG scheme: a swing direction is being drawn with the left button held (the view does not turn).
    /// </summary>
    bool Drawing { get; set; }

    /// <summary>
    /// The Action RPG scheme: the block is held (no step while it lasts).
    /// </summary>
    bool Blocking => !settings.Classic && _blockSince >= 0;

    void ClearCombatKeys()
    {
        (Drawing, _blockSince, _gesture, _lock) = (false, -1, Vector2.Zero, CharacterId.None);
    }

    void CombatKeys(ActionState actions, CharacterSnapshot me)
    {
        _clock += HostAccess.Session?.Ticks.TickSeconds ?? 1 / 30.0;
        if (settings.Classic)
        {
            if (Manual) ClassicKeys(actions, me);
            return;
        }

        ActionKeys(actions, me);
        FollowLock(me);
    }

    void ClassicKeys(ActionState actions, CharacterSnapshot me)
    {
        var input = actions.Input;
        if (KeyOf(settings.AttackKey, Key.E) is var attack && input.WasPressed(attack) && Fresh(attack)) Attack(me, 0);
        var dodge = KeyOf(settings.DodgeKey, Key.Q);
        if (input.WasPressed(dodge) && Fresh(dodge)) Send(new CombatPress(me.Id, PressKind.Dodge) { Tick = Now });

        var block = KeyOf(settings.BlockKey, Key.F);
        if (input.WasPressed(block) && Fresh(block)) BlockDown(me, 0);
        bool tooLong = _blockSince >= 0 && _clock - _blockSince > settings.BlockHoldMs / 1000.0;
        if (_blockSince >= 0 && (!input.IsDown(block) || tooLong)) BlockUp(me);
    }

    void ActionKeys(ActionState actions, CharacterSnapshot me)
    {
        var input = actions.Input;
        if (input.WasPressed(Key.F)) SetView(View == View.FirstPerson ? View.Shoulder : View.FirstPerson);
        if (input.WasPressed(MouseButton.Middle)) ToggleLock();
        StanceKeys(input, me);
        if (!LookActive)
        {
            Drawing = false;
            if (_blockSince >= 0) BlockUp(me);
            return;
        }

        DrawGesture(actions);
        InteractKey(input, me);
        if (input.WasPressed(MouseButton.Right)) BlockDown(me, BlockCode(Drawing ? GestureOf(_gesture) : RecentGesture()));
        if (_blockSince >= 0 && !input.IsDown(MouseButton.Right)) BlockUp(me);
    }

    /// <summary>
    /// Left button: held, the mouse draws a direction; released, the swing goes that way (a cancel by the right button drops it).
    /// </summary>
    void DrawGesture(ActionState actions)
    {
        var input = actions.Input;
        if (input.WasPressed(MouseButton.Left)) (Drawing, _gesture) = (true, Vector2.Zero);
        if (Drawing) _gesture += actions.MouseDelta;
        if (Drawing && input.WasPressed(MouseButton.Right)) Drawing = false;
        if (!Drawing || input.IsDown(MouseButton.Left)) return;

        Drawing = false;
        var gesture = GestureOf(_gesture);
        if (gesture != Gesture.None) (_lastGesture, _gestureAt) = (gesture, _clock);
        if (_lockedGesture != Gesture.None) gesture = _lockedGesture;
        if (HostAccess.Session?.CurrentSnapshot is { } snapshot && HostAccess.Find(snapshot, Controlled) is { } me) Attack(me, AttackCode(gesture));
    }

    /// <summary>
    /// The way a drawn mouse movement went (the larger of its two axes); none under the dead zone.
    /// </summary>
    static Gesture GestureOf(Vector2 drawn)
    {
        if (drawn.Length() < GestureDeadZone) return Gesture.None;
        if (MathF.Abs(drawn.Y) > MathF.Abs(drawn.X)) return drawn.Y < 0 ? Gesture.Up : Gesture.Down;

        return drawn.X > 0 ? Gesture.Right : Gesture.Left;
    }

    /// <summary>
    /// A swing's technique direction (the editor's <c>CutDirection</c>: 1 CUT_DOWNWARD, 2 CUT_LEFT, 3 CUT_RIGHT, 4 CUT_THRUST): up the downward
    /// cut, down the thrust, and the side cuts by the blade's way (the fork: mouse right, the blade goes right to left, a left cut).
    /// </summary>
    static int AttackCode(Gesture g) => g switch { Gesture.Up => 1, Gesture.Down => 4, Gesture.Right => 2, Gesture.Left => 3, _ => 0 };

    /// <summary>
    /// A block's side: the fork turns the sides round for blocks (mouse right, the right block), up and down as for swings.
    /// </summary>
    static int BlockCode(Gesture g) => g switch { Gesture.Up => 1, Gesture.Down => 4, Gesture.Right => 3, Gesture.Left => 2, _ => 0 };

    Gesture RecentGesture() => _gestureAt >= 0 && _clock - _gestureAt <= KeepGestureSeconds ? _lastGesture : Gesture.None;

    /// <summary>
    /// The direction shown by the arrows: the one being drawn, else the last one while it is kept.
    /// </summary>
    public Gesture ShownGesture => Drawing ? GestureOf(_gesture) : RecentGesture();

    /// <summary>
    /// The attack direction locked with the lock key (none: the mouse decides).
    /// </summary>
    public Gesture LockedGesture => _lockedGesture;

    Gesture _lockedGesture, _lastGesture;

    /// <summary>
    /// The lock key: the shown direction becomes every swing's, or the lock is let go.
    /// </summary>
    void ToggleDirectionLock() => _lockedGesture = _lockedGesture != Gesture.None ? Gesture.None : ShownGesture;

    /// <summary>
    /// One swing: at the aimed enemy (ordered to fight it, so it steps in), past the recovery after the last swing.
    /// </summary>
    void Attack(CharacterSnapshot me, int direction)
    {
        if (_clock < _attackReady) return;

        _attackReady = _clock + settings.AttackRecoveryMs / 1000.0;
        var target = AimTarget(me);
        if (Shoot(me, target)) return;

        if (target is { } t) Send(new AttackOrder([me.Id], t.Id) { Tick = Now });

        bool far = target is { } aimed && Vector3.Distance(aimed.Position, me.Position) > 25;
        Send(new CombatPress(me.Id, PressKind.Attack, direction, far) { Tick = Now });
    }

    void BlockDown(CharacterSnapshot me, int direction)
    {
        bool rewarded = HostState.RewardShown;
        if (_blockSince >= 0 || _clock < _blockReady && !rewarded) return;

        _blockSince = _clock;
        _blockOrderWasOn = (me.Orders & CharacterOrders.Block) != 0;
        bool order = settings.Classic || settings.AiBlocks;
        if (order && !_blockOrderWasOn) Send(new OrdersCommand([me.Id], CharacterOrders.Block, On: true) { Tick = Now });

        Send(new CombatPress(me.Id, PressKind.Block, direction) { Tick = Now });
    }

    void BlockUp(CharacterSnapshot me)
    {
        _blockSince = -1;
        _blockReady = _clock + settings.BlockCooldownMs / 1000.0;
        if (!_blockOrderWasOn) Send(new OrdersCommand([me.Id], CharacterOrders.Block, On: false) { Tick = Now });

        Send(new CombatPress(me.Id, PressKind.BlockUp) { Tick = Now });
    }

    /// <summary>
    /// Past the double-press guard since the key's last press.
    /// </summary>
    bool Fresh(Key key)
    {
        double guard = settings.InputRepeatGuardMs / 1000.0;
        if (_lastPress.TryGetValue(key, out double last) && _clock - last < guard) return false;

        _lastPress[key] = _clock;
        return true;
    }

    static Key KeyOf(string name, Key fallback) =>
        Enum.TryParse<Key>(name.Trim(), ignoreCase: true, out var key) && Enum.IsDefined(key) ? key : fallback;

    /// <summary>
    /// The Action RPG scheme's lock-on: the enemy under the crosshair, or off.
    /// </summary>
    void ToggleLock()
    {
        if (!_lock.IsNone)
        {
            _lock = CharacterId.None;
            return;
        }

        if (HostAccess.Session?.CurrentSnapshot is { } snapshot && HostAccess.Find(snapshot, Controlled) is { } me && AimTarget(me) is { } target)
            _lock = target.Id;
    }

    /// <summary>
    /// A locked view turns to the target every tick, so W and S close in and back off and A and D circle it.
    /// </summary>
    void FollowLock(CharacterSnapshot me)
    {
        if (_lock.IsNone || HostAccess.Session?.CurrentSnapshot is not { } snapshot) return;
        if (HostAccess.Find(snapshot, _lock) is not { Body: not { Dead: true } } target)
        {
            _lock = CharacterId.None;
            return;
        }

        var to = target.Position - me.Position;
        if (to.X * to.X + to.Z * to.Z > 1) LookYaw = MathF.Atan2(-to.X, -to.Z);
    }

    /// <summary>
    /// Where the crosshair meets the ground near the character (the view's ray down to the character's height, at most 300 units off).
    /// </summary>
    Vector3? CrosshairGround()
    {
        if (HostAccess.Session?.CurrentSnapshot is not { } snapshot || HostAccess.Find(snapshot, Controlled) is not { } me) return null;

        float reach = LookPitch < -0.05f ? MathF.Min(300, settings.OtsHeight / MathF.Tan(-LookPitch) + settings.OtsDistance * Zoom) : 100;
        var forward = new Vector3(-MathF.Sin(LookYaw), 0, -MathF.Cos(LookYaw));
        return me.Position + forward * reach;
    }
}
