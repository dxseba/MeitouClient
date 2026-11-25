using System.Numerics;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace Meitou.DirectControl;

/// <summary>
/// The views direct control has: the game's camera following the character, first person at its eyes, the third-person camera over its shoulder.
/// </summary>
enum View : byte
{
    Normal,
    FirstPerson,
    Shoulder,
}

/// <summary>
/// The host side of direct control, on the host's thread only: the on/off state, the driven character, the view and its mouse look, and each
/// control tick's keys turned into commands for <see cref="ControlSystem"/>. The camera keys the character's movement takes (the game's camera
/// move, and its turn keys while the combat keys or a locked view use them) are taken out of the tick's actions before the strategy camera reads
/// them (<see cref="Patches"/>: Harmony on <c>CameraRig.Update</c>).
/// </summary>
sealed partial class Pilot(ModContext context, ControlSettings settings)
{
    /// <summary>
    /// Radians the view turns per mouse pixel at sensitivity 1 (engine choice).
    /// </summary>
    const float LookPerPixel = 0.0025f;

    /// <summary>
    /// The highest and lowest the view looks, in radians.
    /// </summary>
    const float PitchLimit = 1.35f;

    /// <summary>
    /// The running pilot, for the patches.
    /// </summary>
    public static Pilot? Current { get; set; }

    public ControlSettings Settings => settings;

    public bool On { get; private set; }

    public CharacterId Controlled { get; private set; } = CharacterId.None;

    public View View { get; private set; }

    public bool Manual { get; private set; }

    /// <summary>
    /// The view's yaw and pitch in first person and the third-person camera (the strategy camera's conventions: yaw 0 looks along -Z; pitch positive
    /// looks up).
    /// </summary>
    public float LookYaw { get; private set; }

    public float LookPitch { get; private set; }

    /// <summary>
    /// The third-person camera's side: 1 right of the character, -1 left (Swap shoulder).
    /// </summary>
    public float Shoulder { get; private set; } = 1;

    /// <summary>
    /// The wheel's zoom of the third-person camera, a factor on its distance.
    /// </summary>
    public float Zoom { get; private set; } = 1;

    /// <summary>
    /// The mouse turns the view and the pointer is held (first person or the third-person camera, no window that needs the mouse, no Alt).
    /// </summary>
    public bool LookActive { get; private set; }

    /// <summary>
    /// The world the state belongs to: a new game or a load turns direct control off.
    /// </summary>
    World? _world;

    /// <summary>
    /// The last steer sent (to send only changes).
    /// </summary>
    (Vector2 Direction, byte? Mode, bool Face, float Yaw) _sent;

    /// <summary>
    /// One control tick (host thread, before the strategy camera reads <paramref name="actions"/>).
    /// </summary>
    public void ControlTick(ActionState actions)
    {
        if (!NoteWorld() || !On) return;

        var snapshot = HostAccess.Session?.CurrentSnapshot;
        if (snapshot is null || HostAccess.Find(snapshot, Controlled) is not { } me || !Fit(me))
        {
            TurnOff(context.Tr("Direct Control is off: your character cannot be controlled."));
            return;
        }

        LookActive = View != View.Normal && !HostAccess.InterfaceBusy && !HostAccess.Covered && !actions.Input.IsDown(Key.LeftAlt)
            && !actions.Input.IsDown(Key.RightAlt);
        Look(actions);
        Steer(actions, me);
        TakeCameraKeys(actions);
        CombatKeys(actions, me);
    }

    /// <summary>
    /// Whether the world is still the one the state was made in; a new one resets it all.
    /// </summary>
    bool NoteWorld()
    {
        var world = HostAccess.Session?.World;
        if (ReferenceEquals(world, _world)) return true;

        _world = world;
        (On, Controlled, View, Manual, LookActive) = (false, CharacterId.None, View.Normal, false, false);
        ClearCombatKeys();
        return false;
    }

    /// <summary>
    /// A character direct control can drive: the player's, alive and awake.
    /// </summary>
    static bool Fit(CharacterSnapshot c) => c.IsPlayer && c.Body is not { Dead: true } and not { Unconscious: true };

    /// <summary>
    /// The mouse turns the view while it is held (in the Action RPG scheme not while a swing direction is being drawn).
    /// </summary>
    void Look(ActionState actions)
    {
        if (!LookActive || Drawing) return;

        float k = LookPerPixel * (View == View.FirstPerson ? settings.Sensitivity : settings.OtsSensitivity);
        var d = actions.MouseDelta;
        LookYaw -= d.X * k;
        LookPitch = Math.Clamp(LookPitch - d.Y * k, -PitchLimit, PitchLimit);
        if (View == View.Shoulder && actions.Wheel != 0) Wheel(actions.Wheel);
    }

    /// <summary>
    /// The wheel zooms the third-person camera; in the Action RPG scheme the nearest step is first person and a step out from it returns.
    /// </summary>
    void Wheel(float notches)
    {
        Zoom = Math.Clamp(Zoom * MathF.Pow(0.85f, notches), 0.25f, 6f);
        if (!settings.Classic && Zoom <= 0.25f && notches > 0) SetView(View.FirstPerson);
    }

