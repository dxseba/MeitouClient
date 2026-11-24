using Meitou.Modding;

namespace Meitou.BestMinimap;

/// <summary>
/// The original's <c>BestMinimap.ini</c> keys (property names), defaults of <c>BestMinimap.defaults.ini</c> and ranges of its loader and MCM page
/// (<b>Verified</b>, decompile); labels and hints are the original's English catalogue texts (<c>best_minimap.mo</c>), so its Russian and Chinese
/// catalogues translate them. The keys (<c>KeyToggle</c> and the zoom and size keys) are key actions on the Controls page instead.
/// </summary>
public sealed class MinimapSettings
{
    const string General = "General", Look = "Look", Who = "Who is shown", Colours = "Colours", Towns = "Towns and places", Map = "Map",
        Alert = "Attack alert", Camera = "Camera range", Info = "Information below the map", Diagnostics = "Diagnostics";

    public static readonly string[] Shapes = ["Circle", "Square", "Rectangle"];
    public static readonly string[] Frames = ["None", "Line", "Game panel"];
    public static readonly string[] IconStyles = ["As on the game map", "Diamonds"];
    public static readonly string[] SquadLabelModes = ["None", "Other squads", "All squads"];
    public static readonly string[] LineStyles = ["Dotted", "Solid"];

    /// <summary>Never true: hides the remembered place on the settings page.</summary>
    public bool Never => false;

    [Setting("Enabled", Hint = "Off - no minimap at all.", SubGroup = General)]
    public bool Enabled { get; set; } = true;

    [Setting("Shown", Hint = "The same switch as the show / hide key.", SubGroup = General)]
    public bool Visible { get; set; } = true;

    [Setting("Lock position", Hint = "Off - drag the minimap with the left mouse button.", SubGroup = General)]
    public bool Locked { get; set; }

    [Setting("Centre on the selected", Hint = "Off - on the centre of the camera.", SubGroup = General)]
    public bool CenterOnSelected { get; set; }

    [Setting("Rotate with the camera", Hint = "Off - north is always up.", SubGroup = General)]
    public bool RotateWithCamera { get; set; }

    [Setting("Range, m", Min = 30, Max = 150000, Step = 10, SubGroup = General,
        Hint = "From the centre to the edge. The wheel over the minimap and the zoom keys change it too. The map picture of the game has about 70 m in a pixel, so below about 1500 m it gets blurry.")]
    public int Range { get; set; } = 2000;

    [Setting("Right click - go there", SubGroup = General,
        Hint = "Right click on the minimap sends the selected characters to that spot, as a right click on the ground does.")]
    public bool RightClickMove { get; set; } = true;

    [Setting("Double click - camera there", SubGroup = General,
        Hint = "A double left click on the minimap moves the camera to that spot, if the camera can go that far from your characters (the camera range line). Farther - nothing happens.")]
    public bool DoubleClickCamera { get; set; } = true;

    [Setting("Shape", Options = ["Circle", "Square", "Rectangle"], Hint = "The rectangle is 16:10, its width is the Size.", SubGroup = Look)]
    public string Shape { get; set; } = "Circle";

    [Setting("Size, px", Min = 80, Max = 900, Step = 1, SubGroup = Look,
        Hint = "Diameter of the circle, side of the square, width of the rectangle. CTRL+wheel over the minimap changes it too.")]
    public int Size { get; set; } = 220;

    [Setting("Frame", Options = ["None", "Line", "Game panel"], SubGroup = Look,
        Hint = "Game panel - the skin of the game's own panels (and of interface mods); the circle gets a line in the colour of the game palette.")]
    public string Frame { get; set; } = "Line";

    [Setting("Frame colour", Hint = "auto - from the palette of the game (and of interface mods).", MaxLength = 7, SubGroup = Look)]
    public string FrameColour { get; set; } = "auto";

    [Setting("Background", Hint = "Colour where the map picture ends.", MaxLength = 7, SubGroup = Look)]
    public string BackgroundColour { get; set; } = "#1E1C19";

    [Setting("Opacity", Min = 1, Max = 10, Step = 1, Hint = "1-10 (10% steps). Only the map: the markers stay opaque.", SubGroup = Look)]
    public int Opacity { get; set; } = 10;

    [Setting("Marker size, px", Min = 2, Max = 16, Step = 1, SubGroup = Look)]
    public int DotSize { get; set; } = 5;

    [Setting("Tooltips", Hint = "Hover a town or a character on the minimap: name, faction and relations, squad, bounty.", SubGroup = Look)]
    public bool Tooltips { get; set; } = true;

    [Setting("Show camera direction", Hint = "A small arrow in the centre.", SubGroup = Look)]
    public bool ShowView { get; set; } = true;

