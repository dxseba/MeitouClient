using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Rendering.Sky;
using Meitou.Modding;

namespace Meitou.CheatMenu;

/// <summary>
/// The host-side cheats the mod API has no call for, reached by reflection on the game's host from the host thread only (the window's actions):
/// Next weather here and Next season here (the renderer's weather scheduler, which the world reads each frame as its weather input, as the
/// original's weather manager), Free camera (the camera rig's own toggle, as the free camera key) and the Level editor (the host's own). The game
/// host is taken from <see cref="ModFrame"/>'s private host field once a frame; nothing of the frame is kept. A member that is gone leaves the
/// button saying "not changed" and logs once.
/// </summary>
internal static class HostBridge
{
    static readonly FieldInfo? HostField = AccessTools.Field(typeof(ModFrame), "_host");
    static readonly MethodInfo? CameraToggle = AccessTools.Method(typeof(CameraRig), "Toggle");
    static readonly MethodInfo? SeasonEnd = AccessTools.PropertySetter(typeof(Meitou.Data.World.WeatherRegion), "SeasonEndDay");

    static WeakReference<object>? s_host;
    static ModContext? s_context;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        if (HostField is null || CameraToggle is null || SeasonEnd is null)
            context.LogError("a host member is gone: some of Free camera, Next weather and Next season do nothing");

        context.AddFrame(frame =>
        {
            if (HostField?.GetValue(frame) is { } host && (s_host is null || !s_host.TryGetTarget(out var old) || !ReferenceEquals(old, host)))
                s_host = new WeakReference<object>(host);
        });
    }

    static object? Host => s_host is { } weak && weak.TryGetTarget(out var host) ? host : null;

    static object? Member(object? owner, string name) => owner is null ? null
        : AccessTools.Field(owner.GetType(), name)?.GetValue(owner) ?? AccessTools.Property(owner.GetType(), name)?.GetValue(owner);

    static WorldWeather? Weather => Member(Member(Host, "Gpu"), "Weather") as WorldWeather;

    static CameraRig? Camera => (Member(Host, "Session") as WorldSession)?.Camera;

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a host cheat failed: {e}");
    }

    /// <summary>The camera region's next weather of its season (the original cycles the list); its name, or null when unchanged.</summary>
    internal static string? NextWeather()
    {
        try
        {
            if (Weather is not { } weather || weather.Choices() is not { Count: > 0 } choices) return null;

            int next = (Math.Max(0, weather.Current()) + 1) % choices.Count;
            weather.Choose(next);
            return choices[next];
        }
        catch (Exception e)
        {
            Fault(e);
            return null;
        }
    }

    /// <summary>
    /// The camera region's next season now (the original steps the season index and starts its weather): the season is made due and the region's
    /// own update rolls it, as when its days run out. Its name, or null when unchanged.
    /// </summary>
    internal static string? NextSeason(System.Numerics.Vector3 camera)
    {
        try
        {
            if (Weather?.World is not { } world || SeasonEnd is null) return null;

            var region = world.RegionAt(camera.X, camera.Z);
            SeasonEnd.Invoke(region, [world.Time.Day]);
            region.Update(world.Time);
            world.Snap();
            return region.Season.Season.Name;
        }
        catch (Exception e)
        {
            Fault(e);
            return null;
        }
    }

    /// <summary>Whether the free camera is on (false without a camera).</summary>
    internal static bool FreeCamera => Camera?.IsFree ?? false;

    /// <summary>Switches the free camera as its key does; false when it could not.</summary>
    internal static bool ToggleFreeCamera()
    {
        try
        {
            if (Camera is not { } rig || CameraToggle is null) return false;

            CameraToggle.Invoke(rig, null);
            return true;
        }
        catch (Exception e)
        {
            Fault(e);
            return false;
        }
    }

    /// <summary>Opens the game's level editor (Shift+F12); false when it could not.</summary>
    internal static bool OpenLevelEditor()
    {
        try
        {
            if (Member(Host, "LevelEditorHost") is not { } editor) return false;
            if (AccessTools.Method(editor.GetType(), "OpenLevelEditor") is not { } open) return false;

            open.Invoke(editor, null);
            return true;
        }
        catch (Exception e)
        {
            Fault(e);
            return false;
        }
    }
}
