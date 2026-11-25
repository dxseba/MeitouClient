using Meitou.Modding;

namespace Meitou.QuickStartSelect;

/// <summary>
/// The original's QuickStartSelect.ini keys and defaults (its <c>.defaults.ini</c> and MCM page), under Mod settings. Layout and sort are the
/// window's buttons in the original (stored in the ini); here they are settings too so the buttons' choice is kept.
/// </summary>
public sealed class QssSettings
{
    [Setting("Enabled", Hint = "Replace the game's start selection window with the list.")]
    public bool Enabled { get; set; } = true;

    [Setting("Confirm before starting", Hint = "Ask before starting the game, so a stray double click does not start it.")]
    public bool ConfirmBegin { get; set; } = true;

    [Setting("Right click resets an option", Hint = "Right click on a line of the advanced options returns its default value.")]
    public bool RightClickReset { get; set; } = true;

    [Setting("Text colour", Options = ["auto", "light", "dark"], Hint = "auto - by the interface palette: dark text on the vanilla parchment, "
        + "light on dark interfaces. If the text is hard to read with your interface, pick light or dark. Applies when the window opens.")]
    public string TextColour { get; set; } = "auto";

    [Setting("Layout", Options = ["Wide", "Compact"], SubGroup = "Window", Hint = "Wide: list on the left, description on the right. Compact: a "
        + "narrow window, description under the list. The first button in the window switches it too.")]
    public string Layout { get; set; } = "Wide";

    [Setting("Sort", Options = ["By name", "By difficulty"], SubGroup = "Window",
        Hint = "Order of the list. The buttons in the window switch it too; pressing the active one reverses the order.")]
    public string Sort { get; set; } = "By name";

    [Setting("Reverse order", Hint = "Z-A, or hardest first.", SubGroup = "Window")]
    public bool SortDesc { get; set; }

    [Setting("Wide view width, %", Hint = "Width of the wide window, in percent of the screen width.", Min = 50, Max = 100, Step = 1,
        SubGroup = "Window size")]
    public int WideWidth { get; set; } = 74;

    [Setting("Compact view width, %", Hint = "Width of the compact window, in percent of the screen width.", Min = 25, Max = 70, Step = 1,
        SubGroup = "Window size")]
    public int CompactWidth { get; set; } = 32;

    [Setting("Window height, %", Hint = "Height of the window, in percent of the screen height.", Min = 50, Max = 100, Step = 1,
        SubGroup = "Window size")]
    public int WindowHeight { get; set; } = 86;

    const string CustomHint = "Difficulties of starts from other mods to show in this colour and sort as this level. Several - separated by commas; "
        + "part of the word is enough.";

    [Setting("Very easy (green)", Hint = CustomHint, SubGroup = "Custom difficulties")]
    public string DifficultyVeryEasy { get; set; } = "";

    [Setting("Easy (light green)", Hint = CustomHint, SubGroup = "Custom difficulties")]
    public string DifficultyEasy { get; set; } = "";

    [Setting("Normal (yellow)", Hint = CustomHint, SubGroup = "Custom difficulties")]
    public string DifficultyNormal { get; set; } = "";

    [Setting("Above normal (orange)", Hint = CustomHint, SubGroup = "Custom difficulties")]
    public string DifficultyAboveNormal { get; set; } = "";

    [Setting("Hard (red-orange)", Hint = CustomHint, SubGroup = "Custom difficulties")]
    public string DifficultyHard { get; set; } = "";

    [Setting("Very hard (red)", Hint = CustomHint, SubGroup = "Custom difficulties")]
    public string DifficultyVeryHard { get; set; } = "";

    [Setting("Detailed log", Hint = "Details in the game's log: every start with its difficulty and races.", SubGroup = "Diagnostics")]
    public bool Debug { get; set; }

    /// <summary>The wide layout is chosen.</summary>
    public bool Wide => Layout != "Compact";

    /// <summary>The list is sorted by difficulty.</summary>
    public bool ByDifficulty => Sort == "By difficulty";

    /// <summary>The custom difficulty lists, easiest first, as the six levels.</summary>
    public string[] CustomLevels() =>
        [DifficultyVeryEasy, DifficultyEasy, DifficultyNormal, DifficultyAboveNormal, DifficultyHard, DifficultyVeryHard];
}
