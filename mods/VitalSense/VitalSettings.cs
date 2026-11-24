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
/// clamp messages); labels and hints are the msgids of its catalogues, shown through them (<see cref="VitalSenseMod"/>).
/// </summary>
public sealed class VitalSettings
{
    /// <summary>
    /// <c>enabled</c>.
    /// </summary>
    [Setting("Enabled", Hint = "Enable all Vital Sense overlays and tint behavior")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>highlight_key</c> UNBOUND: always on. Otherwise the game's Highlight key (Alt, the original's default) or <c>Highlight</c> key action.
    /// </summary>
    [Setting("Always on", Hint = "Show the markers without holding the Highlight key (Alt) or the mod's own key.")]
    public bool AlwaysOn { get; set; }

    /// <summary>
    /// <c>max_highlight_distance_m</c> (5 to 20000, 3500), world units from the camera's centre, flat.
    /// </summary>
    [Setting("Max highlight distance", Hint = "Maximum horizontal distance from camera center for highlights", Min = 5, Max = 20000, Step = 50)]
    public float MaxDistance { get; set; } = 3500;

    /// <summary>
    /// <c>max_markers</c> (8 to 256, 96), nearest the camera's centre first.
    /// </summary>
    [Setting("Max markers",
        Hint = "How many markers can be on screen at once; the nearest to the camera centre get them first", Min = 8, Max = 256, Step = 8)]
    public int Budget { get; set; } = 96;

    /// <summary>
    /// <c>show_text</c>.
    /// </summary>
    [Setting("Show text", Hint = "Render short state labels such as ZZ and DY")]
    public bool ShowText { get; set; } = true;

    [Setting("Show unconscious", Hint = "Show markers for unconscious targets")]
    public bool Unconscious { get; set; } = true;

    [Setting("Show recovery coma", Hint = "Show markers for recovery coma targets")]
    public bool RecoveryComa { get; set; } = true;

    [Setting("Show dying", Hint = "Show markers for dying targets")]
    public bool Dying { get; set; } = true;

    [Setting("Show playing dead", Hint = "Show markers for playing dead targets")]
    public bool PlayingDead { get; set; } = true;

    [Setting("Show dead", Hint = "Show markers for dead targets")]
    public bool Dead { get; set; } = true;

    /// <summary>
    /// <c>state_symbol_live_anchor_y_offset_cm</c> (0 to 20000, 0): extra height of the state marker.
    /// </summary>
    [Setting("State marker height offset (cm)",
        Hint = "Raise state text and icons above their usual position. A bounty symbol shown beside them moves with them. "
            + "Set to 0 for the usual position.", Min = 0, Max = 20000, Step = 10)]
    public int StateOffsetCm { get; set; }

    /// <summary>
    /// <c>show_bounty_symbol</c>.
    /// </summary>
    [Setting("Show bounty symbol", Hint = "Render bounty symbol text using tier colors")]
    public bool ShowBounty { get; set; } = true;

    /// <summary>
    /// <c>show_bounty_symbol_on_all_characters</c>.
    /// </summary>
    [Setting("Show live bounty symbol", Hint = "Render bounty symbols for non-downed on-screen bounty targets")]
    public bool BountyOnAll { get; set; } = true;

    /// <summary>
    /// <c>bounty_symbol_live_anchor_y_offset_cm</c> (0 to 20000, 420): height of a standing character's $.
    /// </summary>
    [Setting("Live symbol anchor offset", Hint = "Vertical world-anchor offset for live bounty symbols", Min = 0, Max = 20000, Step = 10)]
    public int BountyOffsetCm { get; set; } = 420;

    /// <summary>
    /// <c>enable_character_tint</c>: the body's albedo blended to the marker's colour (<c>Tint</c>, the original's <c>coloroverride</c>).
    /// </summary>
    [Setting("Enable character tint", Hint = "Tint resolved characters in addition to marker overlays")]
    public bool Tint { get; set; } = true;

    /// <summary>
    /// <c>character_tint_include_squad</c>.
    /// </summary>
    [Setting("Tint squad", Hint = "Tint conscious squadmates when character tint is enabled")]
    public bool TintSquad { get; set; } = true;

    /// <summary>
    /// <c>character_tint_include_bounty_only</c>: standing targets with a bounty, in the bounty tier's colour.
    /// </summary>
    [Setting("Tint bounty targets",
        Hint = "Characters with a bounty are tinted in the colour of their bounty tier, like the $ symbol; "
            + "standing ones too if the live bounty symbol is on")]
    public bool TintBounty { get; set; }

    /// <summary>
    /// <c>character_tint_force_depth_override</c>: the tinted characters are also drawn where the scene hides them (at most 64).
    /// </summary>
    [Setting("Force depth override", Hint = "Force tint to render through depth when supported by the shader")]
    public bool TintThroughWalls { get; set; } = true;

    /// <summary>
    /// <c>show_icons</c>.
    /// </summary>
    [Setting("Show icons", Hint = "Render icon widgets for KO states")]
    public bool ShowIcons { get; set; } = true;

