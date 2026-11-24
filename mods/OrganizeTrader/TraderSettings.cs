using Meitou.Modding;

namespace Meitou.OrganizeTrader;

/// <summary>
/// The MCM fork's settings page, in its order, with its <c>mod-config.defaults.json</c> defaults, ranges and catalogue's labels and hints. The
/// fork's page no longer offers the search and sort panels' width and height (its controls sit inline in the window) and neither does this port.
/// </summary>
public sealed class TraderSettings
{
    [Setting("Enabled", Hint = "Enable Organize the Trader search controls")]
    public bool Enabled { get; set; } = true;

    [Setting("Show entry count", Hint = "Show visible and total entry counts in the search bar")]
    public bool ShowEntryCount { get; set; } = true;

    [Setting("Show quantity count", Hint = "Show visible stack quantity in the search bar")]
    public bool ShowQuantityCount { get; set; } = true;

    [Setting("Show clear button", Hint = "Show the clear button inside the search bar")]
    public bool ShowClearButton { get; set; } = true;

    [Setting("Auto-focus search", Hint = "Focus the trader search input automatically when controls are injected")]
    public bool AutoFocus { get; set; } = true;

    [Setting("Search field width",
        Hint = "Preferred search field width in pixels. Readable text takes priority; narrow windows use separate rows.", Min = 120, Max = 600,
        Step = 1)]
    public int SearchFieldWidth { get; set; } = 260;

    [Setting("Sort list width", Hint = "Preferred sort list width in pixels. 0 fits its translated items. Narrow windows use separate rows.",
        Min = 0, Max = 400, Step = 1)]
    public int SortListWidth { get; set; }

    [Setting("Deal balance",
        Hint = "Show the current deal balance at the centre of the trader header (plus - gain, minus - cost).")]
    public bool ShowDealBalance { get; set; } = true;

    [Setting("Debug logging", Hint = "Write detailed logs to the game's log")]
    public bool DebugLogging { get; set; }
}
