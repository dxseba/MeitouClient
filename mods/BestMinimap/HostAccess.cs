using System.Numerics;
using System.Reflection;
using Meitou.Data.Ogre;
using Meitou.Game.Host;
using Meitou.Modding;

namespace Meitou.BestMinimap;

/// <summary>
/// What the code-mod API does not offer, reached by reflection on the game's internal host (host thread only): the asset locator the big map
/// finds its picture with (<c>GameHost.Assets</c>), and the camera move of a double click (<c>GameHost.Session.Camera.MovePivot</c>, which
/// keeps the camera's angle and distance, after clearing <c>PlayerInterface.Follow</c> so a followed character no longer pulls it back, as the
/// original's README says). Each member is looked up once; one that is missing turns its feature off with one log line.
/// </summary>
static class HostAccess
{
    static readonly FieldInfo? HostField = typeof(ModFrame).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic);

    static FieldInfo? s_assets, s_session, s_player;
    static object? s_host;
    static PropertyInfo? s_follow;
    static readonly HashSet<string> Reported = [];

    public static Action<string>? Log;

    /// <summary>The host was taken from a frame.</summary>
    public static bool Ready => s_host is not null;

    /// <summary>The game's file for an asset name (mods' replacements included); null when it cannot be asked.</summary>
    public static string? FindAsset(string name)
    {
        if (s_host is not { } host) return null;

        s_assets ??= host.GetType().GetField("Assets", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (s_assets?.GetValue(host) is not AssetLocator assets) return Missing("GameHost.Assets");

        return assets.Find(name);
    }

    /// <summary>Puts the camera's pivot on world <paramref name="point"/> (X, Z), keeping its angle and distance; false when it cannot.</summary>
    public static bool MoveCamera(Vector2 point)
    {
        if (s_host is not { } host) return false;

        s_session ??= host.GetType().GetField("Session", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (s_session?.GetValue(host) is not WorldSession session)
        {
            Missing("GameHost.Session");
            return false;
        }

        StopFollowing(host);
        session.Camera.MovePivot(point);
        return true;
    }

    /// <summary>The running world's population data (factions, relations, towns: <c>GameHost.PopData</c>); null before a world.</summary>
    public static Simulation.Population.PopulationData? Population()
    {
        if (s_host is not { } host) return null;

        s_population ??= host.GetType().GetField("PopData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (s_population is null)
        {
            Missing("GameHost.PopData");
            return null;
        }

        return s_population.GetValue(host) as Simulation.Population.PopulationData;
    }

    static FieldInfo? s_population;

    static void StopFollowing(object host)
    {
        s_player ??= host.GetType().GetField("Player", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (s_player?.GetValue(host) is not { } player) return;

        s_follow ??= player.GetType().GetProperty("Follow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        s_follow?.SetValue(player, null);
    }

    /// <summary>Takes the game's host from a frame (once; the same host serves every world of the session).</summary>
    public static void Capture(ModFrame frame)
    {
        if (s_host is not null) return;

        try
        {
            s_host = HostField?.GetValue(frame) ?? Missing("ModFrame._host");
        }
        catch (Exception e) when (e is TargetException or FieldAccessException)
        {
            Missing("ModFrame._host");
        }
    }

    static string? Missing(string member)
    {
        if (Reported.Add(member)) Log?.Invoke($"{member} not found: this game build is not the one the port was made for");
        return null;
    }
}
