using System.Numerics;
using Meitou.Game.Rendering.Frame;

namespace Meitou.DirectControl;

/// <summary>
/// The drawn camera in first person and the third-person camera, set on the game's camera right before the frame takes its view (Harmony on
/// <c>GameHost.UpdateView</c>), so the picture, the picking and the projection all use it; the strategy camera underneath keeps following the
/// character (streaming, sound and the map arrow follow it). The near plane follows the camera's distance (a quarter of it, at least 0.5), so the
/// first-person eye sits that far behind the look point to give the clipping distance asked for.
/// </summary>
sealed partial class Pilot
{
    /// <summary>
    /// The field of view the game had before first person changed it.
    /// </summary>
    float? _gameFov;

    public void ApplyView(WorldCamera camera)
    {
        var snapshot = HostAccess.Session?.CurrentSnapshot;
        if (!On || snapshot is null || HostAccess.Find(snapshot, Controlled) is not { } me || View == View.Normal && !FaceCam)
        {
            RestoreFov(camera);
            return;
        }

        if (FaceCam) FaceCamera(camera, me);
        else if (View == View.FirstPerson) FirstPerson(camera, me);
        else Shoulder3(camera, me);
    }

    void FirstPerson(WorldCamera camera, Simulation.CharacterSnapshot me)
    {
        var forward = LookForward();
        var flat = new Vector3(-MathF.Sin(LookYaw), 0, -MathF.Cos(LookYaw));
        var head = HostAccess.Head(me.Id) ?? HostAccess.Drawn(me) + new Vector3(0, 17 * me.Scale, 0);
        var eye = head + new Vector3(0, settings.EyeDrop, 0) + flat * settings.EyeForward;

        // The near plane is a quarter of the distance (WorldCamera.Near, at least 0.5).
        float distance = MathF.Max(2, 4 * settings.NearClip);
        Place(camera, eye + forward * distance, distance);
        _gameFov ??= camera.FieldOfView;
        camera.FieldOfView = Math.Clamp(settings.Fov, 50, 110) * MathF.PI / 180;
        ScaleGrass(settings.GrassRangeMult);
    }

    /// <summary>
    /// The grass range the game had before first person scaled it.
    /// </summary>
    float? _gameGrass;

    void ScaleGrass(float mult)
    {
        if (HostAccess.Foliage is not { } foliage) return;

        _gameGrass ??= foliage.GrassRangeSetting;
        foliage.GrassRangeSetting = _gameGrass.Value * mult;
    }

    void Shoulder3(WorldCamera camera, Simulation.CharacterSnapshot me)
    {
        RestoreFov(camera);
        var right = new Vector3(MathF.Cos(LookYaw), 0, -MathF.Sin(LookYaw));
        var pivot = HostAccess.Drawn(me) + new Vector3(0, settings.OtsHeight, 0) + right * settings.OtsSide * Shoulder;
        Place(camera, pivot, Clear(pivot, settings.OtsDistance * Zoom));
    }

    /// <summary>
    /// The inventory face-cam: the camera in front of the character, looking at its chest, so the gear shows.
    /// </summary>
    void FaceCamera(WorldCamera camera, Simulation.CharacterSnapshot me)
    {
        RestoreFov(camera);
        camera.Target = HostAccess.Drawn(me) + new Vector3(0, 12 * me.Scale, 0);
        camera.Yaw = me.Yaw;
        camera.Pitch = 0.12f;
        camera.Distance = 32 * me.Scale;
    }

    /// <summary>
    /// The third-person camera eases in clear of walls (the original's wall avoidance): the farthest boom up to <paramref name="distance"/> whose
    /// eye the pivot sees (the world's sight test against walls and buildings, halved eight times) and that keeps above the ground and floors.
    /// </summary>
    float Clear(Vector3 pivot, float distance)
    {
        if (HostAccess.Session is not { } session) return distance;

        var back = -LookForward();
        var walk = session.World.Walkability;
        bool Sees(float d)
        {
            var eye = pivot + back * d;
            return walk.LineOfSight(pivot, eye) && eye.Y >= session.Camera.EyeGroundAt(eye.X, eye.Z, eye.Y) + 1;
        }

        if (Sees(distance)) return distance;

        float lo = 1, hi = distance;
        for (int i = 0; i < 8; i++)
        {
            float mid = (lo + hi) / 2;
            if (Sees(mid)) lo = mid;
            else hi = mid;
        }

        return lo;
    }

    /// <summary>
    /// The character's own inventory is open (and it is not fighting), with the face-cam on: the camera faces it and WASD waits.
    /// </summary>
    public bool FaceCam => settings.InventoryFaceCam && _inventoryOpen && !_inCombat;

    bool _inventoryOpen, _inCombat;

    /// <summary>
    /// First person shows every storey of the buildings in view (the original's "show floors below you").
    /// </summary>
    public bool RevealFloors => On && View == View.FirstPerson && settings.RevealFloors;

    /// <summary>
    /// The camera looking from <c>target - forward x distance</c> at <paramref name="target"/> along the view.
    /// </summary>
    void Place(WorldCamera camera, Vector3 target, float distance)
    {
        camera.Target = target;
        camera.Yaw = LookYaw;
        camera.Pitch = -LookPitch;
        camera.Distance = distance;
    }

    Vector3 LookForward() =>
        new(-MathF.Cos(LookPitch) * MathF.Sin(LookYaw), MathF.Sin(LookPitch), -MathF.Cos(LookPitch) * MathF.Cos(LookYaw));

    void RestoreFov(WorldCamera camera)
    {
        if (_gameGrass is { } grass && HostAccess.Foliage is { } foliage)
        {
            foliage.GrassRangeSetting = grass;
            _gameGrass = null;
        }

        if (_gameFov is not { } fov) return;

        camera.FieldOfView = fov;
        _gameFov = null;
    }

    /// <summary>
    /// The character whose head is hidden this frame (first person with the setting on), or none.
    /// </summary>
    public Simulation.CharacterId HiddenHead => On && View == View.FirstPerson && settings.HideHead ? Controlled : Simulation.CharacterId.None;
}
