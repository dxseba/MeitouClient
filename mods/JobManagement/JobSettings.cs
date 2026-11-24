using Meitou.Modding;

namespace Meitou.JobManagement;

/// <summary>The station groups of the Stations tab, in the original's order (StationSettings: the first five and Other on by default).</summary>
public enum StationCategory { Crafting, Refining, Farming, Mining, Research, Training, Storage, Defense, Other }

/// <summary>The original's MCM settings (mod on/off, the open key, the panel button) and its Stations tab filters.</summary>
public sealed class JobSettings
{
    [Setting("Enable Job Management", Hint = "Master switch: the window, the button and the key.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The fork's <c>Hotkey</c> (default <c>CTRL+J</c>; <c>NONE</c>: only the panel button): a key name with optional <c>CTRL+</c>, <c>ALT+</c>,
    /// <c>SHIFT+</c> (<see cref="WindowKey"/>), the default of the window's key action (a chord with modifiers), read at start.
    /// </summary>
    [Setting("Window key", Hint = "The window key's default binding, with optional CTRL+, ALT+ or SHIFT+ (CTRL+J, ALT+J, F9...), used while the "
        + "Controls page has not rebound it; read at start. NONE: no default.", MaxLength = 32)]
    public string Hotkey { get; set; } = "CTRL+J";

    [Setting("Show JM button", Hint = "A JM button under the time panel opens the window.")]
    public bool ShowButton { get; set; } = true;

    public const int MinScale = 70, MaxScale = 200;

    /// <summary>The fork's <c>UiScale</c>: the whole window's size (text, buttons, lists), taken when the window opens.</summary>
    [Setting("Window size (%)", Hint = "The whole window's size: text, buttons and lists (70-200). Applies the next time the window opens.",
        Min = MinScale, Max = MaxScale, Step = 5)]
    public int UiScale { get; set; } = 100;

    /// <summary>The fork's <c>Debug</c>: each change the window makes, and its outcome, in the mod log.</summary>
    [Setting("Debug logging", Hint = "Write each job change the window makes, and its result, to the mod log.")]
    public bool Debug { get; set; }

    [Setting("Stations: Crafting", Hint = "Show crafting benches in the Stations tab.")]
    public bool Crafting { get; set; } = true;

    [Setting("Stations: Refining", Hint = "Show refineries and furnaces in the Stations tab.")]
    public bool Refining { get; set; } = true;

    [Setting("Stations: Farming", Hint = "Show farms in the Stations tab.")]
    public bool Farming { get; set; } = true;

    [Setting("Stations: Mining", Hint = "Show mines in the Stations tab.")]
    public bool Mining { get; set; } = true;

    [Setting("Stations: Research", Hint = "Show research benches in the Stations tab.")]
    public bool Research { get; set; } = true;

    [Setting("Stations: Training", Hint = "Show training dummies in the Stations tab.")]
    public bool Training { get; set; }

    [Setting("Stations: Storage / Hauling", Hint = "Show storages and wells in the Stations tab.")]
    public bool Storage { get; set; }

    [Setting("Stations: Defense", Hint = "Show turrets in the Stations tab.")]
    public bool Defense { get; set; }

    [Setting("Stations: Other", Hint = "Show other workable machines in the Stations tab.")]
    public bool Other { get; set; } = true;

    /// <summary>Whether the Stations tab shows <paramref name="category"/>.</summary>
    public bool Shows(StationCategory category) => category switch
    {
        StationCategory.Crafting => Crafting,
        StationCategory.Refining => Refining,
        StationCategory.Farming => Farming,
        StationCategory.Mining => Mining,
        StationCategory.Research => Research,
        StationCategory.Training => Training,
        StationCategory.Storage => Storage,
        StationCategory.Defense => Defense,
        _ => Other,
    };

    /// <summary>The original's Reset Default: the first five groups and Other shown, Training, Storage and Defense hidden.</summary>
    public void ResetFilters()
    {
        (Crafting, Refining, Farming, Mining, Research, Other) = (true, true, true, true, true, true);
        (Training, Storage, Defense) = (false, false, false);
    }

    /// <summary>Flips <paramref name="category"/>'s filter (the window's own filter buttons; kept for the session).</summary>
    public void Toggle(StationCategory category)
    {
        bool on = !Shows(category);
        switch (category)
        {
            case StationCategory.Crafting: Crafting = on; break;
            case StationCategory.Refining: Refining = on; break;
            case StationCategory.Farming: Farming = on; break;
            case StationCategory.Mining: Mining = on; break;
            case StationCategory.Research: Research = on; break;
            case StationCategory.Training: Training = on; break;
            case StationCategory.Storage: Storage = on; break;
            case StationCategory.Defense: Defense = on; break;
            default: Other = on; break;
        }
    }
}
