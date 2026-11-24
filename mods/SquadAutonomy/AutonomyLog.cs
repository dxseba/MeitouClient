namespace Meitou.SquadAutonomy;

/// <summary>
/// The original's logger (its <c>EnableLogging</c>, <c>LogSeverity</c> and <c>LogSpam</c> options, written to the game's log with an INF / WRN /
/// ERR / DBG prefix), here into the mod log: a line shows while logging is on, its severity is at most the chosen one, and a spam line (each
/// score the filter changes) only with Log spam. Off, it costs one test. Any thread.
/// </summary>
static class AutonomyLog
{
    /// <summary>The original's severities, in its order.</summary>
    public enum Severity { None, Error, Warning, Info, Debug }

    /// <summary>The mod log; nothing before the mod starts.</summary>
    public static Action<string> Sink { get; set; } = _ => { };

    /// <summary>Whether a line of <paramref name="severity"/> would show.</summary>
    public static bool On(Severity severity, bool spam = false)
    {
        var settings = SquadAutonomyMod.Settings;
        return settings.EnableLogging && (!spam || settings.LogSpam) && (int)severity <= settings.LogSeverity;
    }

    /// <summary>Writes the line when it shows.</summary>
    public static void Write(Severity severity, string line, bool spam = false)
    {
        if (!On(severity, spam)) return;

        string prefix = severity switch { Severity.Info => "INF: ", Severity.Warning => "WRN: ", Severity.Error => "ERR: ", _ => "DBG: " };
        Sink("Squad Autonomy " + prefix + line);
    }
}
