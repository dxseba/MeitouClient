using Meitou.Modding;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The original's settings (its ini keys, its Mod Config Menu labels and hints), under Mod settings. The values here are the original's
/// <c>GearCompare.defaults.ini</c>; at start the installed original's ini replaces them as the defaults (<see cref="GearIni"/>). <c>HoldKey</c> is
/// the original's key text (<see cref="HoldKey"/>).
/// </summary>
public sealed class GearSettings
{
    /// <summary>The largest per-property and per-kilogram weight of the total.</summary>
    public const int MaxWeight = 100;

    [Setting("Enabled", Hint = "Compares the item under the cursor with what is worn: a panel next to the item description.")]
    public bool Enabled { get; set; } = true;

    [Setting("Show while held", Hint = "The panel is visible only while this key is held.", MaxLength = 32, SubGroup = "Panel")]
    public string HoldKey { get; set; } = "ALT";

    [Setting("Compare with an empty slot",
        Hint = "On - compare with an empty slot too, Worn: nothing. Off - no panel when nothing suitable is worn.", SubGroup = "Panel")]
    public bool CompareEmptySlot { get; set; }

    [Setting("Compare recipes", Hint = "On the Crafting tab, right-click two lines of the Items list: the first is marked green (A), the second "
        + "blue (B), and a panel compares them. Right-click a marked line to unmark it. Armour and limbs are compared at masterwork grade, weapons "
        + "at the model of the line.", SubGroup = "Crafting")]
    public bool CraftCompare { get; set; } = true;

    [Setting("Total percentage", Hint = "A total at the bottom of the panel: the main axis, plus a bonus for each better property, minus one for "
        + "each worse property and for extra weight.", SubGroup = "Total")]
    public bool ShowTotal { get; set; }

    [Setting("Per property, %", Hint = "How many percent each property that got better adds to the total, and each one that got worse takes away.",
        Min = 0, Max = MaxWeight, Step = 1, SubGroup = "Total")]
    public int TotalPerProperty { get; set; } = 3;

    [Setting("Per extra kg, %", Hint = "How many percent each extra kilogram takes away from the total.", Min = 0, Max = MaxWeight, Step = 1,
        SubGroup = "Total")]
    public int TotalPerKg { get; set; } = 2;

    [Setting("Better", Hint = "Colour of what gets better. auto - like the +2 in the game's item tooltip, or #RRGGBB.", MaxLength = 7,
        SubGroup = "Colours")]
    public string ColourBetter { get; set; } = "auto";

    [Setting("Worse", Hint = "Colour of what gets worse. auto - like the -1 in the game's item tooltip, or #RRGGBB.", MaxLength = 7,
        SubGroup = "Colours")]
    public string ColourWorse { get; set; } = "auto";

    [Setting("Detailed log", Hint = "Details in the log: the item under the cursor, for whom, what it is compared with. Written only when the "
        + "item changes.", SubGroup = "Diagnostics")]
    public bool Debug { get; set; }
}
