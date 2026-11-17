using Meitou.Modding;

namespace Meitou.MapMarkers;

/// <summary>The original's <c>mod-config.json</c> settings, under Mod settings (same defaults).</summary>
public sealed class MapMarkersSettings
{
    [Setting("Enable Map markers", Hint = "Master toggle: draws the markers and takes the map clicks that add, select and move them.")]
    public bool Enabled { get; set; } = true;

    [Setting("Markers visible", Hint = "Shows the markers on the world map; the map's Markers: On/Off button switches it too.")]
    public bool MarkersVisible { get; set; } = true;

    [Setting("Close editor on map close", Hint = "Deselects the marker, closing its editor, when the map closes.")]
    public bool CloseEditorOnMapClose { get; set; } = true;

    [Setting("Show hover labels", Hint = "Shows a marker's type and label over it while the pointer is on it.")]
    public bool ShowHoverLabels { get; set; } = true;

    [Setting("Default marker type", Hint = "The type a middle click gives a new marker.")]
    public MarkerKind DefaultType { get; set; } = MarkerKind.Note;
}
