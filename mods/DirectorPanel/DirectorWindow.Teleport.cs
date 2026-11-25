using System.Globalization;
using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;

namespace Meitou.DirectorPanel;

/// <summary>The Teleport and World tabs, and the map's Shift + left click.</summary>
internal sealed partial class DirectorWindow
{
    int _point = -1;
    bool _renaming;
    long _renewAt;

    IEnumerable<UiNode> TeleportTab()
    {
        var at = Target?.Position;
        yield return Label("tp-now", at is { } p ? texts.Format("Current position:  X %.0f", p.X) + $"  Y {F0(p.Z)}" : texts["Current position: nobody selected"]);
        yield return Hint("tp-map", "Teleport by map. While the director panel is open, open the in-game map and Shift + left-click the spot you want. The selected characters will move there.");

        yield return Heading("tp-ch", "Teleport to coordinates:");
        var coords = Row("tp-xy");
        coords.Children.Add(Field("tp-x", 120, at is { } a ? F0(a.X) : "0"));
        coords.Children.Add(Field("tp-z", 120, at is { } b ? F0(b.Z) : "0"));
        coords.Children.Add(Button("tp-go", texts["Teleport"], () => Move(new Vector3(Number("tp-x", 0), 0, Number("tp-z", 0))), 200,
            !_target.IsNone));
        yield return coords;

        foreach (var n in AddPointRows(at)) yield return n;
        foreach (var n in SavedPointRows()) yield return n;
    }

    /// <summary>Moves the selection (else the panel's character) to the point, on the ground there.</summary>
    void Move(Vector3 to)
    {
        IReadOnlyList<Simulation.CharacterId> who = _selected.Count > 0 ? _selected : [_target];
        if (who.Count > 0 && !who[0].IsNone) Send(new Teleport(who, to) { Tick = context.Tick });
    }

    IEnumerable<UiNode> AddPointRows(Vector3? at)
    {
        yield return Heading("tp-ah", "Add a point:");
        var line = Row("tp-add");
        line.Children.Add(Field("tp-name", 240));
        line.Children.Add(Button("tp-here", texts["By character position"], () => AddPoint(at), 200, at is not null));
        line.Children.Add(Button("tp-coord", texts["By coordinates"], () => AddPoint(new Vector3(Number("tp-x", 0), 0, Number("tp-z", 0))), 170));
        yield return line;
    }

    void AddPoint(Vector3? at)
    {
        string name = Text("tp-name").Trim();
        if (name.Length == 0)
        {
            _note = texts["Enter a point name"];
            return;
        }

        if (at is not { } p) return;

        _note = points.Add(name, p) ? texts["Point added: "] + name : texts["Could not save the points list"];
    }

    IEnumerable<UiNode> SavedPointRows()
    {
        yield return Heading("tp-sh", "Saved points:");
        var all = points.All;
        for (int i = 0; i < all.Count; i++)
        {
            int k = i;
            yield return Button("tp-p" + i, Points.Shown(all[i], texts.Russian), () => (_point, _renaming) = (k, false), 0, picked: i == _point);
        }

        if ((uint)_point >= (uint)all.Count) yield break;

        var line = Row("tp-act");
        line.Children.Add(Button("tp-pgo", texts["Teleport to point"], () => Move(all[_point].At), 200, !_target.IsNone));
        line.Children.Add(Button("tp-ren", texts["rename"], () => _renaming = true, 120));
        line.Children.Add(Button("tp-del", texts["erase"], () => (_note, _point) = (points.Erase(_point) ? "" : texts["Could not save the points list"], -1), 120));
        yield return line;

        if (!_renaming) yield break;

        var rename = Row("tp-rn");
        rename.Children.Add(Label("tp-rnl", texts["New name:"], width: 120));
        rename.Children.Add(Field("tp-rnname", 240, all[_point].Name));
        rename.Children.Add(Button("tp-rnok", texts["Save##rn"].Replace("##rn", ""), Rename, 100));
        rename.Children.Add(Button("tp-rnno", texts["Cancel##rn"].Replace("##rn", ""), () => _renaming = false, 100));
        yield return rename;
    }

    void Rename()
    {
        _renaming = false;
        _note = points.Rename(_point, Text("tp-rnname")) ? texts["Point renamed"] : texts["Could not save the points list"];
    }

    /// <summary>Shift + left click on the world map while the panel is open moves the selection there (the original's map teleport).</summary>
    bool MapClick(UiMapInput input)
    {
        if (!_open || input.Kind != UiMapInputKind.Press || input.Button != MouseButton.Left || !input.Shift || input.View is not { } view)
            return false;

        var spot = view.ScreenToWorld(input.Point);
        Move(new Vector3(spot.X, 0, spot.Y));
        return true;
    }

    IEnumerable<UiNode> WorldTab()
    {
        foreach (var n in OpacityRows()) yield return n;

        yield return Heading("wo-h", "Time of day:");
        yield return Hint("wo-warn", "Warning: changing the normal flow of time may cause some game mechanics to work incorrectly.");
        if (ClockPatch.Clock is not { } clock)
        {
            yield return Hint("wo-none", "Game clock not found. Time control works only in version 1.0.65 under RE_Kenshi and only in a loaded world.");
            yield break;
        }

        yield return Label("wo-now", texts.Format("Now: day %d, %02d:%02d", clock.Day, clock.Hour, clock.Minute));
        yield return Tick("wo-freeze", "Freeze time", ClockPatch.Frozen is not null, on => ClockPatch.Frozen = on ? clock.HourOfDay : null);
        yield return Hint("wo-fwd", "Time only moves forward: if the chosen hour is earlier than now, it becomes the next day.");
        var set = Row("wo-set");
        set.Children.Add(Label("wo-hl", texts["Hour:"], width: 70));
        set.Children.Add(Field("wo-hour", 60, clock.Hour.ToString(CultureInfo.InvariantCulture)));
        set.Children.Add(Label("wo-ml", texts["Minutes:"], width: 90));
        set.Children.Add(Field("wo-min", 60, "0"));
        set.Children.Add(Button("wo-go", texts["Set time"], SetTime, 160));
        yield return set;
    }

    void SetTime()
    {
        int hour = (int)Number("wo-hour", 0), minute = (int)Number("wo-min", 0);
        _note = ClockPatch.Set(hour, minute) ? texts.Format("Time set: %02d:%02d", Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59)) : texts["Could not set the time"];
    }

    /// <summary>Renews the looped effects about every 10 seconds of frames (the effects last 30).</summary>
    void KeepLooped()
    {
        long now = Environment.TickCount64;
        if (_looped.Count == 0 || now < _renewAt) return;

        _renewAt = now + 10_000;
        RenewLooped();
    }
}
