using System.Globalization;
using System.Numerics;
using Meitou.Modding;

namespace Meitou.ContainerHighlight;

/// <summary>
/// The original's Mod Config Menu rows in its order, with its mod-config.defaults.json defaults and its ranges; labels and hints are the msgids of
/// its catalogues (<c>container_highlight</c>), shown through them. Its highlight key is the mod's key action on the Controls page.
/// </summary>
public sealed class ContainerSettings
{
    const string Advanced = "Advanced";

    /// <summary>
    /// <c>enabled</c>.
    /// </summary>
    [Setting("Enabled", Hint = "Enable Container Highlight runtime behavior")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>max_highlight_distance_m</c> (100 to 5000, step 50, 900): the scan sphere's radius in world units round the selected character, else the
    /// camera's centre (the original passes the value as it is to the game's sphere search).
    /// </summary>
    [Setting("Max highlight distance", Hint = "Maximum scan radius in meters for nearby container detection", Min = 100, Max = 5000, Step = 50)]
    public float MaxDistance { get; set; } = 900;

    /// <summary>
    /// <c>show_icons</c> (off by default).
    /// </summary>
    [Setting("Show icons", Hint = "Show screen-space icon markers above matched containers")]
    public bool ShowIcons { get; set; }

    /// <summary>
    /// <c>show_text</c> (off by default).
    /// </summary>
    [Setting("Show text", Hint = "Show screen-space text markers above matched containers")]
    public bool ShowText { get; set; }

    /// <summary>
    /// <c>marker_text</c> (at most 24 characters, "BOX").
    /// </summary>
    [Setting("Marker text", Hint = "Text shown above matched containers when Show text is enabled", MaxLength = 24, VisibleWhen = nameof(ShowText))]
    public string MarkerText { get; set; } = "BOX";

    /// <summary>
    /// <c>marker_color_hex</c> (#RRGGBB).
    /// </summary>
    [Setting("Marker color", Hint = "RGB color for marker text and icon", MaxLength = 7)]
    public string MarkerColour { get; set; } = "#F6C454";

    /// <summary>
    /// <c>enable_tint</c>.
    /// </summary>
    [Setting("Enable tint", Hint = "Apply model tint to matched containers when the highlight gate is active")]
    public bool Tint { get; set; } = true;

    /// <summary>
    /// <c>tint_color_hex</c>'s RGB (#RRGGBB); its alpha stays the defaults' D9, as the menu row keeps the file's alpha.
    /// </summary>
    [Setting("Tint color", Hint = "RGB tint color for highlighted containers; JSON alpha is preserved", MaxLength = 7, VisibleWhen = nameof(Tint))]
    public string TintColour { get; set; } = "#F6C454";

    /// <summary>
    /// <c>debug_logging</c>: a scan summary in the log at most every 5 s.
    /// </summary>
    [Setting("Debug logging", Hint = "Write probe summaries and diagnostics to RE_Kenshi_log.txt", SubGroup = Advanced)]
    public bool DebugLogging { get; set; }

    /// <summary>
    /// <c>log_probe_snapshot</c>: the next scan logs every container it looked at and why it was or was not matched.
    /// </summary>
    [Setting("Log nearby probe snapshot",
        Hint = "Dump nearby scanned objects and filter decisions into RE_Kenshi_log.txt for barrel/container debugging", SubGroup = Advanced)]
    public Action LogProbe => () => ProbeRequested = true;

    internal volatile bool ProbeRequested;

    /// <summary>
    /// The tint's alpha, the defaults' <c>#F6C454D9</c>.
    /// </summary>
    const float TintAlpha = 0xD9 / 255f;

    internal Vector4 Marker => Colour(MarkerColour, 1) ?? new Vector4(0xF6 / 255f, 0xC4 / 255f, 0x54 / 255f, 1);

    internal Vector4 TintRgba => Colour(TintColour, TintAlpha) ?? new Vector4(0xF6 / 255f, 0xC4 / 255f, 0x54 / 255f, TintAlpha);

    /// <summary>
    /// A "#RRGGBB" (the # optional) as a colour with <paramref name="alpha"/>; null for anything else (the original refuses it: expected_rgb_hex).
    /// </summary>
    static Vector4? Colour(string text, float alpha)
    {
        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb)) return null;

        return new Vector4((rgb >> 16 & 0xFF) / 255f, (rgb >> 8 & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }
}
