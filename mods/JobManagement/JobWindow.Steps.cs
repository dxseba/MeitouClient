using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

public sealed partial class JobWindow
{
    /// <summary>Commands sent and their snapshot not seen yet: a click now would plan on rows that are about to change, so it is ignored.</summary>
    bool Busy => _waitFor is not null || _steps.Count > 0;

    /// <summary>
    /// Queues an edit's steps. Each plans its commands on the snapshot of its time; null means its precondition is not met yet (it is asked again on
    /// a later snapshot, then dropped), an empty list that there is nothing to do.
    /// </summary>
    void Run(params Func<WorldSnapshot, IReadOnlyList<SimCommand>?>[] steps)
    {
        if (Busy)
        {
            _status = "Still applying the last change...";
            return;
        }

        foreach (var step in steps) _steps.Enqueue(step);
        Pump();
    }

    /// <summary>Runs the steps that are due: after the snapshot moved on from the last commands (or a second has passed).</summary>
    void Pump()
    {
        if (_snapshot is not { } snapshot) return;

        if (_waitFor is not null)
        {
            if (ReferenceEquals(snapshot, _waitFor) && ++_waitFrames < MaxWaitFrames) return;
            _waitFor = null;
        }

        while (_steps.Count > 0 && _waitFor is null) RunStep(snapshot);
    }

    void RunStep(WorldSnapshot snapshot)
    {
        var commands = _steps.Peek()(snapshot);
        if (commands is null)
        {
            if (++_retries > MaxRetries)
            {
                _steps.Clear();
                _retries = 0;
                _status = "The game did not take the change; nothing more was done.";
                return;
            }

            Wait(snapshot);
            return;
        }

        _steps.Dequeue();
        _retries = 0;
        if (_abort)
        {
            _abort = false;
            _steps.Clear();
        }

        if (commands.Count == 0) return;

        foreach (var command in commands) enqueue(command);
        Wait(snapshot);
    }

    /// <summary>A step's way to end its edit: the steps after it are dropped and the status says why.</summary>
    List<SimCommand> Abort(string status)
    {
        _abort = true;
        _status = status;
        return [];
    }

    void Wait(WorldSnapshot snapshot)
    {
        _waitFor = snapshot;
        _waitFrames = 0;
    }

    /// <summary>The overlay: the JM button and, while open, the window.</summary>
    UiNode Build(UiFrameContext frame)
    {
        _clicks.Clear();
        _dragJobs.Clear();
        if (!settings.Enabled)
        {
            if (_open) Close();
            return new("jm-off") { Visible = false };
        }

        var root = new UiNode("jm") { Placement = UiPlacement.Fill, Pickable = false };
        if (settings.ShowButton)
        {
            _clicks["toggle"] = Toggle;
            root.Children.Add(new("jm-toggle", UiNodeKind.Button)
            {
                Placement = new(40, 26, Left: 486, Top: 44), Text = "JM", Style = _open ? _styles!.Selected : _styles!.Button,
                Action = _clickAction, Argument = "toggle",
            });
        }

        if (_open && _snapshot is { } snapshot) root.Children.Add(Window(frame.Viewport, snapshot));
        return root;
    }

    UiNode Window(UiRect view, WorldSnapshot snapshot)
    {
        float width = Math.Min(1000, view.Width - 32), height = Math.Min(660, view.Height - 150);
        var window = new UiNode("jm-window")
        {
            Placement = new(width, height, Left: (view.Width - width) / 2, Top: 80), Style = _styles!.Panel, Flow = UiFlow.Column,
            Padding = 8, Gap = UiKit.Gap,
        };

        float inner = width - 16, body = height - 16 - UiKit.RowHeight - UiKit.Gap;
        window.Children.Add(Header(inner));
        window.Children.Add(_tab == Tab.Squads ? SquadTab(snapshot, inner, body) : StationTab(snapshot, inner, body));
        return window;
    }

    UiNode Header(float width)
    {
        var row = UiKit.Row("jm-header");
        row.Children.Add(Label("title", "Job Management", 170));
        row.Children.Add(Button("tab.squads", "Squad Jobs", 110, () => _tab = Tab.Squads, selected: _tab == Tab.Squads));
        row.Children.Add(Button("tab.stations", "Stations", 110, () => _tab = Tab.Stations, selected: _tab == Tab.Stations));

        string status = Busy ? "Applying..." : _status;
        row.Children.Add(Label("status", status, Math.Max(40, width - 170 - 220 - 70 - 4 * UiKit.Gap), _styles!.Dim));
        row.Children.Add(Button("close", "Close", 70, Close));
        return row;
    }

    /// <summary>The characters of the snapshot by id.</summary>
    static Dictionary<CharacterId, CharacterSnapshot> ById(WorldSnapshot snapshot)
    {
        var map = new Dictionary<CharacterId, CharacterSnapshot>();
        foreach (var c in snapshot.Characters) map[c.Id] = c;
        return map;
    }

    static CharacterSnapshot? Find(WorldSnapshot snapshot, CharacterId id) => snapshot.Characters.FirstOrDefault(c => c.Id == id);

    /// <summary>The board the world publishes (skills, invalid rows); empty before the first tick.</summary>
    JobBoard Board() => _session?.World.Systems.OfType<JobBoardSystem>().FirstOrDefault()?.Board ?? JobBoard.Empty;
}
