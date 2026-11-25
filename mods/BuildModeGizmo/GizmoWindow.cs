using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Construction;

namespace Meitou.BuildModeGizmo;

/// <summary>
/// The transform window and the arrows on the selected site (the original's editor gizmo and <c>TransformWindow</c>): selecting one of the player's
/// unbuilt sites opens them; the arrows stay while it is selected and through clicks on the ground, and go when another object is selected, the
/// site is finished or gone, or the window is closed with its cross (then they come back only once something else was selected). Dragging an arrow
/// moves the site along it (the rotation handle turns it), and the release sends the move; the window also takes typed coordinates and a yaw, steps
/// and a reset to where the site stood when it was selected. Interface thread only; the move is a command.
/// </summary>
sealed partial class GizmoWindow(ModContext context, GizmoSettings settings) : IUiExtension
{
    /// <summary>
    /// The site the arrows are on (null: none), and the one closed with the cross.
    /// </summary>
    string? _site, _dismissed;

    /// <summary>
    /// Where the site stood when it was selected (Reset), and the place and yaw shown (moved by the arrows and the fields before Apply).
    /// </summary>
    Vector3 _origin, _shown;
    Quaternion _originRotation;
    float _yaw;

    /// <summary>
    /// The site's name for the window's title (the original sets it, its 2026-10-15 change).
    /// </summary>
    string _name = "";

    /// <summary>
    /// The window's mode: false the move arrows, true the rotation handle.
    /// </summary>
    bool _rotate;

    /// <summary>
    /// The arrows' screen points of the last frame (<see cref="Frame"/>).
    /// </summary>
    volatile ScreenGizmo? _screen;

    public string Id => "dxseba.buildmodegizmo.ui";

    public void Register(UiRegistration registration)
    {
        var styles = RegisterStyles(registration);
        var actions = RegisterActions(registration);
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 57);
        registration.AddBuildingActions(Selected);
        registration.AddKeyInput(input => _site is not null && input.Cancel && Close());
    }

    /// <summary>
    /// The selection panel asks for its buttons whenever a building is selected: an own unbuilt site opens the arrows (unless closed on it), any
    /// other selection takes them away; a closed site offers a button to bring them back.
    /// </summary>
    IEnumerable<UiBuildingAction> Selected(UiBuildingContext ui)
    {
        if (!settings.Enabled || !settings.SiteArrows) return [];

        var site = Movable(ui);
        if (site is null)
        {
            (_site, _dismissed) = (null, null);
            return [];
        }

        if (site.Id == _dismissed) return [new UiBuildingAction(context.Tr("Move arrows"), null, () => Open(site, ui.Building.Name))];
        if (site.Id != _site) Open(site, ui.Building.Name);

        return [];
    }

    BuildingSnapshot? Movable(UiBuildingContext ui)
    {
        var building = ui.Building;
        if (!building.Owned || building.SiteId is not { } id) return null;

        var site = ui.Snapshot.Buildings.FirstOrDefault(b => b.Id == id);
        return site is { Complete: false, LevelPlacement: null, Parent: null, WallParent: null } ? site : null;
    }

    void Open(BuildingSnapshot site, string name)
    {
        (_site, _dismissed, _name, _rotate) = (site.Id, null, name, false);
        (_origin, _originRotation) = (site.Position, site.Rotation);
        (_shown, _yaw, _seen) = (site.Position, YawOf(site.Rotation), site.Position);
        SetFields();
    }

    bool Close()
    {
        (_dismissed, _site) = (_site, null);
        return true;
    }

    /// <summary>
    /// The move as it is shown, sent to the simulation (the release of an arrow, Apply, a step, Reset).
    /// </summary>
    void Apply()
    {
        if (_site is not { } site) return;

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw - YawOf(_originRotation)) * _originRotation;
        context.Enqueue(new MoveSite(site, _shown, rotation, settings.SnapToGround) { Tick = context.Tick });
        SetFields();
    }

    void Reset()
    {
        (_shown, _yaw) = (_origin, YawOf(_originRotation));
        Apply();
    }

    /// <summary>
    /// The heading of a rotation about the up axis (its tilt left out), radians: the turned +Z's direction on the ground.
    /// </summary>
    static float YawOf(Quaternion q)
    {
        var forward = Vector3.Transform(Vector3.UnitZ, q);
        return MathF.Atan2(forward.X, forward.Z);
    }

    /// <summary>
    /// Each frame (<c>AddFrame</c>): the site is followed in the snapshot (gone or complete: the arrows close) and the arrows projected.
    /// </summary>
    public void Frame(ModFrame frame)
    {
        if (_site is not { } id)
        {
            _screen = null;
            return;
        }

        var site = frame.Snapshot.Buildings.FirstOrDefault(b => b.Id == id);
        if (site is null || site.Complete)
        {
            (_site, _screen) = (null, null);
            return;
        }

        Follow(site);
        _screen = ScreenGizmo.Of(frame, _shown);
    }

    /// <summary>
    /// The place the simulation gave the site (a move applied, its height settled) becomes the shown one.
    /// </summary>
    void Follow(BuildingSnapshot site)
    {
        if (site.Position == _seen) return;

        _seen = site.Position;
        (_shown, _yaw) = (site.Position, YawOf(site.Rotation));
        SetFields();
    }

    Vector3 _seen;

    static string Number(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    static float? Parse(string text) =>
        float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v) ? v : null;
}

/// <summary>
/// The arrows on screen: the site's point and the tips of its X, Z and up axes (null: behind the camera), and how many pixels one world unit
/// along each is.
/// </summary>
sealed record ScreenGizmo(Vector2 Centre, Vector2? X, Vector2? Y, Vector2? Z)
{
    /// <summary>
    /// The arrows' length in world units (engine choice).
    /// </summary>
    public const float Length = 30;

    public static ScreenGizmo? Of(ModFrame frame, Vector3 at)
    {
        if (frame.Project(at) is not { } centre) return null;

        return new ScreenGizmo(centre, frame.Project(at + Vector3.UnitX * Length), frame.Project(at + Vector3.UnitY * Length),
            frame.Project(at + Vector3.UnitZ * Length));
    }

    /// <summary>
    /// The world distance along an axis whose tip is drawn at <paramref name="tip"/> for a pointer move of <paramref name="delta"/> pixels.
    /// </summary>
    public float Along(Vector2? tip, Vector2 delta)
    {
        if (tip is not { } t) return 0;

        var axis = t - Centre;
        float pixels = axis.LengthSquared();
        return pixels < 1 ? 0 : Vector2.Dot(delta, axis) / pixels * Length;
    }
}
