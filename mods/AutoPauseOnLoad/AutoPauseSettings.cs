using Meitou.Modding;

namespace Meitou.AutoPauseOnLoad;

/// <summary>
/// The original's mod-config.json keys, same defaults (its <c>mod-config.defaults.json</c>) and ranges (its range errors); labels and hints are
/// the msgids of its catalogues (<c>auto_pause_on_load</c>), shown through them.
/// </summary>
public sealed class AutoPauseSettings
{
    /// <summary>
    /// <c>enabled</c>.
    /// </summary>
    [Setting("Enabled", Hint = "Enable all Auto-Pause-on-Load behavior")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>pause_debounce_ms</c> (0 to 600000, 2000): a pause after a load waits this long after the mod's last pause.
    /// </summary>
    [Setting("Pause debounce (ms)", Hint = "Minimum interval between automatic pause events", Min = 0, Max = 600000, Step = 100)]
    public int DebounceMs { get; set; } = 2000;

    /// <summary>
    /// <c>debug_logging</c>: the pause and resume lines in the log.
    /// </summary>
    [Setting("Debug logging", Hint = "Emit verbose transition logs for load and UI pause flows")]
    public bool DebugLogging { get; set; }

    /// <summary>
    /// <c>pause_on_trade</c>.
    /// </summary>
    [Setting("Pause on trade", Hint = "Pause the game when trade UI opens")]
    public bool PauseOnTrade { get; set; } = true;

    /// <summary>
    /// <c>resume_after_trade</c> (only with <see cref="PauseOnTrade"/>).
    /// </summary>
    [Setting("Resume after trade", Hint = "Resume when trade UI closes if this mod paused the game", VisibleWhen = nameof(PauseOnTrade))]
    public bool ResumeAfterTrade { get; set; } = true;

    /// <summary>
    /// <c>pause_on_inventory_open</c>.
    /// </summary>
    [Setting("Pause on inventory open", Hint = "Pause the game when inventory opens")]
    public bool PauseOnInventory { get; set; } = true;

    /// <summary>
    /// <c>resume_after_inventory_close</c> (only with <see cref="PauseOnInventory"/>).
    /// </summary>
    [Setting("Resume after inventory close", Hint = "Resume when inventory closes if this mod paused the game",
        VisibleWhen = nameof(PauseOnInventory))]
    public bool ResumeAfterInventory { get; set; } = true;

    /// <summary>
    /// <c>pause_on_squad_attack</c>.
    /// </summary>
    [Setting("Pause when another squad is attacked",
        Hint = "Pause the game when a character of a squad that is not selected takes a hit (not just enemies nearby). For one squad - not more "
            + "often than the cooldown below")]
    public bool PauseOnSquadAttack { get; set; } = true;

    /// <summary>
    /// <c>squad_attack_cooldown_s</c> (0 to 300, 30).
    /// </summary>
    [Setting("Attacked squad pause cooldown (s)",
        Hint = "After a pause for an attacked squad, the same squad pauses the game again not sooner than this many seconds. 0 - on every hit",
        Min = 0, Max = 300, Step = 1, VisibleWhen = nameof(PauseOnSquadAttack))]
    public int SquadCooldownSeconds { get; set; } = 30;
}
