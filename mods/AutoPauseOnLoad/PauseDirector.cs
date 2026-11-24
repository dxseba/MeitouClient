using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.AutoPauseOnLoad;

/// <summary>
/// The mod's pauses, on the host's thread once a frame (<see cref="Frame"/>): after a load, for trade and inventory windows
/// (<c>PauseDirector.Windows.cs</c>) and for an attacked squad (<c>PauseDirector.Squads.cs</c>). It pauses and resumes as the pause key does
/// (<see cref="ModInterface.Pause"/>); "paused" is the player's pause (speed 0). Times are the host's clock in milliseconds, as the original's
/// <c>GetTickCount</c>; nothing here enters the simulation.
/// </summary>
sealed partial class PauseDirector(ModInterface game, Action<string> log)
{
    /// <summary>
    /// An import pauses once the frames have come steadily (no gap above 250 ms) for 2 s (the fork's 16.10 change).
    /// </summary>
    const long SteadyGapMs = 250, SteadyForMs = 2000;

    /// <summary>
    /// The start the last world ready reported (<see cref="WorldStartKind"/> as an int; Unknown: none waiting). Written by the tick, read here.
    /// </summary>
    volatile int _armed;

    /// <summary>
    /// When the mod last paused for a load or a window (0: never): the load pause's debounce reads it.
    /// </summary>
    long _lastPauseAt;

    long _lastFrameAt, _steadySince;

    static AutoPauseSettings Settings => AutoPauseOnLoadMod.Settings;

    static long Now => Environment.TickCount64;

    bool Paused => game.Speed == 0;

    /// <summary>
    /// A world started from a save (tick thread, between ticks).
    /// </summary>
    public void Arm(WorldStartKind kind) => _armed = (int)kind;

    public void Frame(ModFrame frame)
    {
        try
        {
            long now = Now;
            if (!Settings.Enabled)
            {
                _armed = (int)WorldStartKind.Unknown;
                ForgetWindows();
                _victims.Clear();
                return;
            }

            LoadStep(now);
            WindowStep(frame);
            SquadStep(frame.Snapshot, now);
            _lastFrameAt = now;
        }
        catch (Exception e)
        {
            LogOnce("frame", e);
        }
    }

    /// <summary>
    /// A load pauses at the first frame after it, unless the mod paused less than the debounce before; an import once the frames are steady.
    /// </summary>
    void LoadStep(long now)
    {
        var kind = (WorldStartKind)_armed;
        if (kind == WorldStartKind.Load)
        {
            _armed = (int)WorldStartKind.Unknown;
            if (_lastPauseAt != 0 && now - _lastPauseAt < Settings.DebounceMs) Debug("pause skipped (debounce)");
            else if (game.Pause(true))
            {
                _lastPauseAt = now;
                Debug("paused_after_load=true source=load_transition");
            }

            return;
        }

        if (kind != WorldStartKind.Import) return;

        if (_steadySince == 0 || now - _lastFrameAt > SteadyGapMs) _steadySince = now;
        if (now - _steadySince < SteadyForMs) return;

        _armed = (int)WorldStartKind.Unknown;
        _steadySince = 0;
        if (game.Pause(true))
        {
            _lastPauseAt = now;
            log("paused_after_load=true source=save_import");
        }
    }

    /// <summary>
    /// Pauses for <paramref name="source"/>: true when the mod paused the game (false when it was already paused or the call was refused).
    /// </summary>
    bool PauseFor(string source)
    {
        if (Paused || !game.Pause(true)) return false;

        _lastPauseAt = Now;
        Debug("paused=true source=" + source);
        return true;
    }

    void Debug(string line)
    {
        if (Settings.DebugLogging) log(line);
    }

    readonly HashSet<string> _logged = [];

    void LogOnce(string where, Exception e)
    {
        if (_logged.Add(where)) log($"{where} failed, left as it was: {e.Message}");
    }
}
