using Meitou.Modding;

namespace Meitou.VitalRead;

/// <summary>
/// The portrait corner a badge sits in (the original's <c>portraitIconAnchor</c> / <c>portraitTextAnchor</c> values).
/// </summary>
public enum Corner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// The original's Mod Hub settings, its labels (en_GB catalog) and defaults (Vital-Read <c>mod-config.defaults.json</c>); the ranges are Meitou's.
/// </summary>
public sealed class VitalReadSettings
{
    /// <summary>
    /// <c>enabled</c>, default true.
    /// </summary>
    [Setting("Enabled", Hint = "Turns the whole mod on and off")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>showIcons</c>, default true.
    /// </summary>
    [Setting("Show icons", Hint = "State icons on the portraits")]
    public bool ShowIcons { get; set; } = true;

    /// <summary>
    /// <c>showText</c>, default true.
    /// </summary>
    [Setting("Show text", Hint = "Short state text on the portraits")]
    public bool ShowText { get; set; } = true;

    /// <summary>
    /// <c>portraitIconDisplaySizePx</c>, default 20; 0 sizes the icon by the portrait.
    /// </summary>
    [Setting("Icon size", Hint = "Size of the state icon, px. 0 - automatic", Min = 0, Max = 128, Step = 1)]
    public int IconSize { get; set; } = 20;

    /// <summary>
    /// <c>portraitTextFontHeightPx</c>, default 18.
    /// </summary>
    [Setting("Text size", Hint = "Font height of the state text", Min = 8, Max = 48, Step = 1)]
    public int TextSize { get; set; } = 18;

    /// <summary>
    /// <c>portraitIconAnchor</c>, default bottom_left.
    /// </summary>
    [Setting("Icon corner", Hint = "Which corner of the portrait the icon goes in")]
    public Corner IconAnchor { get; set; } = Corner.BottomLeft;

    /// <summary>
    /// <c>portraitTextAnchor</c>, default top_right.
    /// </summary>
    [Setting("Text corner", Hint = "Which corner of the portrait the text goes in")]
    public Corner TextAnchor { get; set; } = Corner.TopRight;

    /// <summary>
    /// <c>portraitOverlayMarginXPx</c>, default 0, shared by icon and text.
    /// </summary>
    [Setting("Horizontal margin", Hint = "Distance of the icon and text from the side edge of the portrait: plus - inward, minus - outward",
        Min = -50, Max = 50, Step = 1)]
    public int MarginX { get; set; }

    /// <summary>
    /// <c>portraitOverlayMarginYPx</c>, default 0, shared by icon and text.
    /// </summary>
    [Setting("Vertical margin", Hint = "Distance of the icon and text from the top or bottom edge of the portrait: plus - inward, minus - outward",
        Min = -50, Max = 50, Step = 1)]
    public int MarginY { get; set; }

    /// <summary>
    /// Meitou's: tints each text badge by its state (the original's text is the skin's colour).
    /// </summary>
    [Setting("Coloured text", Hint = "Tints each text badge by its state (Meitou's; the original's text is plain).")]
    public bool Coloured { get; set; }

    /// <summary>
    /// <c>debugLogging</c>, default false: each member's state changes and each icon's file to the mod log, and the probe keys.
    /// </summary>
    [Setting("Debug logging", Hint = "Writes state changes and icon lookups to the log, and turns on the probe keys (Ctrl+Alt+F11, Ctrl+Alt+F12).")]
    public bool DebugLogging { get; set; }
}
