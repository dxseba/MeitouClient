using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>Where a dragged job lands: a member's lines (move it there) or the DROP TO REMOVE zone.</summary>
readonly record struct JobDrop(CharacterId Member, bool Remove);

/// <summary>The pointer: dragging a job onto another member or DROP TO REMOVE, and the wheel over the lists (one line a notch).</summary>
public sealed partial class JobWindow
{
    /// <summary>How far (pixels) a press moves before it is a drag rather than a click.</summary>
    const float DragSlop = 6;

    /// <summary>The drop targets of the shown lines, by node id (each node of a member's lines, the remove zone).</summary>
    readonly Dictionary<string, JobDrop> _drops = new(StringComparer.Ordinal);

    /// <summary>The job each drag button holds and its click, by the button's argument.</summary>
    readonly Dictionary<string, (JobPick Pick, Action Click)> _dragJobs = new(StringComparer.Ordinal);

    /// <summary>The job being dragged; null: none.</summary>
    JobPick? _drag;

    /// <summary>A job's button: a click picks it, a drag carries it.</summary>
    UiNode DragButton(string key, string text, float width, JobPick job, Action onClick, bool selected)
    {
        _dragJobs[key] = (job, onClick);
        var style = selected ? _styles!.Selected : _styles!.Button;
        return UiKit.Button("jm-" + key, text, width, style, _dragAction, key, pointer: UiPointerEvents.Drag | UiPointerEvents.Release);
    }

    /// <summary>Makes <paramref name="node"/> and its children drop targets for <paramref name="member"/>.</summary>
    void DropOn(UiNode node, CharacterId member)
    {
        _drops[node.Id] = new(member, Remove: false);
        foreach (var child in node.Children) DropOn(child, member);
    }

    /// <summary>The original's DROP TO REMOVE zone, shown while a job is dragged.</summary>
    UiNode DropToRemove(float width)
    {
        const string id = "jm-droptoremove";
        _drops[id] = new(default, Remove: true);
        return new(id, UiNodeKind.Label)
        {
            Placement = new(width, UiKit.LineHeight), Text = "DROP TO REMOVE", Style = _styles!.Warn, Pickable = true,
        };
    }

    void Drag(UiActionContext c)
    {
        switch (c.Kind)
        {
            case UiEventKind.Press:
                if (_dragJobs.TryGetValue(c.Argument, out var pressed)) pressed.Click();
                break;
            case UiEventKind.Drag:
                StartDrag(c);
                break;
            case UiEventKind.Release:
                Drop(c.Over);
                break;
        }
    }

    void StartDrag(UiActionContext c)
    {
        if (_drag is not null || Busy || (c.Position - c.Start).Length() < DragSlop) return;
        if (!_dragJobs.TryGetValue(c.Argument, out var held)) return;

        _drag = held.Pick;
        _pick = null;
    }

    /// <summary>A drag let go over <paramref name="over"/>: onto another member it moves the job (a refused give keeps it), onto the zone it removes it.</summary>
    void Drop(string over)
    {
        if (_drag is not { } job) return;

        _drag = null;
        if (!_drops.TryGetValue(over, out var drop)) return;

        if (drop.Remove)
        {
            EditRow(job.Owner, job.Row, job.Job, (c, t) => JobPlanner.Remove(job.Owner, c.Jobs, job.Row, t));
            return;
        }

        if (drop.Member != job.Owner) GiveTo(job, drop.Member, copy: false);
    }

    /// <summary>The wheel over a list: one line a notch, as the fork's (its <c>NormalizeMouseWheelNotches</c> fix).</summary>
    void Wheel(UiActionContext c)
    {
        int notches = (int)MathF.Round(c.Wheel);
        if (notches == 0) notches = Math.Sign(c.Wheel);

        switch (c.Argument)
        {
            case "jobs": _scroll -= notches; break;
            case "stations": _stationScroll -= notches; break;
            case "workers": _workerScroll -= notches; break;
        }
    }
}
