using System.Globalization;
using System.Numerics;
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

    [Setting("Hover label text colour", Hint = "#RRGGBB or #RRGGBBAA; an invalid value draws the default #F5F5F5", MaxLength = 9)]
    public string HoverTextColour { get; set; } = "#F5F5F5";

    [Setting("Hover label background colour", Hint = "#RRGGBB or #RRGGBBAA; an invalid value draws the default #1A1A1AF2", MaxLength = 9)]
    public string HoverBackgroundColour { get; set; } = "#1A1A1AF2";

    [Setting("Default marker type", Hint = "The type a middle click gives a new marker.")]
    public MarkerKind DefaultType { get; set; } = MarkerKind.Note;

    /// <summary>The hover label's text and strip colours (the original's <c>hover_label_*_color_hex</c>: an invalid one is its default).</summary>
    public (Vector4 Text, Vector4 Back) HoverColours() => (Parse(HoverTextColour, 0xF5F5F5FF), Parse(HoverBackgroundColour, 0x1A1A1AF2));

    static Vector4 Parse(string hex, uint fallback)
    {
        string digits = hex.Trim().TrimStart('#');
        bool valid = digits.Length is 6 or 8 && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
        uint rgba = valid ? uint.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) : fallback;
        if (valid && digits.Length == 6) rgba = rgba << 8 | 0xFF;

        return MapOverlay.Rgba(rgba);
    }
}
