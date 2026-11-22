using Meitou.Modding;

namespace Meitou.MapMarkers;

/// <summary>The original's <c>mod-config.json</c> settings, under Mod settings (same defaults; labels and hints the MCM fork's, <b>Verified</b> DLL strings).</summary>
public sealed class MapMarkersSettings
{
    [Setting("Enabled", Hint = "Enable Map-markers map UI and runtime behavior")]
    public bool Enabled { get; set; } = true;

    [Setting("Markers visible", Hint = "Shows the markers on the world map; the map's Markers: On/Off button switches it too.")]
    public bool MarkersVisible { get; set; } = true;

    [Setting("Close editor on map close", Hint = "Clear selection and close the marker editor when the world map closes")]
    public bool CloseEditorOnMapClose { get; set; } = true;

    [Setting("Show hover labels", Hint = "Show marker type and label text when hovering a marker on the world map")]
    public bool ShowHoverLabels { get; set; } = true;

    [Setting("Default marker type", Hint = "The type a middle click gives a new marker.")]
    public MarkerKind DefaultType { get; set; } = MarkerKind.Note;
}
