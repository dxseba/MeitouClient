using System.Diagnostics;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.BestMinimap;

/// <summary>
/// What the minimap shows this frame, taken from <see cref="ModFrame"/> once a frame on the host's thread (the interface's overlay is built on the
/// same thread right after): the snapshot, camera, selection, pointer and screen, the population data, and the view (<see cref="Projection"/>).
/// Display state only; nothing here reaches the world except the orders queued by <see cref="MinimapInput"/>.
/// </summary>
static class Minimap
{
    /// <summary>The original's <c>Range</c> limits and zoom factors, and the size limits and step (<b>Verified</b>, decompile).</summary>
    public const float MinRange = 30, MaxRange = 150000, ZoomIn = 0.8f, ZoomOut = 1.25f;
    public const int MinSize = 80, MaxSize = 900, SizeStep = 20, Margin = 10;

    public static MinimapSettings Settings = new();
    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static WorldSnapshot? Snapshot;
    public static PopulationData? Population;
    public static Vector3 CameraEye, CameraTarget;
    public static Vector2 Pointer, ScreenSize;
    public static bool Ctrl, Shown;
    public static IReadOnlyList<CharacterId> Selected = [];
    public static Projection View;
    public static Vector2 Heading;

    /// <summary>Where a right click last sent the selected, and when (the green flag).</summary>
    public static Vector2? MoveFlag;
    public static double MoveFlagAt;

    /// <summary>The drag's position offset not yet kept in the settings (pixels).</summary>
    public static Vector2? DragPlace;
    public static int? DragSize;

    static double s_lastFrame = -10;

    public static double Now => Clock.Elapsed.TotalSeconds;

    /// <summary>The frame seen recently (not over the title or a loading cover).</summary>
    public static bool Live => Now - s_lastFrame < 0.5 && Snapshot is not null;

    public static void Frame(ModFrame frame)
    {
        s_lastFrame = Now;
        Snapshot = frame.Snapshot;
        HostAccess.Capture(frame);
        Population = HostAccess.Population();
        (CameraEye, CameraTarget) = (frame.CameraEye, frame.CameraTarget);
        (Pointer, ScreenSize, Ctrl) = (frame.Pointer, frame.ScreenSize, frame.Ctrl);
        Selected = frame.Selected;
        var s = Settings;
        Shown = s.Enabled && s.Visible && frame.UiVisible && !frame.IsOpen("overview");
        var look = new Vector2(CameraTarget.X - CameraEye.X, CameraTarget.Z - CameraEye.Z);
        if (look.LengthSquared() > 1e-6f) Heading = Vector2.Normalize(look);
        View = MakeView();
    }

    /// <summary>The minimap's size on screen: a circle or square of Size, a 16:10 rectangle Size wide.</summary>
    public static Vector2 BoxSize(int size) => Settings.IsShape(2) ? new(size, MathF.Round(size * 10f / 16)) : new(size, size);

    /// <summary>The camera's heading, clockwise from map north (-Z), radians.</summary>
    public static float HeadingAngle => MathF.Atan2(Heading.X, -Heading.Y);

    static Projection MakeView()
    {
        var s = Settings;
        var size = BoxSize(DragSize ?? s.Size);
        var place = DragPlace ?? new Vector2(s.Left, s.Top);
        float x = place.X < 0 ? ScreenSize.X - size.X - Margin : place.X;
        float y = place.Y < 0 ? Margin : place.Y;
        x = Math.Clamp(x, 0, MathF.Max(0, ScreenSize.X - size.X));
        y = Math.Clamp(y, 0, MathF.Max(0, ScreenSize.Y - size.Y));
        var rect = new UiRect(x, y, size.X, size.Y);
        float scale = MathF.Min(size.X, size.Y) / 2 / Math.Clamp(s.Range, MinRange, MaxRange);
        float angle = s.RotateWithCamera && ImagePatch.Active ? -HeadingAngle : 0;
        return new(rect, Centre(), scale, angle, s.IsShape(0));
    }

    /// <summary>The camera's target, or the selected characters' middle with "Centre on the selected".</summary>
    static Vector2 Centre()
    {
        var camera = new Vector2(CameraTarget.X, CameraTarget.Z);
        if (!Settings.CenterOnSelected || Snapshot is not { } snap || Selected.Count == 0) return camera;

        var sum = Vector2.Zero;
        int n = 0;
        foreach (var c in snap.Characters)
        {
            if (!c.Selected) continue;

            sum += new Vector2(c.Position.X, c.Position.Z);
            n++;
        }

        return n > 0 ? sum / n : camera;
    }

    /// <summary>How far the camera may go from the player's characters: the setting, else the game's leash (2250).</summary>
    public static float CameraReach => Settings.CameraRange > 0 ? Settings.CameraRange : KenshiCamera.LeashRadius;

    /// <summary>The player's characters (the leash's anchors).</summary>
    public static IEnumerable<CharacterSnapshot> PlayerCharacters() => Snapshot?.Characters.Where(c => c.IsPlayer) ?? [];

    /// <summary>The player's faction: the first player character's.</summary>
    public static int PlayerFaction => PlayerCharacters().Select(c => c.Faction).FirstOrDefault(f => f >= 0, -1);
}
