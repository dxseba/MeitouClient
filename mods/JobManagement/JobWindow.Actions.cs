using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>The Squad Jobs tab's edits that take more than one character or more than one step.</summary>
public sealed partial class JobWindow
{
    /// <summary>
    /// Move or copy: the receiver is given the job first; a move takes it off the giver only once the receiver's list shows it (so a refused add
    /// never loses the job).
    /// </summary>
    void GiveTo(JobPick pick, CharacterId to, bool copy)
    {
        _pick = null;
        Run(s => GiveStep(s, pick, to), s => copy ? Array.Empty<SimCommand>() : TakeStep(s, pick, to));
    }

    IReadOnlyList<SimCommand> GiveStep(WorldSnapshot snapshot, JobPick pick, CharacterId to)
    {
        if (Find(snapshot, pick.Owner) is not { } from || Find(snapshot, to) is not { } receiver || !Holds(from, pick))
        {
            return Abort("That job is gone.");
        }

        var (add, refused) = JobPlanner.Give(from, pick.Row, receiver, Tick);
        if (add is null)
        {
            return Abort(refused ?? "");
        }

        _status = $"{JobRules.Name(pick.Job.Type)} given to {receiver.Name}.";
        return [add];
    }

    /// <summary>The move's second step: null (wait) until the receiver holds the job.</summary>
    IReadOnlyList<SimCommand>? TakeStep(WorldSnapshot snapshot, JobPick pick, CharacterId to)
    {
        if (Find(snapshot, to) is not { } receiver || !JobPlanner.Holds(receiver.Jobs, pick.Job)) return null;
        if (Find(snapshot, pick.Owner) is not { } from || !Holds(from, pick)) return [];

        _status = $"{JobRules.Name(pick.Job.Type)} moved to {receiver.Name}.";
        return JobPlanner.Remove(from.Id, from.Jobs, pick.Row, Tick);
    }

    static bool Holds(CharacterSnapshot owner, JobPick pick) => pick.Row < owner.Jobs.Count && owner.Jobs[pick.Row] == pick.Job;

    /// <summary>
    /// Add Medic/Robotics: the missing healing roles for each person of the squad, then the healing order (a doubled Put in Bed removed).
    /// </summary>
    void AddHealing(IReadOnlyList<CharacterId> ids)
    {
        Run(s => ForPeople(s, ids, c => JobPlanner.AddHealing(c.Id, c.Jobs, Tick), "Healing jobs added"),
            s => People(s, ids).Any(c => !JobPlanner.HasHealing(c.Jobs))
                ? null
                : ForPeople(s, ids, c => JobPlanner.Prioritize(c.Id, c.Jobs, dedupe: true, Tick), "Healing jobs added and prioritized"));
    }

    void PrioritizeHealing(IReadOnlyList<CharacterId> ids) =>
        Run(s => ForPeople(s, ids, c => JobPlanner.Prioritize(c.Id, c.Jobs, dedupe: false, Tick), "Healing priorities updated"));

    /// <summary>Remove Invalid Jobs, from the rows the world last found without a target.</summary>
    void RemoveInvalid(IReadOnlyList<CharacterId> ids)
    {
        var board = Board();
        if (board.Tick < 0)
        {
            _status = "No data yet: let the game run a moment.";
            return;
        }

        Run(s => ForPeople(s, ids, c => JobPlanner.RemoveInvalid(c.Id, c.Jobs, board.Invalid, Tick), "Invalid jobs removed", animals: true));
    }

    /// <summary>The squad's members in the snapshot; people only unless <paramref name="animals"/>.</summary>
    static IEnumerable<CharacterSnapshot> People(WorldSnapshot snapshot, IReadOnlyList<CharacterId> ids, bool animals = false) =>
        ids.Select(id => Find(snapshot, id)).OfType<CharacterSnapshot>().Where(c => animals || !JobRules.IsAnimal(c));

    /// <summary>One plan per member, with the status line saying how many rows changed.</summary>
    List<SimCommand> ForPeople(WorldSnapshot snapshot, IReadOnlyList<CharacterId> ids, Func<CharacterSnapshot, List<SimCommand>> plan, string done,
        bool animals = false)
    {
        var commands = People(snapshot, ids, animals).SelectMany(plan).ToList();
        _status = commands.Count == 0 ? "Nothing to do." : $"{done} ({commands.Count.ToString(CultureInfo.InvariantCulture)} changes).";
        return commands;
    }

    /// <summary>The list's scroll buttons and position.</summary>
    UiNode ScrollBar(string id, int count, int visible, int scroll, Action<int> set, float width)
    {
        var row = UiKit.Row($"jm-scroll.{id}");
        int step = Math.Max(1, visible - 2);
        row.Children.Add(Button($"{id}.up", "Scroll up", 90, () => set(scroll - step), scroll > 0));
        row.Children.Add(Button($"{id}.down", "Scroll down", 100, () => set(scroll + step), scroll + visible < count));
        string shown = count == 0 ? "empty" : $"{scroll + 1}-{Math.Min(count, scroll + visible)} of {count}";
        row.Children.Add(Label($"{id}.pos", shown, Math.Max(40, width - 190 - 2 * UiKit.Gap), _styles!.Dim));
        return row;
    }
}
