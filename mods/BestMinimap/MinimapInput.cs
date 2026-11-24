using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.BestMinimap;

/// <summary>
/// The original's controls: the show / hide, zoom and size keys (zoom repeats while held), the wheel over the minimap (zoom; with Ctrl the size),
/// a left drag (moves it unless locked; from the lower right corner it resizes), a right click (the selected go there, as a right click on the
/// ground) and a double left click (the camera goes there, when that spot is within the camera's reach of a player character). Position, size,
/// zoom and the shown state are kept in the settings, as the original writes them to its ini.
/// </summary>
static class MinimapInput
{
    const double DoubleClickSeconds = 0.4, RepeatDelay = 0.4, RepeatGap = 0.12;
    const float ClickSlop = 4, CornerGrip = 16;

    public static Action<string, object>? Keep;
    public static Action<string>? Debug;
    public static Func<long>? Tick;

    static double s_lastClick = -10;
    static Vector2 s_lastClickAt;
    static bool s_dragged, s_resizing;
    static Vector2 s_dragTotal;
    static float s_sizeStart;
    static double s_heldSince, s_lastRepeat;

    #region Keys

    public static void Toggle(ModKeyContext _) => Keep?.Invoke(nameof(MinimapSettings.Visible), !Minimap.Settings.Visible);

    public static void Zoom(float factor)
    {
        float range = Math.Clamp(Minimap.Settings.Range * factor, Minimap.MinRange, Minimap.MaxRange);
        Keep?.Invoke(nameof(MinimapSettings.Range), (int)MathF.Round(range));
    }

    public static void Resize(int step)
    {
        int size = Math.Clamp(Minimap.Settings.Size + step, Minimap.MinSize, Minimap.MaxSize);
        Keep?.Invoke(nameof(MinimapSettings.Size), size);
    }

    /// <summary>Repeats the zoom while its key stays down (the original's "hold - repeat").</summary>
    public static void Repeat(ModKeyAction zoomIn, ModKeyAction zoomOut)
    {
        bool inHeld = zoomIn.Held, outHeld = zoomOut.Held;
        double now = Minimap.Now;
        if (!inHeld && !outHeld)
        {
            s_heldSince = now;
            return;
        }

        if (now - s_heldSince < RepeatDelay || now - s_lastRepeat < RepeatGap) return;

        s_lastRepeat = now;
        Zoom(inHeld ? Minimap.ZoomIn : Minimap.ZoomOut);
    }

    #endregion

    #region Pointer

    /// <summary>An event of the minimap's body node.</summary>
    public static void Pointer(UiActionContext c)
    {
        switch (c.Kind)
        {
            case UiEventKind.Wheel:
                Wheel(c.Wheel);
                break;
            case UiEventKind.Press when c.Button == MouseButton.Right:
                RightClick(c.Position);
                break;
            case UiEventKind.Press when c.Button == MouseButton.Left:
                Pressed(c.Position);
                break;
            case UiEventKind.Drag:
                Dragged(c.Delta);
                break;
            case UiEventKind.Release:
                Released(c.Position);
                break;
        }
    }

    static void Wheel(float steps)
    {
        if (steps == 0) return;

        if (Minimap.Ctrl) Resize(steps > 0 ? Minimap.SizeStep : -Minimap.SizeStep);
        else Zoom(steps > 0 ? Minimap.ZoomIn : Minimap.ZoomOut);
    }

    static void Pressed(Vector2 at)
    {
        var r = Minimap.View.Rect;
        s_dragged = false;
        s_dragTotal = Vector2.Zero;
        s_resizing = at.X > r.X + r.Width - CornerGrip && at.Y > r.Y + r.Height - CornerGrip;
        s_sizeStart = Minimap.Settings.Size;
    }

    static void Dragged(Vector2 delta)
    {
        s_dragTotal += delta;
        if (!s_dragged && s_dragTotal.Length() < ClickSlop) return;
        if (Minimap.Settings.Locked) return;

        s_dragged = true;
        if (s_resizing)
        {
            Minimap.DragSize = (int)Math.Clamp(s_sizeStart + s_dragTotal.X, Minimap.MinSize, Minimap.MaxSize);
            return;
        }

        var r = Minimap.View.Rect;
        Minimap.DragPlace = (Minimap.DragPlace ?? new Vector2(r.X, r.Y)) + delta;
    }

    static void Released(Vector2 at)
    {
        if (s_dragged)
        {
            KeepDrag();
            return;
        }

        double now = Minimap.Now;
        bool second = now - s_lastClick < DoubleClickSeconds && Vector2.Distance(at, s_lastClickAt) < ClickSlop * 2;
        (s_lastClick, s_lastClickAt) = (second ? -10 : now, at);
        if (second && Minimap.Settings.DoubleClickCamera) CameraTo(Minimap.View.ToWorld(at));
    }

    static void KeepDrag()
    {
        if (Minimap.DragSize is { } size) Keep?.Invoke(nameof(MinimapSettings.Size), size);
        if (Minimap.DragPlace is not null)
        {
            var r = Minimap.View.Rect;
            Keep?.Invoke(nameof(MinimapSettings.Left), (int)MathF.Round(r.X));
            Keep?.Invoke(nameof(MinimapSettings.Top), (int)MathF.Round(r.Y));
        }

        (Minimap.DragPlace, Minimap.DragSize) = (null, null);
    }

    static void RightClick(Vector2 at)
    {
        if (!Minimap.Settings.RightClickMove || Minimap.Selected.Count == 0 || Minimap.Snapshot is null) return;

        var world = Minimap.View.ToWorld(at);
        var target = new Vector3(world.X, Minimap.CameraTarget.Y, world.Y);
        LastFrameEnqueue?.Invoke(new MoveOrder([.. Minimap.Selected], target) { Tick = Tick?.Invoke() ?? 0 });
        (Minimap.MoveFlag, Minimap.MoveFlagAt) = (world, Minimap.Now);
        Debug?.Invoke($"move order to {target.X:0} {target.Y:0} {target.Z:0}");
    }

    /// <summary>Queues a command for the world (the mod's context).</summary>
    public static Action<SimCommand>? LastFrameEnqueue;

    static void CameraTo(Vector2 world)
    {
        float nearest = Minimap.PlayerCharacters()
            .Select(c => Vector2.Distance(world, new(c.Position.X, c.Position.Z)))
            .DefaultIfEmpty(float.MaxValue)
            .Min();
        if (nearest > Minimap.CameraReach)
        {
            Debug?.Invoke($"camera spot {nearest:0} from own people, range {Minimap.CameraReach:0} - not moved");
            return;
        }

        if (HostAccess.MoveCamera(world)) Debug?.Invoke($"camera to {world.X:0} {world.Y:0}");
    }

    #endregion
}
