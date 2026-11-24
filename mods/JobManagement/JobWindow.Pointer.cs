using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>Where a dragged job lands: a member's lines (move it there; on its own list, to <see cref="Row"/>) or the DROP TO REMOVE zone.</summary>
readonly record struct JobDrop(CharacterId Member, bool Remove, int Row = -1);

/// <summary>
/// The pointer: dragging the picked jobs onto another member, within one list or onto DROP TO REMOVE, and the wheel over the lists (one line a
/// notch).
/// </summary>
public sealed partial class JobWindow
{
    /// <summary>How far (pixels, on either axis) a press moves before it is a drag rather than a click (the original's 5).</summary>
    const float DragSlop = 5;

    /// <summary>The drop targets of the shown lines, by node id (each node of a member's lines, the remove zone).</summary>
    readonly Dictionary<string, JobDrop> _drops = new(StringComparer.Ordinal);

    /// <summary>The job each drag button holds and its click, by the button's argument.</summary>
    readonly Dictionary<string, (JobPick Pick, Action Click)> _dragJobs = new(StringComparer.Ordinal);

    /// <summary>The jobs being dragged (the picked ones); null: none.</summary>
    List<JobPick>? _drag;

    /// <summary>A job's button: a click picks it, a drag carries it (with the other picked jobs).</summary>
    UiNode DragButton(string key, string text, float width, JobPick job, Action onClick, bool selected)
    {
        _dragJobs[key] = (job, onClick);
        var style = selected ? _styles!.Selected : _styles!.Button;
        return UiKit.Button("jm-" + key, text, width, style, _dragAction, key, pointer: UiPointerEvents.Drag | UiPointerEvents.Release);
    }

    /// <summary>Makes <paramref name="node"/> and its children drop targets for <paramref name="member"/>'s row <paramref name="row"/>.</summary>
    UiNode DropOn(UiNode node, CharacterId member, int row)
    {
        _drops[node.Id] = new(member, Remove: false, row);
        foreach (var child in node.Children) DropOn(child, member, row);
        return node;
    }

    /// <summary>The original's DROP TO REMOVE zone, shown while a job is dragged.</summary>
    UiNode DropToRemove(float width)
    {
        const string id = "jm-droptoremove";
        _drops[id] = new(default, Remove: true);
        return new(id, UiNodeKind.Label)
        {
            Placement = new(UiKit.S(width), UiKit.S(UiKit.LineHeight)), Text = "DROP TO REMOVE", Style = _styles!.Warn, Pickable = true,
            TextScale = UiKit.Scale,
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

    /// <summary>
    /// A press moved past the slop: the drag carries the picked jobs, or the held one alone when it is not picked (the original's single and
    /// multi-job drags).
    /// </summary>
    void StartDrag(UiActionContext c)
    {
        var moved = c.Position - c.Start;
        if (_drag is not null || Busy || Math.Max(Math.Abs(moved.X), Math.Abs(moved.Y)) < DragSlop) return;
        if (!_dragJobs.TryGetValue(c.Argument, out var held)) return;

        if (!_picks.Contains(held.Pick))
        {
            _picks.Clear();
            _picks.Add(held.Pick);
            RecipientsFromPicks();
        }

        _drag = [.. _picks];
    }

    /// <summary>
    /// A drag let go over <paramref name="over"/>: onto DROP TO REMOVE it removes the jobs; one job on its own list moves to the row it lands on;
    /// onto a member owning none of them it moves them there (one job: a refused give keeps it; several: the ones the member would refuse stay
    /// where they are, as the original's "leaving duplicates in place").
    /// </summary>
    void Drop(string over)
    {
        if (_drag is not { } jobs) return;

        _drag = null;
        if (!_drops.TryGetValue(over, out var drop)) return;

        if (drop.Remove)
        {
            RemovePicks(jobs);
            return;
        }

        if (jobs.Count == 1 && jobs[0].Owner == drop.Member)
        {
            Reorder(jobs[0], drop.Row);
            return;
        }

        if (jobs.Any(j => j.Owner == drop.Member))
        {
            Status = "Move cancelled: choose a member that does not own any selected source job.";
            return;
        }

        GiveTo(jobs, drop.Member, copy: false, skipRefused: jobs.Count > 1);
    }

    /// <summary>One job dragged within its own list: its unit goes where the unit on <paramref name="row"/> stands.</summary>
    void Reorder(JobPick job, int row)
    {
        if (row < 0 || row == job.Row) return;

        EditRow(job.Owner, job.Row, job.Job, (c, t) => JobPlanner.MoveUnit(job.Owner, c.Jobs, job.Row, row, t));
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
