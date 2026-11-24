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
/// The original's Mod Hub settings, its labels and hints (the msgids of its catalogues, shown through them: <see cref="VitalReadMod"/>) and
/// defaults (Vital-Read <c>mod-config.defaults.json</c>); the ranges are Meitou's.
/// </summary>
public sealed class VitalReadSettings
{
    /// <summary>
    /// <c>enabled</c>, default true.
    /// </summary>
    [Setting("Enabled", Hint = "Enable Vital Read runtime probes and overlays")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>showIcons</c>, default true.
    /// </summary>
    [Setting("Show icons", Hint = "Show portrait state icon overlays")]
    public bool ShowIcons { get; set; } = true;

    /// <summary>
    /// <c>showText</c>, default true.
    /// </summary>
    [Setting("Show text", Hint = "Show portrait state text badges")]
    public bool ShowText { get; set; } = true;

    /// <summary>
    /// <c>portraitIconDisplaySizePx</c>, default 20; 0 sizes the icon by the portrait.
    /// </summary>
    [Setting("Icon size", Hint = "Set portrait state icon display size in pixels (0 keeps automatic sizing)", Min = 0, Max = 128, Step = 1)]
    public int IconSize { get; set; } = 20;

    /// <summary>
    /// <c>portraitTextFontHeightPx</c>, default 18.
    /// </summary>
    [Setting("Text size", Hint = "Set portrait state text badge font height", Min = 8, Max = 48, Step = 1)]
    public int TextSize { get; set; } = 18;

    /// <summary>
    /// <c>portraitIconAnchor</c>, default bottom_left.
    /// </summary>
    [Setting("Icon corner", Hint = "Set portrait icon corner", Options = [TopLeft, TopRight, BottomLeft, BottomRight])]
    public string IconCorner { get; set; } = BottomLeft;

    public Corner IconAnchor => CornerOf(IconCorner, Corner.BottomLeft);

    /// <summary>
    /// <c>portraitTextAnchor</c>, default top_right.
    /// </summary>
    [Setting("Text corner", Hint = "Set portrait text corner", Options = [TopLeft, TopRight, BottomLeft, BottomRight])]
    public string TextCorner { get; set; } = TopRight;

    public Corner TextAnchor => CornerOf(TextCorner, Corner.TopRight);

    /// <summary>
    /// <c>portraitOverlayMarginXPx</c>, default 0, shared by icon and text.
    /// </summary>
    [Setting("Overlay X margin",
        Hint = "Set horizontal edge margin for portrait icons and text; positive moves inward, negative moves outward", Min = -50, Max = 50, Step = 1)]
    public int MarginX { get; set; }

    /// <summary>
    /// <c>portraitOverlayMarginYPx</c>, default 0, shared by icon and text.
    /// </summary>
    [Setting("Overlay Y margin",
        Hint = "Set vertical edge margin for portrait icons and text; positive moves inward, negative moves outward", Min = -50, Max = 50, Step = 1)]
    public int MarginY { get; set; }

    /// <summary>
    /// Meitou's: tints each text badge by its state (the original's text is the skin's colour).
    /// </summary>
    [Setting("Coloured text", Hint = "Tints each text badge by its state (Meitou's; the original's text is plain).")]
    public bool Coloured { get; set; }

    /// <summary>
    /// <c>debugLogging</c>, default false: each member's state changes and each icon's file to the mod log, and the probe keys.
    /// </summary>
    [Setting("Debug logging", Hint = "Enable general Vital Read debug logs")]
    public bool DebugLogging { get; set; }

    const string TopLeft = "Top left";
    const string TopRight = "Top right";
    const string BottomLeft = "Bottom left";
    const string BottomRight = "Bottom right";

    static Corner CornerOf(string text, Corner fallback) => text switch
    {
        TopLeft => Corner.TopLeft,
        TopRight => Corner.TopRight,
        BottomLeft => Corner.BottomLeft,
        BottomRight => Corner.BottomRight,
        _ => fallback,
    };
}
