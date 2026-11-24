using System.Globalization;
using Meitou.Data.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// The original's diagnostics (<b>Verified</b>, its README and DLL strings): Ctrl+Alt+F7 writes one snapshot of the map overlay's state, Ctrl+Alt+F8
/// switches the detailed log (<c>debug_logging</c>, also on the settings page) that follows the hover and the editor. Port: lines in the mod log, in
/// its own words; nothing is written while the detailed log is off but the snapshot.
/// </summary>
static class MapDiagnostics
{
    /// <summary>The overlay's layout as last built (host thread).</summary>
    public static MapLayout? Layout { get; set; }

    static int s_hovered;

    /// <summary>One line of the overlay's state: settings, markers, the selection, the map image, viewport, UI scale and editor.</summary>
    public static void Snapshot(string reason)
    {
        var settings = MapMarkersMod.Settings;
        int count = MapState.Book?.Snapshot().Length ?? 0;
        string layout = Layout is { } l ? $"image={Rect(l.Image)} viewport={Rect(l.Viewport)} ui={l.Ui:0.###} editor={Rect(l.Editor)}" : "map=not shown";
        MapMarkersMod.Log(string.Create(CultureInfo.InvariantCulture,
            $"overlay_diagnostics reason={reason} enabled={settings.Enabled} visible={settings.MarkersVisible} markers={count} "
            + $"selected={MapState.Selected?.Id ?? 0} moved={MapState.EditorMoved.X:0},{MapState.EditorMoved.Y:0} {layout}"));
    }

    /// <summary>Switches the detailed log and keeps it with the settings.</summary>
    public static void Toggle(string reason)
    {
        bool on = !MapMarkersMod.Settings.DebugLogging;
        MapMarkersMod.Keep(nameof(MapMarkersSettings.DebugLogging), on);
        MapMarkersMod.Log($"toggle_footer_diagnostics reason={reason} detailed_log={(on ? "on" : "off")}");
    }

    /// <summary>A detailed log line (only while it is on).</summary>
    public static void Detail(string text)
    {
        if (MapMarkersMod.Settings.DebugLogging) MapMarkersMod.Log("PROBE " + text);
    }

    /// <summary>Logs the marker under the pointer each time it changes (detailed log only).</summary>
    public static void Hovered(Marker? marker)
    {
        int id = marker?.Id ?? 0;
        if (id == s_hovered) return;

        s_hovered = id;
        if (marker is null) Detail("hover none");
        else Detail(string.Create(CultureInfo.InvariantCulture, $"hover marker={id} type={marker.Kind} x={marker.At.X:0.####} y={marker.At.Y:0.####}"));
    }

    static string Rect(UiRect r) => string.Create(CultureInfo.InvariantCulture, $"{r.X:0},{r.Y:0},{r.Width:0}x{r.Height:0}");
}