    /// <summary>
    /// WASD (the game's camera move keys) as a ground direction relative to the view, sent when it changes.
    /// </summary>
    void Steer(ActionState actions, CharacterSnapshot me)
    {
        float forward = (actions.Held(InputAction.MoveForward) ? 1 : 0) - (actions.Held(InputAction.MoveBack) ? 1 : 0);
        float right = (actions.Held(InputAction.MoveRight) ? 1 : 0) - (actions.Held(InputAction.MoveLeft) ? 1 : 0);
        float yaw = View == View.Normal ? HostAccess.Camera?.Yaw ?? 0 : LookYaw;
        var f = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
        var r = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
        var wanted = f * forward + r * right;
        var direction = wanted == Vector2.Zero || Blocking || FaceCam ? Vector2.Zero : Quantise(Vector2.Normalize(wanted));
        GetUp(me, direction);

        bool face = View == View.FirstPerson;
        float bodyYaw = Quantise(yaw + MathF.PI);
        var sent = (direction, Mode(actions), face, face ? bodyYaw : 0f);
        if (sent == _sent) return;

        _sent = sent;
        Send(new SteerCommand(me.Id, direction, sent.Item2, face, bodyYaw) { Tick = Now });
    }

    bool _gotUp;

    /// <summary>
    /// Get up and go: WASD on a character sitting, lying in a bed or working at a machine gives it the game's own move order a step ahead (as a
    /// right click does), once per press; the direct step takes over once it stands.
    /// </summary>
    void GetUp(CharacterSnapshot me, Vector2 direction)
    {
        if (direction == Vector2.Zero)
        {
            _gotUp = false;
            return;
        }

        if (_gotUp || !HostState.Stuck) return;

        _gotUp = true;
        Send(new MoveOrder([me.Id], me.Position + new Vector3(direction.X, 0, direction.Y) * 30) { Tick = Now });
    }

    /// <summary>
    /// The walk mode: the character's own run speed in the Direct Control scheme; in the Action RPG scheme a walk, a run while Shift is held or the
    /// run lock is on.
    /// </summary>
    byte? Mode(ActionState actions)
    {
        if (settings.Classic) return null;

        bool run = _runLock || actions.Input.IsDown(Key.LeftShift) || actions.Input.IsDown(Key.RightShift);
        return run ? SpeedMode.Free : SpeedMode.Walk;
    }

    /// <summary>
    /// A direction on a 1/1024 turn grid, so a camera drifting by a hair sends no new command.
    /// </summary>
    static Vector2 Quantise(Vector2 d)
    {
        float a = Quantise(MathF.Atan2(d.X, d.Y));
        return new Vector2(MathF.Sin(a), MathF.Cos(a));
    }

    static float Quantise(float angle)
    {
        const float step = MathF.Tau / 1024;
        return MathF.Round(angle / step) * step;
    }

    /// <summary>
    /// Takes the camera move keys out of the tick (the character walks with them); in a locked view or with the combat keys on, the turn keys too.
    /// </summary>
    void TakeCameraKeys(ActionState actions)
    {
        Clear(actions, InputAction.MoveForward, InputAction.MoveBack, InputAction.MoveLeft, InputAction.MoveRight);
        if (View != View.Normal || Manual) Clear(actions, InputAction.RotateLeft, InputAction.RotateRight);
    }

    static void Clear(ActionState actions, params InputAction[] taken)
    {
        foreach (var action in taken) Patches.SetAction?.Invoke(actions, action, false, false, false);
    }

    void Send(SimCommand command) => context.Enqueue(command);

    long Now => context.Tick;

    /// <summary>
    /// Direct control on, on <paramref name="id"/>: the camera follows it.
    /// </summary>
    public void TurnOn(CharacterId id)
    {
        (On, Controlled) = (true, id);
        _sent = default;
        HostAccess.Follow = id;
        SendControl();
        if (settings.Classic) return;

        SetView(View.Shoulder);
        Send(new StanceCommand(id, true) { Tick = Now });
    }

    /// <summary>
    /// Direct control off (with a notice when given): the view goes back to the game's camera.
    /// </summary>
    public void TurnOff(string? notice = null)
    {
        if (!On) return;

        SetView(View.Normal);
        (On, Manual, LookActive) = (false, false, false);
        ClearCombatKeys();
        Controlled = CharacterId.None;
        SendControl();
        if (notice is not null) Notify(notice);
    }

    /// <summary>
    /// Tells the simulation who is driven and how (also after a settings change).
    /// </summary>
    public void SendControl() => Send(new ControlCommand(On ? Controlled : CharacterId.None, Manual, settings.Tuning()) { Tick = Now });

    public void SetView(View view)
    {
        if (view == View) return;

        if (view != View.Normal && View == View.Normal && HostAccess.Camera is { } camera)
        {
            LookYaw = camera.Yaw;
            LookPitch = view == View.FirstPerson ? 0 : -0.2f;
        }

        View = view;
        _sent = default;
        if (view != View.Shoulder) Zoom = 1;
    }

    /// <summary>
    /// The interface's notice line, next frame.
    /// </summary>
    void Notify(string text) => _notice = text;

    public void LogError(string message) => context.LogError(message);

    string? _notice;
}
