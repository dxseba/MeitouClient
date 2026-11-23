using Meitou.Modding;

namespace Meitou.VitalSense;

/// <summary>
/// The original's mod-config.json keys that Meitou can honour, same defaults and clamps (Vital Sense <c>mod-config.defaults.json</c>, the DLL's
/// clamp messages).
/// </summary>
public sealed class VitalSettings
{
    /// <summary>
    /// <c>enabled</c>.
    /// </summary>
    [Setting("Vital Sense", Hint = "Markers over downed characters (ZZ, RC, DY, PD, DE), a bounty symbol and a body tint.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>highlight_key</c> UNBOUND: always on. Otherwise the game's Highlight key (Alt, the original's default) or <c>Highlight</c> key action.
    /// </summary>
    [Setting("Always on", Hint = "Show the markers without holding the Highlight key (Alt) or the mod's own key.")]
    public bool AlwaysOn { get; set; }

    /// <summary>
    /// <c>max_highlight_distance_m</c> (5 to 20000, 3500), world units from the camera's centre, flat.
    /// </summary>
    [Setting("Max distance", Hint = "World units from the camera's centre, measured flat.", Min = 5, Max = 20000, Step = 50)]
    public float MaxDistance { get; set; } = 3500;

    /// <summary>
    /// <c>max_markers</c> (8 to 256, 96), nearest the camera's centre first.
    /// </summary>
    [Setting("Marker budget", Hint = "At most this many markers, nearest the camera's centre first.", Min = 8, Max = 256, Step = 8)]
    public int Budget { get; set; } = 96;

    /// <summary>
    /// <c>show_text</c>.
    /// </summary>
    [Setting("State text", Hint = "Show the state letters.")]
    public bool ShowText { get; set; } = true;

    [Setting("Unconscious (ZZ)")]
    public bool Unconscious { get; set; } = true;

    [Setting("Recovery coma (RC)")]
    public bool RecoveryComa { get; set; } = true;

    [Setting("Dying (DY)")]
    public bool Dying { get; set; } = true;

    [Setting("Playing dead (PD)")]
    public bool PlayingDead { get; set; } = true;

    [Setting("Dead (DE)")]
    public bool Dead { get; set; } = true;

    /// <summary>
    /// <c>state_symbol_live_anchor_y_offset_cm</c> (0 to 20000, 0): extra height of the state marker.
    /// </summary>
    [Setting("State marker height offset (cm)", Hint = "Additional height of the state text.", Min = 0, Max = 20000, Step = 10)]
    public int StateOffsetCm { get; set; }

    /// <summary>
    /// <c>show_bounty_symbol</c>.
    /// </summary>
    [Setting("Bounty symbol", Hint = "A $ before the state, coloured by the character's total bounty.")]
    public bool ShowBounty { get; set; } = true;

    /// <summary>
    /// <c>show_bounty_symbol_on_all_characters</c>.
    /// </summary>
    [Setting("Bounty on everyone", Hint = "Also show the $ over standing characters with a bounty.")]
    public bool BountyOnAll { get; set; } = true;

    /// <summary>
    /// <c>bounty_symbol_live_anchor_y_offset_cm</c> (0 to 20000, 420): height of a standing character's $.
    /// </summary>
    [Setting("Bounty symbol height (cm)", Hint = "Height of the $ over a standing character.", Min = 0, Max = 20000, Step = 10)]
    public int BountyOffsetCm { get; set; } = 420;

    /// <summary>
    /// <c>enable_character_tint</c>: the body's albedo blended to the marker's colour (<c>Tint</c>, the original's <c>coloroverride</c>).
    /// </summary>
    [Setting("Character tint", Hint = "Colours marked characters in their relation colour while the markers show.")]
    public bool Tint { get; set; } = true;

    /// <summary>
    /// <c>character_tint_include_squad</c>.
    /// </summary>
    [Setting("Tint squad", Hint = "Also tint your conscious characters (squad colour).")]
    public bool TintSquad { get; set; } = true;

    /// <summary>
    /// <c>character_tint_include_bounty_only</c>: standing targets with a bounty, in the bounty tier's colour.
    /// </summary>
    [Setting("Tint bounty targets", Hint = "Also tint standing characters with a bounty, in the bounty tier's colour.")]
    public bool TintBounty { get; set; }

    /// <summary>
    /// <c>character_tint_force_depth_override</c>: the tinted characters are also drawn where the scene hides them (at most 64).
    /// </summary>
    [Setting("Tint through walls", Hint = "Also draw tinted characters where walls or terrain hide them.")]
    public bool TintThroughWalls { get; set; } = true;

    /// <summary>
    /// <c>show_icons</c>.
    /// </summary>
    [Setting("State icons", Hint = "Show an icon beside the state letters.")]
    public bool ShowIcons { get; set; } = true;

    /// <summary>
    /// <c>unconscious_text_size_px</c> and the other states' (8 to 128, 18).
    /// </summary>
    [Setting("Unconscious text size", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int UnconsciousSize { get; set; } = 18;

    [Setting("Recovery coma text size", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int RecoveryComaSize { get; set; } = 18;

    [Setting("Dying text size", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int DyingSize { get; set; } = 18;

    [Setting("Playing dead text size", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int PlayingDeadSize { get; set; } = 18;

    [Setting("Dead text size", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int DeadSize { get; set; } = 18;

    /// <summary>
    /// <paramref name="state"/>'s text size in pixels.
    /// </summary>
    public int TextSize(VitalState state) => state switch
    {
        VitalState.Unconscious => UnconsciousSize,
        VitalState.RecoveryComa => RecoveryComaSize,
        VitalState.Dying => DyingSize,
        VitalState.PlayingDead => PlayingDeadSize,
        _ => DeadSize,
    };

    /// <summary>
    /// Whether a marker of <paramref name="state"/> is switched on.
    /// </summary>
    public bool Shows(VitalState state) => state switch
    {
        VitalState.Unconscious => Unconscious,
        VitalState.RecoveryComa => RecoveryComa,
        VitalState.Dying => Dying,
        VitalState.PlayingDead => PlayingDead,
        VitalState.Dead => Dead,
        _ => false,
    };
}
