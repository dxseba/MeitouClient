using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.GateFix;

/// <summary>
/// Port of GateFix v3.1 (Basusu, Workshop 3787399736): a player character whose path runs through a closed or locked gate of the player's faction
/// gets it opened (a locked one is unlocked for it), the whole squad walks through, and once nobody of the gate's faction is near it closes
/// again and takes back its lock. Meitou's walkers already open closed, unlocked doors on their path; the port adds the unlock, the closing and
/// the relock (README "Facts").
/// </summary>
public sealed class GateFixMod : IMod
{
    internal static readonly GateFixSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("gates", _ => new GateSystem(Settings, context.Log), SystemAnchor.Before(StandardParts.Crime));
        context.Log("GateFix initialized");
    }
}

/// <summary>
/// The original's <c>GateFix.cfg</c> keys that still mean something here, with its defaults (the description and the DLL's initial values). The
/// keys of its click-injection workaround (<c>pathDist</c>, <c>maxDetour</c>, <c>openThreshold</c>, <c>pendingWait</c>, <c>injectDelay</c>,
/// <c>moveEps</c>, <c>arriveEps</c>, <c>areaFlag</c>, <c>verbose</c>, <c>logLimit</c>) have no counterpart: Meitou's paths already run
/// through gates.
/// </summary>
public sealed class GateFixSettings
{
    [Setting("Enabled", Hint = "Master switch: off leaves gates to the game.")]
    public bool Enabled { get; set; } = true;

    [Setting("Auto close", Hint = "Close a gate the mod opened once everyone is through. Off leaves it open.")]
    public bool AutoClose { get; set; } = true;

    [Setting("Close radius", Hint = "The gate does not close while one of its faction is this close. Guards posted at the gate keep it open.",
        Min = 0, Max = 1000, Step = 5)]
    public float CloseRadius { get; set; } = 110;

    [Setting("Close delay (s)", Hint = "Game seconds the area must stay clear before the gate closes.", Min = 0, Max = 60, Step = 0.5)]
    public float CloseDelay { get; set; } = 3;

    [Setting("Close grace (s)", Hint = "The gate stays open at least this long after the last one passes (game seconds).", Min = 0, Max = 120, Step = 0.5)]
    public float CloseGrace { get; set; } = 10;

    [Setting("Give up after (s)", Hint = "A gate that could not close in this many game seconds is left open.", Min = 10, Max = 1800, Step = 10)]
    public float CloseGiveUp { get; set; } = 300;

    [Setting("Respect locked gates", Hint = "Leave locked gates alone; only closed, unlocked ones are opened and closed again.")]
    public bool RespectLocked { get; set; }

    [Setting("Debug log", Hint = "Write each gate the mod opens and closes to the mod log.")]
    public bool Debug { get; set; }
}
