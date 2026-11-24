using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>A job the player picked to move or copy: its owner, its row and the row as it was.</summary>
readonly record struct JobPick(CharacterId Owner, int Row, PermaJob Job);

/// <summary>
/// The Job Management window (Ctrl+J or the JM button): the Squad Jobs and Stations tabs, drawn into the HUD overlay. It pauses the game while open
/// (a mod pause hold) and puts the speed back on close. Edits go out as job commands in steps; each step waits for the snapshot that shows the last one applied.
/// </summary>
public sealed partial class JobWindow(JobSettings settings, Func<long> tick, Action<SimCommand> enqueue, StationScanner scanner,
    Func<ModPauseHold?> holdPause, StationIcons icons, Action<string> log) : IUiExtension
{
    enum Tab { Squads, Stations, Messages }

    const int MaxWaitFrames = 60, MaxRetries = 5;

    readonly Dictionary<string, Action> _clicks = new(StringComparer.Ordinal);
    readonly Queue<Func<WorldSnapshot, IReadOnlyList<SimCommand>?>> _steps = new();
    Styles? _styles;
    string _clickAction = "", _dragAction = "", _wheelAction = "";

    JobBoardSystem? _board;
    WorldSnapshot? _snapshot;
    ModPauseHold? _hold;
    bool _open;
    Tab _tab;

    /// <summary>Ctrl as the last frame saw it: a click with it held adds to the picked jobs.</summary>
    bool _ctrl;

    WorldSnapshot? _waitFor;
    int _waitFrames, _retries;
    bool _abort;

    public string Id => "dxseba.jobmanagement.ui";

    long Tick => tick();

    public void Register(UiRegistration registration)
    {
        var ink = new System.Numerics.Vector4(.91f, .86f, .74f, 1);
        var pad = new Meitou.Data.Ui.UiInsets(6, 1, 6, 1);
        _styles = new(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .96f), Text = ink }),
            registration.RegisterStyle("text", new() { Text = ink, TextPadding = pad }),
            registration.RegisterStyle("dim", new() { Text = new(.66f, .62f, .54f, 1), TextPadding = pad }),
            registration.RegisterStyle("warn", new() { Text = new(.95f, .45f, .30f, 1), TextPadding = pad }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Disabled = new(.14f, .12f, .10f, .8f), TextPadding = pad,
            }),
            registration.RegisterStyle("selected", new()
            {
                Background = new(.45f, .32f, .14f, 1), Hover = new(.55f, .40f, .18f, 1), TextPadding = pad,
            }));
        _clickAction = registration.RegisterAction("click", c => Click(c.Argument));
        _dragAction = registration.RegisterAction("drag", Drag);
        _wheelAction = registration.RegisterAction("wheel", Wheel);
        registration.Contribute(UiSlots.Overlay, Build, order: 60);
    }

    /// <summary>
    /// Once per frame on the host's thread (<c>AddFrame</c>), with the snapshot on screen: follows the running world (its board system, told
    /// ready) and runs the next due step.
    /// </summary>
    public void Frame(WorldSnapshot snapshot, bool ctrl)
    {
        _snapshot = snapshot;
        _ctrl = ctrl;
        if (JobBoardSystem.Running is not { } board) return;

        if (!ReferenceEquals(board, _board)) Reset(board);
        Pump();
    }

    public void Toggle()
    {
        if (_open) Close();
        else Open();
    }

    void Open()
    {
        if (_board is null || _snapshot is null || !settings.Enabled) return;

        _open = true;
        UiKit.Scale = Math.Clamp(settings.UiScale, JobSettings.MinScale, JobSettings.MaxScale) / 100f;
        _hold = holdPause();
        Status = "";
        if (_tab == Tab.Messages) _tab = Tab.Squads;
        OpenSquads();
    }

    /// <summary>Closes the window; the speed comes back unless the player changed it while the window was open.</summary>
    void Close()
    {
        _open = false;
        ClearSelection();
        ReleasePause();
    }

    /// <summary>Lets go of the pause hold: the last hold of all mods puts the kept speeds back while the game is still paused.</summary>
    void ReleasePause()
    {
        _hold?.Release();
        _hold = null;
    }

    /// <summary>A new world: the window closes and the steps left are dropped (their rows are gone).</summary>
    void Reset(JobBoardSystem board)
    {
        _open = false;
        ClearSelection();
        ReleasePause();
        _steps.Clear();
        _waitFor = null;
        _board = board;
    }

    void Click(string key)
    {
        if (_clicks.TryGetValue(key, out var action)) action();
    }

    /// <summary>A button: its click runs <paramref name="onClick"/> (looked up by <paramref name="key"/> when clicked).</summary>
    UiNode Button(string key, string text, float width, Action onClick, bool enabled = true, bool selected = false)
    {
        _clicks[key] = onClick;
        return UiKit.Button("jm-" + key, text, width, selected ? _styles!.Selected : _styles!.Button, _clickAction, key, enabled);
    }

    UiNode Label(string id, string text, float width, string? style = null) => UiKit.Label("jm-" + id, text, width, style ?? _styles!.Text);
}