    /// <summary>
    /// <c>unconscious_text_size_px</c> and the other states' (8 to 128, 18).
    /// </summary>
    [Setting("Unconscious text size", Hint = "Font height for the unconscious label", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int UnconsciousSize { get; set; } = 18;

    [Setting("Recovery coma text size", Hint = "Font height for the recovery coma label", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int RecoveryComaSize { get; set; } = 18;

    [Setting("Dying text size", Hint = "Font height for the dying label", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int DyingSize { get; set; } = 18;

    [Setting("Playing dead text size", Hint = "Font height for the playing dead label", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int PlayingDeadSize { get; set; } = 18;

    [Setting("Dead text size", Hint = "Font height for the dead label", SubGroup = "Text sizes", Min = 8, Max = 128, Step = 1)]
    public int DeadSize { get; set; } = 18;

    /// <summary>
    /// <c>update_interval_ms</c> (50 to 2000, 150): how often the states and bounties are read, in game time.
    /// </summary>
    [Setting("Update interval", Hint = "KO target refresh interval in milliseconds", Min = 50, Max = 2000, Step = 10)]
    public int UpdateMs { get; set; } = 150;

    /// <summary>
    /// <c>show_bounty_glow</c>: <c>gui/gfx/bounty_glow_64px.png</c> behind the icon of a character with a bounty.
    /// </summary>
    [Setting("Show bounty glow", Hint = "Render bounty glow behind eligible targets")]
    public bool ShowGlow { get; set; } = true;

    /// <summary>
    /// <c>bounty_symbol</c>, "$".
    /// </summary>
    [Setting("Bounty symbol", Hint = "Short bounty symbol text such as $ or B", SubGroup = "Bounty symbol", MaxLength = 8)]
    public string BountySymbol { get; set; } = "$";

    /// <summary>
    /// <c>bounty_symbol_size_px</c> (8 to 128, 18).
    /// </summary>
    [Setting("Bounty symbol size", Hint = "Base font height for the bounty symbol", SubGroup = "Bounty symbol", Min = 8, Max = 128, Step = 1)]
    public int BountySize { get; set; } = 18;

    /// <summary>
    /// <c>bounty_symbol_position</c>, <c>before_state_icon</c>.
    /// </summary>
    [Setting("Bounty symbol position",
        Hint = "Choose whether the bounty symbol renders before the state icon or before the state text", SubGroup = "Bounty symbol",
        Options = [BeforeIcon, BeforeText])]
    public string BountyText { get; set; } = BeforeIcon;

    public BountyPosition BountyAt => BountyText == BeforeText ? BountyPosition.BeforeStateText : BountyPosition.BeforeStateIcon;

    const string BeforeIcon = "Before state icon";
    const string BeforeText = "Before state text";

    /// <summary>
    /// The DLL's eight outline text boxes round the state text and the symbol (their colour is Meitou's: black).
    /// </summary>
    [Setting("Text outline", Hint = "A dark outline round the state text and the bounty symbol.")]
    public bool Outline { get; set; } = true;

    [Setting("Unconscious text", Hint = "Short label for the unconscious marker", SubGroup = "State texts", MaxLength = 16)]
    public string UnconsciousText { get; set; } = "ZZ";

    [Setting("Recovery coma text", Hint = "Short label for the recovery coma marker", SubGroup = "State texts", MaxLength = 16)]
    public string RecoveryComaText { get; set; } = "RC";

    [Setting("Dying text", Hint = "Short label for the dying marker", SubGroup = "State texts", MaxLength = 16)]
    public string DyingText { get; set; } = "DY";

    [Setting("Playing dead text", Hint = "Short label for the playing dead marker", SubGroup = "State texts", MaxLength = 16)]
    public string PlayingDeadText { get; set; } = "PD";

    [Setting("Dead text", Hint = "Short label for the dead marker", SubGroup = "State texts", MaxLength = 16)]
    public string DeadText { get; set; } = "DE";

    /// <summary>
    /// <c>*_color_hex</c>: #RRGGBB or #RRGGBBAA; an invalid one takes its default.
    /// </summary>
    [Setting("Enemy color", Hint = "Relation color used for enemy markers and tint", SubGroup = "Colours", MaxLength = 9)]
    public string EnemyColour { get; set; } = "#FF3333";

    [Setting("Ally color", Hint = "Relation color used for ally markers and tint", SubGroup = "Colours", MaxLength = 9)]
    public string AllyColour { get; set; } = "#DEE85A";

    [Setting("Neutral color", Hint = "Relation color used for neutral markers and tint", SubGroup = "Colours", MaxLength = 9)]
    public string NeutralColour { get; set; } = "#FFA64D";

    [Setting("Squad color", Hint = "Relation color used for squad markers and tint", SubGroup = "Colours", MaxLength = 9)]
    public string SquadColour { get; set; } = "#40FF40";

    /// <summary>
    /// <c>bounty_tier_*_max</c>: the upper bounds of the tiers; not strictly ascending, the defaults are used.
    /// </summary>
    [Setting("Bounty trivial max", Hint = "Upper bound for the trivial bounty tier", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int TrivialMax { get; set; } = 1999;

    [Setting("Bounty low max", Hint = "Upper bound for the low bounty tier", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int LowMax { get; set; } = 4999;

    [Setting("Bounty modest max", Hint = "Upper bound for the modest bounty tier", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int ModestMax { get; set; } = 9999;

    [Setting("Bounty notable max", Hint = "Upper bound for the notable bounty tier", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int NotableMax { get; set; } = 19999;

    [Setting("Bounty high value max",
        Hint = "Upper bound for the high-value bounty tier", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int HighValueMax { get; set; } = 39999;

    [Setting("Bounty elite max", Hint = "Upper bound for the elite bounty tier", SubGroup = "Bounty tiers", Min = 1, Max = 10000000, Step = 100)]
    public int EliteMax { get; set; } = 74999;

    [Setting("Bounty trivial color",
        Hint = "Bounty symbol text color for the trivial tier; Mod Hub edits RGB only", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string TrivialColour { get; set; } = "#B0B0B0C8";

    [Setting("Bounty low color",
        Hint = "Bounty symbol text color for the low tier; Mod Hub edits RGB only", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string LowColour { get; set; } = "#C9A57BD4";

    [Setting("Bounty modest color",
        Hint = "Bounty symbol text color for the modest tier; Mod Hub edits RGB only", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string ModestColour { get; set; } = "#9FBD69E0";

    [Setting("Bounty notable color",
        Hint = "Bounty symbol text color for the notable tier; Mod Hub edits RGB only", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string NotableColour { get; set; } = "#6FA6D9EC";

    [Setting("Bounty high value color",
        Hint = "Bounty symbol text color for the high-value tier; Mod Hub edits RGB only", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string HighValueColour { get; set; } = "#FFC247F5";

    [Setting("Bounty elite color",
        Hint = "Bounty symbol text color for the elite tier; Mod Hub edits RGB only", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string EliteColour { get; set; } = "#FF8A2BFA";

    [Setting("Bounty legendary color",
        Hint = "Bounty symbol text color for the legendary tier; Mod Hub edits RGB only", SubGroup = "Bounty tiers", MaxLength = 9)]
    public string LegendaryColour { get; set; } = "#E03131FF";

    /// <summary>
    /// <c>*_icon_texture</c>: a picture under the mod folder (or the original's); empty: the <c>Kenshi_UI.png</c> cell.
    /// </summary>
    [Setting("Unconscious icon texture", Hint = "Short path for the unconscious icon texture", SubGroup = "Icons", MaxLength = 260)]
    public string UnconsciousIcon { get; set; } = "";

    [Setting("Recovery coma icon texture", Hint = "Short path for the recovery coma icon texture", SubGroup = "Icons", MaxLength = 260)]
    public string RecoveryComaIcon { get; set; } = "gui/gfx/heart_64px.png";

    [Setting("Dying icon texture", Hint = "Short path for the dying icon texture", SubGroup = "Icons", MaxLength = 260)]
    public string DyingIcon { get; set; } = "gui/gfx/death_64px.png";

    [Setting("Playing dead icon texture", Hint = "Short path for the playing dead icon texture", SubGroup = "Icons", MaxLength = 260)]
    public string PlayingDeadIcon { get; set; } = "";

    [Setting("Dead icon texture", Hint = "Short path for the dead icon texture", SubGroup = "Icons", MaxLength = 260)]
    public string DeadIcon { get; set; } = "gui/gfx/grave_64px.png";

    /// <summary>
    /// <c>*_icon_size_px</c> (8 to 512, 64): the square of the picture shown, from its top left.
    /// </summary>
    [Setting("Unconscious icon size", Hint = "Pixel size for the unconscious icon", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int UnconsciousIconSize { get; set; } = 64;

    [Setting("Recovery coma icon size", Hint = "Pixel size for the recovery coma icon", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int RecoveryComaIconSize { get; set; } = 64;

    [Setting("Dying icon size", Hint = "Pixel size for the dying icon", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int DyingIconSize { get; set; } = 64;

    [Setting("Playing dead icon size", Hint = "Pixel size for the playing dead icon", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int PlayingDeadIconSize { get; set; } = 64;

    [Setting("Dead icon size", Hint = "Pixel size for the dead icon", SubGroup = "Icons", Min = 8, Max = 512, Step = 1)]
    public int DeadIconSize { get; set; } = 64;

    /// <summary>
    /// <c>debug_log_diagnostics</c>: each update's counts to the mod log, when they change.
    /// </summary>
    [Setting("Debug diagnostics", Hint = "Enable extra runtime diagnostics", SubGroup = "Debug")]
    public bool DebugDiagnostics { get; set; }

    /// <summary>
    /// <c>debug_log_texture_info</c>: each icon's picture as it is found.
    /// </summary>
    [Setting("Debug texture info", Hint = "Log custom icon texture resolution details", SubGroup = "Debug")]
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
