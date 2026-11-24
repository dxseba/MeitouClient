using Meitou.Modding;

namespace Meitou.OrganizeCraftingStations;

/// <summary>
/// The MCM fork's settings page (its <c>mod-config.defaults.json</c> defaults and its catalogue's labels and hints); the dragged position is
/// kept as hidden settings, as the original keeps it in <c>mod-config.json</c>.
/// </summary>
public sealed class CraftingSettings
{
    [Setting("Enabled", Hint = "Enable Organize the Crafting Stations search controls")]
    public bool Enabled { get; set; } = true;

    [Setting("Show entry count", Hint = "Show visible and total entry counts in the search bar")]
    public bool ShowEntryCount { get; set; } = true;

    [Setting("Show clear button", Hint = "Show the clear button inside the search bar")]
    public bool ShowClearButton { get; set; } = true;

    [Setting("Auto-focus search", Hint = "Focus the crafting search input automatically when controls are injected")]
    public bool AutoFocus { get; set; } = true;

    [Setting("Search input width", Hint = "Desired search input width in pixels", Min = 120, Max = 720, Step = 1)]
    public int SearchWidth { get; set; } = 220;

    [Setting("Search input height", Hint = "Desired search input height in pixels", Min = 22, Max = 48, Step = 1)]
    public int SearchHeight { get; set; } = 26;

    [Setting("Debug logging", Hint = "Write detailed logs to the game's log")]
    public bool DebugLogging { get; set; }

    [Setting("Custom position", VisibleWhen = nameof(Never))]
    public bool PositionCustomized { get; set; }

    [Setting("Position left", Min = 0, Max = 10000, Step = 1, VisibleWhen = nameof(Never))]
    public int Left { get; set; }

    [Setting("Position top", Min = 0, Max = 10000, Step = 1, VisibleWhen = nameof(Never))]
    public int Top { get; set; }

    /// <summary>
    /// Always false: hides the position settings from the page.
    /// </summary>
    public bool Never => false;
}
