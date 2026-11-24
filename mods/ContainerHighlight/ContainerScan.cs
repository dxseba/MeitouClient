using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Locks;

namespace Meitou.ContainerHighlight;

/// <summary>
/// A matched container: its fixture id (the building's placement key), its origin's height (where the outline stops) and its marker's world point.
/// </summary>
readonly record struct Highlighted(string Key, float Floor, Vector3 Marker);

/// <summary>
/// The highlight gate and scan, once a frame on the host's thread (<c>AddFrame</c>; <b>Verified</b>, DLL decompile, its scan step): while the gate
/// is open (the game's Highlight key, Alt as the original's default key, or the mod's own key held), every 150 ms (its <c>update_interval_ms</c>) the
/// containers with an inventory within the max distance (a sphere) of the HUD's selected character, else the camera's centre, that
/// <see cref="ContainerKinds"/> matches are kept, nearest first, at most 48 (the size its list is cut to). Closed, nothing is kept.
/// </summary>
sealed class ContainerScan(ContainerKinds kinds, Action<string> log)
{
    const long IntervalMs = 150, SummaryEveryMs = 5000;
    const int MaxKept = 48;

    ModKeyAction? _key;
    long _scannedAt, _summaryAt;

    /// <summary>
    /// The containers of the last scan (host thread); empty while the gate is closed.
    /// </summary>
    public IReadOnlyList<Highlighted> Current { get; private set; } = [];

    /// <summary>
    /// The markers' screen points of the last frame (those in front of the camera).
    /// </summary>
    public IReadOnlyList<Vector2> Markers { get; private set; } = [];

    /// <summary>
    /// Whether the gate was open at the last frame.
    /// </summary>
    public bool Open { get; private set; }

    static ContainerSettings Settings => ContainerHighlightMod.Settings;

    public void Install(ModContext context)
    {
        // A mod may not default to Alt, so the game's Highlight key (Alt) opens the gate too; the mod's own key is unbound until the player binds it.
        _key = context.AddKeyAction("highlight", "Highlight key", _ => { });
        context.AddFrame(Frame);
    }

    void Frame(ModFrame frame)
    {
        try
        {
            Open = Settings.Enabled && (frame.Highlight || _key?.Held == true);
            if (!Open)
            {
                (Current, Markers, _scannedAt) = ([], [], 0);
                return;
            }

            long now = Environment.TickCount64;
            if (_scannedAt == 0 || now - _scannedAt >= IntervalMs)
            {
                _scannedAt = now;
                Current = Scan(frame.Snapshot, Centre(frame), now);
            }

            Markers = Project(frame);
        }
        catch (Exception e)
        {
            // Nothing is shown this interval; the scan is tried again at the next.
            (Current, Markers) = ([], []);
            log($"scan failed: {e.Message}");
        }
    }

    List<Vector2> Project(ModFrame frame)
    {
        var points = new List<Vector2>(Current.Count);
        foreach (var item in Current)
        {
            if (frame.Project(item.Marker) is { } at) points.Add(at);
        }

        return points;
    }

    static Vector3 Centre(ModFrame frame)
    {
        if (frame.Shown is { } id)
        {
            foreach (var c in frame.Snapshot.Characters)
            {
                if (c.Id == id) return c.Position;
            }
        }

        return frame.CameraTarget;
    }

    List<Highlighted> Scan(WorldSnapshot snapshot, Vector3 centre, long now)
    {
        bool probe = Settings.ProbeRequested;
        Settings.ProbeRequested = false;
        if (probe) log($"probe_snapshot begin center=({centre.X:0.#},{centre.Y:0.#},{centre.Z:0.#}) max_distance={Settings.MaxDistance}");

        var found = new List<(Highlighted Item, float Distance)>();
        int scanned = 0;
        foreach (var spot in snapshot.Containers)
        {
            float distance = Vector3.Distance(spot.Position, centre);
            if (distance > Settings.MaxDistance) continue;

            scanned++;
            var kind = kinds.Of(spot.Record);
            if (probe) log($"probe_snapshot outcome={kind.Reason} id={spot.Id} record={spot.Record}");
            if (kind.Matches) found.Add((new(spot.Id, spot.Position.Y, spot.Position + new Vector3(0, kind.MarkerHeight, 0)), distance));
        }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (found.Count > MaxKept) found.RemoveRange(MaxKept, found.Count - MaxKept);
        Summary(scanned, found.Count, now, probe);
        return found.ConvertAll(f => f.Item);
    }

    void Summary(int scanned, int matched, long now, bool probe)
    {
        if (probe) log($"probe_snapshot end scanned={scanned} matched={matched}");
        if (!Settings.DebugLogging || _summaryAt != 0 && now - _summaryAt < SummaryEveryMs) return;

        _summaryAt = now;
        log($"probe scanned={scanned} matched={matched}");
    }
}
