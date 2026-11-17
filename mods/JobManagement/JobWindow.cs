using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Production;
using Meitou.Simulation.Session;

namespace Meitou.JobManagement;

/// <summary>A job the player picked to move or copy: its owner, its row and the row as it was.</summary>
readonly record struct JobPick(CharacterId Owner, int Row, PermaJob Job);

/// <summary>
/// The Job Management window (Ctrl+J or the JM button): the Squad Jobs and Stations tabs, drawn into the HUD overlay. It pauses the game while open
/// and puts the speed back on close. Edits go out as job commands in steps; each step waits for the snapshot that shows the last one applied.
/// </summary>
public sealed partial class JobWindow(JobSettings settings, Func<long> tick, Action<SimCommand> enqueue, StationScanner scanner) : IUiExtension
{
    enum Tab { Squads, Stations }

    const int MaxWaitFrames = 60, MaxRetries = 5;

    readonly Dictionary<string, Action> _clicks = new(StringComparer.Ordinal);
    readonly Queue<Func<WorldSnapshot, IReadOnlyList<SimCommand>?>> _steps = new();
    Styles? _styles;
    string _clickAction = "";

    WorldSession? _session;
    World? _world;
    SimulationClock.SpeedState? _speeds;
    bool _open;
    Tab _tab;
    string _status = "";

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
        registration.Contribute(UiSlots.Overlay, Build, order: 60);
    }

    /// <summary>Once per frame on the interface's thread (the BindUi hook): follows the session and runs the next due step.</summary>
    public void Frame(WorldSession session)
    {
        if (!ReferenceEquals(session, _session) || _open && !ReferenceEquals(session.World, _world)) Reset(session);
        Pump();
    }

    public void Toggle()
    {
        if (_open) Close();
        else Open();
    }

    void Open()
    {
        if (_session is null || !settings.Enabled) return;

        _open = true;
        _world = _session.World;
        _speeds = _session.Simulation.SaveSpeeds();
        _session.Simulation.Pause();
        _status = "";
        OpenSquads(_session.CurrentSnapshot);
    }

    /// <summary>Closes the window; the speed comes back unless the player changed it while the window was open.</summary>
    void Close()
    {
        _open = false;
        _pick = null;
        if (_session is { } s && _speeds is { } speeds && s.Simulation.Speed == 0) s.Simulation.RestoreSpeeds(speeds);
        _speeds = null;
    }

    /// <summary>A new session or world: the window closes and the steps left are dropped (their rows are gone).</summary>
    void Reset(WorldSession session)
    {
        _open = false;
        _pick = null;
        _speeds = null;
        _steps.Clear();
        _waitFor = null;
        _session = session;
        _world = session.World;
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
