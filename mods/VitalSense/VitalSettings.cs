using Meitou.Modding;

namespace Meitou.VitalSense;

/// <summary>
/// <c>bounty_symbol_position</c>: <c>before_state_icon</c> or <c>before_state_text</c>.
/// </summary>
public enum BountyPosition
{
    BeforeStateIcon,
    BeforeStateText,
}

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
    /// <c>update_interval_ms</c> (50 to 2000, 150): how often the states and bounties are read, in game time.
    /// </summary>
    [Setting("Update interval (ms)", Hint = "How often the states and bounties are read.", Min = 50, Max = 2000, Step = 10)]
    public int UpdateMs { get; set; } = 150;

    /// <summary>
    /// <c>show_bounty_glow</c>: <c>gui/gfx/bounty_glow_64px.png</c> behind the icon of a character with a bounty.
    /// </summary>
    [Setting("Bounty glow", Hint = "A glow behind the state icon of a character with a bounty.")]
    public bool ShowGlow { get; set; } = true;

    /// <summary>
    /// <c>bounty_symbol</c>, "$".
    /// </summary>
    [Setting("Bounty symbol text", SubGroup = "Bounty symbol", MaxLength = 8)]
    public string BountySymbol { get; set; } = "$";

    /// <summary>
    /// <c>bounty_symbol_size_px</c> (8 to 128, 18).
    /// </summary>
    [Setting("Bounty symbol size", SubGroup = "Bounty symbol", Min = 8, Max = 128, Step = 1)]
    public int BountySize { get; set; } = 18;

    /// <summary>
    /// <c>bounty_symbol_position</c>, <c>before_state_icon</c>.
    /// </summary>
    [Setting("Bounty symbol position", SubGroup = "Bounty symbol", Hint = "Before the state icon, or between the icon and the state text.")]
    public BountyPosition BountyAt { get; set; } = BountyPosition.BeforeStateIcon;

    /// <summary>
    /// The DLL's eight outline text boxes round the state text and the symbol (their colour is Meitou's: black).
    /// </summary>
    [Setting("Text outline", Hint = "A dark outline round the state text and the bounty symbol.")]
    public bool Outline { get; set; } = true;

    [Setting("Unconscious text", SubGroup = "State texts", MaxLength = 16)]
    public string UnconsciousText { get; set; } = "ZZ";

    [Setting("Recovery coma text", SubGroup = "State texts", MaxLength = 16)]
    public string RecoveryComaText { get; set; } = "RC";

    [Setting("Dying text", SubGroup = "State texts", MaxLength = 16)]
    public string DyingText { get; set; } = "DY";

    [Setting("Playing dead text", SubGroup = "State texts", MaxLength = 16)]
    public string PlayingDeadText { get; set; } = "PD";

    [Setting("Dead text", SubGroup = "State texts", MaxLength = 16)]
    public string DeadText { get; set; } = "DE";

    /// <summary>
    /// <c>*_color_hex</c>: #RRGGBB or #RRGGBBAA; an invalid one takes its default.
    /// </summary>
    [Setting("Enemy colour", SubGroup = "Colours", Hint = "#RRGGBB or #RRGGBBAA", MaxLength = 9)]
    public string EnemyColour { get; set; } = "#FF3333";

    [Setting("Ally colour", SubGroup = "Colours", Hint = "#RRGGBB or #RRGGBBAA", MaxLength = 9)]
    public string AllyColour { get; set; } = "#DEE85A";

    [Setting("Neutral colour", SubGroup = "Colours", Hint = "#RRGGBB or #RRGGBBAA", MaxLength = 9)]
    public string NeutralColour { get; set; } = "#FFA64D";

    [Setting("Squad colour", SubGroup = "Colours", Hint = "#RRGGBB or #RRGGBBAA", MaxLength = 9)]
    public string SquadColour { get; set; } = "#40FF40";

    /// <summary>
    /// <c>bounty_tier_*_max</c>: the upper bounds of the tiers; not strictly ascending, the defaults are used.
    /// </summary>
    [Setting("Trivial bounty up to", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int TrivialMax { get; set; } = 1999;

    [Setting("Low bounty up to", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int LowMax { get; set; } = 4999;

    [Setting("Modest bounty up to", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int ModestMax { get; set; } = 9999;

    [Setting("Notable bounty up to", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int NotableMax { get; set; } = 19999;

    [Setting("High-value bounty up to", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int HighValueMax { get; set; } = 39999;

    [Setting("Elite bounty up to", SubGroup = "Bounty tiers", Hint = "Above this: legendary.", Min = 1, Max = 10000000, Step = 100)]
    public int EliteMax { get; set; } = 74999;

    [Setting("Trivial bounty colour", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string TrivialColour { get; set; } = "#B0B0B0C8";

    [Setting("Low bounty colour", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string LowColour { get; set; } = "#C9A57BD4";

    [Setting("Modest bounty colour", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string ModestColour { get; set; } = "#9FBD69E0";

    [Setting("Notable bounty colour", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string NotableColour { get; set; } = "#6FA6D9EC";

    [Setting("High-value bounty colour", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string HighValueColour { get; set; } = "#FFC247F5";

    [Setting("Elite bounty colour", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string EliteColour { get; set; } = "#FF8A2BFA";

    [Setting("Legendary bounty colour", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string LegendaryColour { get; set; } = "#E03131FF";

    /// <summary>
    /// <c>*_icon_texture</c>: a picture under the mod folder (or the original's); empty: the <c>Kenshi_UI.png</c> cell.
    /// </summary>
    [Setting("Unconscious icon", SubGroup = "Icons", Hint = "Picture path; empty - the game's own icon.", MaxLength = 260)]
    public string UnconsciousIcon { get; set; } = "";

    [Setting("Recovery coma icon", SubGroup = "Icons", MaxLength = 260)]
    public string RecoveryComaIcon { get; set; } = "gui/gfx/heart_64px.png";

    [Setting("Dying icon", SubGroup = "Icons", MaxLength = 260)]
    public string DyingIcon { get; set; } = "gui/gfx/death_64px.png";

    [Setting("Playing dead icon", SubGroup = "Icons", MaxLength = 260)]
    public string PlayingDeadIcon { get; set; } = "";

    [Setting("Dead icon", SubGroup = "Icons", MaxLength = 260)]
    public string DeadIcon { get; set; } = "gui/gfx/grave_64px.png";

    /// <summary>
    /// <c>*_icon_size_px</c> (8 to 512, 64): the square of the picture shown, from its top left.
    /// </summary>
    [Setting("Unconscious icon size", SubGroup = "Icons", Hint = "Pixels of the picture shown, from its top left.", Min = 8, Max = 512, Step = 1)]
    public int UnconsciousIconSize { get; set; } = 64;

    [Setting("Recovery coma icon size", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int RecoveryComaIconSize { get; set; } = 64;

    [Setting("Dying icon size", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int DyingIconSize { get; set; } = 64;

    [Setting("Playing dead icon size", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int PlayingDeadIconSize { get; set; } = 64;

    [Setting("Dead icon size", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int DeadIconSize { get; set; } = 64;

    /// <summary>
    /// <c>debug_log_diagnostics</c>: each update's counts to the mod log, when they change.
    /// </summary>
    [Setting("Debug diagnostics", SubGroup = "Debug", Hint = "Write additional runtime diagnostics to the log.")]
    public bool DebugDiagnostics { get; set; }

    /// <summary>
    /// <c>debug_log_texture_info</c>: each icon's picture as it is found.
    /// </summary>
    [Setting("Debug texture info", SubGroup = "Debug", Hint = "Write which picture each icon uses to the log.")]
    public bool DebugTextures { get; set; }

    /// <summary>
    /// <paramref name="state"/>'s text.
    /// </summary>
    public string Text(VitalState state) => state switch
    {
        VitalState.Unconscious => UnconsciousText,
        VitalState.RecoveryComa => RecoveryComaText,
        VitalState.Dying => DyingText,
        VitalState.PlayingDead => PlayingDeadText,
        VitalState.Dead => DeadText,
        _ => "",
    };

    /// <summary>
    /// <paramref name="state"/>'s icon picture (empty: the atlas cell) and size.
    /// </summary>
    public (string Texture, int Size) Icon(VitalState state) => state switch
    {
        VitalState.Unconscious => (UnconsciousIcon, UnconsciousIconSize),
        VitalState.RecoveryComa => (RecoveryComaIcon, RecoveryComaIconSize),
        VitalState.Dying => (DyingIcon, DyingIconSize),
        VitalState.PlayingDead => (PlayingDeadIcon, PlayingDeadIconSize),
        _ => (DeadIcon, DeadIconSize),
    };

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