    [Setting("Camera arrow size, px", Min = 4, Max = 64, Step = 1, Hint = "Arrow size in pixels, independently of marker size.", SubGroup = Look)]
    public int CameraArrowSize { get; set; } = 11;

    [Setting("Camera arrow colour", Hint = "The white arrow is hard to see on light sand - pick a brighter or darker one.", MaxLength = 7,
        SubGroup = Look)]
    public string ViewColour { get; set; } = "#F0EAD8";

    [Setting("Your squad", SubGroup = Who)]
    public bool ShowSquad { get; set; } = true;

    [Setting("Allies", SubGroup = Who)]
    public bool ShowAllies { get; set; } = true;

    [Setting("Neutrals", SubGroup = Who)]
    public bool ShowNeutrals { get; set; } = true;

    [Setting("Enemies", SubGroup = Who)]
    public bool ShowEnemies { get; set; } = true;

    [Setting("Animals", SubGroup = Who)]
    public bool ShowAnimals { get; set; } = true;

    [Setting("The dead", Hint = "Dimmed.", SubGroup = Who)]
    public bool ShowDead { get; set; }

    [Setting("Facing", Hint = "A small nose on each dot: where the character looks.", SubGroup = Who)]
    public bool ShowFacing { get; set; } = true;

    [Setting("Flip the facing", Hint = "If the noses point backwards.", SubGroup = Who)]
    public bool FacingFlip { get; set; }

    [Setting("Character names", Hint = "A name under every dot. Many dots - many names.", SubGroup = Who)]
    public bool CharacterNames { get; set; }

    [Setting("Bounties", Hint = "A gold ring and the sum on characters with a price on their head.", SubGroup = Who)]
    public bool ShowBounty { get; set; } = true;

    [Setting("Squad names", Options = ["None", "Other squads", "All squads"], Hint = "The name of a squad over its leader.", SubGroup = Who)]
    public string SquadLabels { get; set; } = "Other squads";

    [Setting("Your squad colour", Hint = "auto - the colour the game gives this character on the big map.", MaxLength = 7, SubGroup = Colours)]
    public string ColourSquad { get; set; } = "auto";

    [Setting("Allies colour", Hint = "auto - the colour the game gives this character on the big map.", MaxLength = 7, SubGroup = Colours)]
    public string ColourAllies { get; set; } = "auto";

    [Setting("Neutrals colour", Hint = "auto - the colour the game gives this character on the big map.", MaxLength = 7, SubGroup = Colours)]
    public string ColourNeutrals { get; set; } = "auto";

    [Setting("Enemies colour", Hint = "auto - the colour the game gives this character on the big map.", MaxLength = 7, SubGroup = Colours)]
    public string ColourEnemies { get; set; } = "auto";

    [Setting("Animals colour", MaxLength = 7, SubGroup = Colours)]
    public string ColourAnimals { get; set; } = "#C8B48C";

    [Setting("Bounty colour", MaxLength = 7, SubGroup = Colours)]
    public string BountyColour { get; set; } = "#F2C230";

    [Setting("Squad name colour", MaxLength = 7, SubGroup = Colours)]
    public string SquadLabelColour { get; set; } = "#E6C84A";

    [Setting("Towns", Hint = "Everything the big map marks: towns, outposts, camps, nests, ruins - as the game knows them.", SubGroup = Towns)]
    public bool ShowTowns { get; set; } = true;

    [Setting("Town names", SubGroup = Towns)]
    public bool TownNames { get; set; } = true;

    [Setting("Names up to range, m", Min = 500, Max = 150000, Step = 100, SubGroup = Towns,
        Hint = "Town names are shown while the range is not wider than this: on the whole map they would merge.")]
    public int TownNamesRange { get; set; } = 8000;

    [Setting("Town radius", Hint = "A thin white circle: how far the town reaches.", SubGroup = Towns)]
    public bool TownRadius { get; set; } = true;

    [Setting("Town icons", Options = ["As on the game map", "Diamonds"], SubGroup = Towns,
        Hint = "As on the game map - the icons of the big map, from the game's interface and interface mods. Places the big map has no icon for get a diamond.")]
    public string TownIcons { get; set; } = "As on the game map";

    [Setting("Town icon size, px", Min = 10, Max = 64, Step = 1, SubGroup = Towns)]
    public int TownIconSize { get; set; } = 22;

    [Setting("Town name size, px", Min = 0, Max = 48, Step = 1, Hint = "0 - like the other labels.", SubGroup = Towns)]
    public int TownNameSize { get; set; }

    [Setting("Town colour", Hint = "auto - by the relations of the town's faction to yours: red hostile, green ally.", MaxLength = 7,
        SubGroup = Towns)]
    public string TownColour { get; set; } = "auto";

    [Setting("Map-markers markers", Hint = "Markers of the Map-markers mod, from the current save.", SubGroup = Map)]
    public bool MapMarkers { get; set; } = true;

