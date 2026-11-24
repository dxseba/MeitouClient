using Meitou.Modding;

namespace Meitou.ItemMarkers;

/// <summary>
/// The original's MCM page (<c>ItemMarkers.defaults.ini</c> keys, defaults, ranges, labels and hints, from the DLL's settings code): same order and
/// groups. Its English texts are the msgids of the original's <c>item_markers</c> catalogues, so they show translated. The show key is a key
/// action (<see cref="ItemMarkersMod"/>), not a setting.
/// </summary>
public sealed class MarkerSettings
{
    public const string Fill = "Fill", Frame = "Frame", Gradient = "Gradient", Star = "Star";
    public const string TopLeft = "Top left", TopRight = "Top right", BottomLeft = "Bottom left", BottomRight = "Bottom right";

    [Setting("Enabled", Hint = "All marks on item icons.", SubGroup = "General")]
    public bool Enabled { get; set; } = true;

    [Setting("Grade colour", Hint = "Armour, weapons, crossbows and limbs get the colour of their grade, as in the item tooltip.",
        SubGroup = "Grade colour")]
    public bool Rarity { get; set; } = true;

    [Setting("Style", Hint = "Fill - the whole cell, frame - the edge with a glow inside, gradient - from the bottom, star - in the corner.",
        Options = [Fill, Frame, Gradient, Star], SubGroup = "Grade colour")]
    public string RarityStyle { get; set; } = Frame;

    [Setting("Star corner", Hint = "Where the star of the Star style goes.", Options = [TopLeft, TopRight, BottomLeft, BottomRight],
        SubGroup = "Grade colour")]
    public string StarCorner { get; set; } = BottomLeft;

    [Setting("Strength", Hint = "How bright the grade colour is, 1-10 (10% steps).", Min = 1, Max = 10, Step = 1, SubGroup = "Grade colour")]
    public int RarityStrength { get; set; } = 6;

    [Setting("From grade", Hint = "Colour items of this grade and better. 0 and 1 are grey and beige - ordinary items; 2 - from blue.", Min = 0,
        Max = 5, Step = 1, SubGroup = "Grade colour")]
    public int MinGrade { get; set; } = 2;

    [Setting("Stolen", Hint = "The game's steal pointer on stolen items.", SubGroup = "Badges")]
    public bool Stolen { get; set; } = true;

    [Setting("Stolen badge size", Hint = "In pixels.", Min = 8, Max = 40, Step = 1, SubGroup = "Badges")]
    public int StolenSize { get; set; } = 16;

    [Setting("Stolen badge strength", Hint = "How visible the badge is, 1-10 (10% steps).", Min = 1, Max = 10, Step = 1, SubGroup = "Badges")]
    public int StolenStrength { get; set; } = 9;

    [Setting("Bought and sold in this trade", Hint = "A bag in the top right corner of items that changed hands in the open trade window: gold - "
        + "you sold it, green - you bought it. Gone in the next trade window.", SubGroup = "Badges")]
    public bool TradeBags { get; set; } = true;

    [Setting("Sold bag", Hint = "Colour of the bag on items you sold, #RRGGBB.", MaxLength = 7, SubGroup = "Badges")]
    public string TradeSoldColour { get; set; } = "#e8b830";

    [Setting("Bought bag", Hint = "Colour of the bag on items you bought, #RRGGBB.", MaxLength = 7, SubGroup = "Badges")]
    public string TradeBoughtColour { get; set; } = "#5fd35f";

    [Setting("Faction uniform", Hint = "The game's hand pointer on armour that is a faction uniform: wearing it you look like one of them.",
        SubGroup = "Badges")]
    public bool Faction { get; set; } = true;

    [Setting("Uniform badge size", Hint = "In pixels.", Min = 8, Max = 40, Step = 1, SubGroup = "Badges")]
    public int FactionSize { get; set; } = 16;

    [Setting("Uniform badge strength", Hint = "How visible the badge is, 1-10 (10% steps).", Min = 1, Max = 10, Step = 1, SubGroup = "Badges")]
    public int FactionStrength { get; set; } = 9;

    [Setting("Eye on learned blueprints", Hint = "An eye in the lower left corner of a blueprint icon if that blueprint is already learned - no "
        + "need to buy it again.", SubGroup = "Badges")]
    public bool Blueprint { get; set; } = true;

    [Setting("Eye on known location maps", Hint = "An eye in the lower left corner of a map icon when all listed locations are already "
        + "discovered. Maps without locations are not marked.", SubGroup = "Badges")]
    public bool MapsKnown { get; set; } = true;

    [Setting("Reload custom grade rules", Hint = "Reload profiles from the grades folder. Invalid files leave the previous rules active.",
        SubGroup = "Badges")]
    public Action ReloadRules { get; set; } = () => { };

    [Setting("Detailed log", Hint = "In RE_Kenshi_log.txt: the first 200 marked items (kind, level, grade) and how long marking takes.",
        SubGroup = "Diagnostics")]
    public bool Debug { get; set; }

    /// <summary>The style's number as the ini writes it: 0 fill, 1 frame, 2 gradient, 3 star.</summary>
    public int StyleIndex => Array.IndexOf([Fill, Frame, Gradient, Star], RarityStyle) is >= 0 and var i ? i : 1;

    /// <summary>The star corner's number as the ini writes it: 0 top left, 1 top right, 2 bottom left, 3 bottom right.</summary>
    public int CornerIndex => Array.IndexOf([TopLeft, TopRight, BottomLeft, BottomRight], StarCorner) is >= 0 and var i ? i : 2;
}
