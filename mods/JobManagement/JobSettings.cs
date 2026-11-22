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
    /// The fork's <c>Hotkey</c> (default <c>CTRL+J</c>; <c>NONE</c>: only the panel button), here the J key with Ctrl or Alt.
    /// </summary>
    [Setting("Window key", Hint = "The key that toggles the Job Management window (it no longer reaches the game's binding of J); NONE: the button only.",
        Options = ["CTRL+J", "ALT+J", "NONE"])]
    public string Hotkey { get; set; } = "CTRL+J";

    [Setting("Show JM button", Hint = "A JM button under the time panel opens the window.")]
    public bool ShowButton { get; set; } = true;

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