    [Setting("WantedMap targets", Hint = "Targets of the WantedMap mod, as of the last look at the big map.", SubGroup = Map)]
    public bool WantedMarkers { get; set; } = true;

    [Setting("Pin to the edge", Hint = "Markers of Map-markers and WantedMap that are out of range stay at the edge, pointing the way.",
        SubGroup = Map)]
    public bool PinToEdge { get; set; } = true;

    [Setting("Alert when your people are attacked", SubGroup = Alert,
        Hint = "A pulsing ring on the minimap where a character of your faction is attacked; out of range - at the edge, pointing the way.")]
    public bool AttackAlert { get; set; } = true;

    [Setting("Alert colour", MaxLength = 7, SubGroup = Alert)]
    public string AlertColour { get; set; } = "#FF3B30";

    [Setting("Alert lasts, s", Min = 1, Max = 120, Step = 1, Hint = "How long the ring stays after the last attack.", SubGroup = Alert)]
    public int AlertSeconds { get; set; } = 8;

    [Setting("Message in the game", Hint = "Under attack: and the names - in the game's message line, at most once in 3 seconds.", SubGroup = Alert)]
    public bool AlertMessage { get; set; } = true;

    [Setting("Flash the frame", Hint = "While someone is attacked the frame of the minimap blinks in the alert colour.", SubGroup = Alert)]
    public bool AlertFlashFrame { get; set; } = true;

    [Setting("Show how far the camera can go", SubGroup = Camera,
        Hint = "The map boundary around your characters. Camera height and terrain can slightly reduce its actual reach.")]
    public bool ShowCameraRange { get; set; } = true;

    [Setting("Camera range, m", Min = 0, Max = 150000, Step = 10, SubGroup = Camera,
        Hint = "0 - use the verified game limit automatically. If another camera mod changes it and the limit cannot be detected, enter the distance manually.")]
    public int CameraRange { get; set; }

    [Setting("Camera line width, px", Min = 1, Max = 8, Step = 1, SubGroup = Camera)]
    public int CameraLineWidth { get; set; } = 3;

    [Setting("Camera line colour", MaxLength = 7, SubGroup = Camera)]
    public string CameraRangeColour { get; set; } = "#E8E2D0";

    [Setting("Camera line style", Options = ["Dotted", "Solid"], SubGroup = Camera)]
    public string CameraRangeStyle { get; set; } = "Dotted";

    [Setting("Show zoom level", Hint = "100% is the default range (2000). Zooming in raises the percentage. The strip disappears when all its fields are off.",
        SubGroup = Info)]
    public bool InfoScale { get; set; }

    [Setting("Show centre coordinates", SubGroup = Info,
        Hint = "X and Z of the minimap centre, in metres. Follows the selected characters when that centring mode is enabled.")]
    public bool InfoCoordinates { get; set; }

    [Setting("Camera direction", Hint = "Camera heading in degrees: 000 is north on the map, 090 east, 180 south and 270 west.", SubGroup = Info)]
    public bool InfoHeading { get; set; }

    [Setting("Show real-world clock", Hint = "Local computer time, in 24-hour HH:MM format.", SubGroup = Info)]
    public bool InfoClock { get; set; }

    [Setting("Updates per second", Min = 1, Max = 60, Step = 1, SubGroup = Diagnostics,
        Hint = "How often the whole minimap is redrawn: the map, dots, names, the camera arrow. Higher - smoother when the camera moves, a little more work for the processor; the game FPS does not depend on it.")]
    public int UpdateFps { get; set; } = 20;

    [Setting("Map picture limit, px", Min = 512, Max = 16384, Step = 512, Hint = "A bigger map picture is scaled down in memory to this size.",
        SubGroup = Diagnostics)]
    public int MaxSourceSize { get; set; } = 4096;

    [Setting("Swap red and blue", Hint = "If the colours of the minimap look wrong (blue sand).", SubGroup = Diagnostics)]
    public bool SwapRedBlue { get; set; }

    [Setting("Detailed log", Hint = "In RE_Kenshi_log.txt.", SubGroup = Diagnostics)]
    public bool Debug { get; set; }

    /// <summary>The original's <c>Left</c> (-1: the top right corner of the screen), kept when the minimap is dragged.</summary>
    [Setting("Left", Min = -1, Max = 10000, Step = 1, VisibleWhen = nameof(Never))]
    public int Left { get; set; } = -1;

    /// <summary>The original's <c>Top</c> (-1: the top right corner of the screen).</summary>
    [Setting("Top", Min = -1, Max = 10000, Step = 1, VisibleWhen = nameof(Never))]
    public int Top { get; set; } = -1;

    public bool IsShape(int index) => Shape == Shapes[index];
}
