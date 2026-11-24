using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>The Squad Jobs tab's edits that take more than one character or more than one step.</summary>
public sealed partial class JobWindow
{
    /// <summary>What a give step sent: the jobs given (the move's second step takes only these) and how many it left in place.</summary>
    sealed class Giving(Func<string, string> tr)
    {
        public List<JobPick> Given { get; } = [];

        public int Skipped { get; set; }

        public string Skips => Skipped > 0 ? " " + Fill(tr("Skipped duplicate bundles: {count}."), ("count", Skipped)) : "";
    }

    /// <summary>
    /// Move or copy the picked jobs: the receiver is given them first; a move takes them off their givers only once the receiver's list shows them
    /// all (so a refused add never loses a job). A job refused stops the whole edit, as the original's "No jobs were moved", unless
    /// <paramref name="skipRefused"/> (a multi-job drag), which leaves it where it is.
    /// </summary>
    void GiveTo(IReadOnlyList<JobPick> picks, CharacterId to, bool copy, bool skipRefused = false)
    {
        _picks.Clear();
        RecipientsFromPicks();
        var giving = new Giving(tr);
        Run(s => GiveStep(s, picks, to, giving, skipRefused), s => copy ? Array.Empty<SimCommand>() : TakeStep(s, giving, to));
    }

    IReadOnlyList<SimCommand> GiveStep(WorldSnapshot snapshot, IReadOnlyList<JobPick> picks, CharacterId to, Giving giving, bool skipRefused)
    {
        if (Find(snapshot, to) is not { } receiver) return Abort("That member is gone.");

        var adds = new List<SimCommand>();
        foreach (var pick in picks)
        {
            if (Find(snapshot, pick.Owner) is not { } from || !Holds(from, pick)) return Abort("That job is gone.");

            var (add, refused) = Twice(giving.Given, pick, receiver) ?? JobPlanner.Give(from, pick.Row, receiver, Tick);
            if (add is not null)
            {
                adds.Add(add);
                giving.Given.Add(pick);
            }
            else if (skipRefused) giving.Skipped++;
            else return Abort(refused ?? "");
        }

        if (adds.Count == 0) return Abort("No jobs were moved." + giving.Skips);

        Status = Done(giving.Given, "given to", receiver.Name) + giving.Skips;
        return adds;
    }

    /// <summary>A refusal when a job like <paramref name="pick"/>'s is already among the ones this edit gives (it would be held twice).</summary>
    static (SimCommand? Add, string? Refused)? Twice(List<JobPick> given, JobPick pick, CharacterSnapshot receiver) =>
        given.Any(g => g.Job.Type == pick.Job.Type && JobRules.SameTarget(g.Job, pick.Job))
            ? (null, $"{receiver.Name} already has {JobRules.Name(pick.Job.Type)}.")
            : null;

    /// <summary>The move's second step: null (wait) until the receiver holds every given job; then each giver loses what it still holds.</summary>
    IReadOnlyList<SimCommand>? TakeStep(WorldSnapshot snapshot, Giving giving, CharacterId to)
    {
        var picks = giving.Given;
        if (Find(snapshot, to) is not { } receiver || picks.Any(p => !JobPlanner.Holds(receiver.Jobs, p.Job))) return null;

        Status = Done(picks, "moved to", receiver.Name) + giving.Skips;
        return RemoveHeld(snapshot, picks);
    }

    static string Done(IReadOnlyList<JobPick> picks, string verb, string name) =>
        picks.Count == 1 ? $"{JobRules.Name(picks[0].Job.Type)} {verb} {name}." : $"{picks.Count} jobs {verb} {name}.";

    /// <summary>The original's Remove Job(s): every picked job (with its companion) off its owner's list.</summary>
    void RemovePicked() => RemovePicks([.. _picks]);

    /// <summary>Removes <paramref name="picks"/> (Remove Job(s), DROP TO REMOVE); the pick is let go.</summary>
    void RemovePicks(IReadOnlyList<JobPick> picks)
    {
        _picks.Clear();
        RecipientsFromPicks();
        Run(s =>
        {
            var commands = RemoveHeld(s, picks);
            Status = Fill(Tr("Removed permanent jobs: {count}."), ("count", commands.Count));
            return commands;
        });
    }

    /// <summary>For each owner, the removal of the picked units it still holds.</summary>
    List<SimCommand> RemoveHeld(WorldSnapshot snapshot, IReadOnlyList<JobPick> picks)
    {
        var commands = new List<SimCommand>();
        foreach (var group in picks.GroupBy(p => p.Owner))
        {
            if (Find(snapshot, group.Key) is { } owner)
                commands.AddRange(JobPlanner.RemoveUnits(owner.Id, owner.Jobs, group.Where(p => Holds(owner, p)).Select(p => p.Row), Tick));
        }

        return commands;
    }

    static bool Holds(CharacterSnapshot owner, JobPick pick) => pick.Row < owner.Jobs.Count && owner.Jobs[pick.Row] == pick.Job;

    /// <summary>
    /// Add Medic/Robotics: the missing healing roles for each person among the recipients, then the healing order (a doubled Put in Bed removed).
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
            Status = "No data yet: let the game run a moment.";
            return;
        }

        Run(s => ForPeople(s, ids, c => JobPlanner.RemoveInvalid(c.Id, c.Jobs, board.Invalid, Tick), "Invalid jobs removed", animals: true));
    }

    /// <summary>The recipients in the snapshot; people only unless <paramref name="animals"/>.</summary>
    static IEnumerable<CharacterSnapshot> People(WorldSnapshot snapshot, IReadOnlyList<CharacterId> ids, bool animals = false) =>
        ids.Select(id => Find(snapshot, id)).OfType<CharacterSnapshot>().Where(c => animals || !JobRules.IsAnimal(c));

    /// <summary>One plan per member, with the status line saying how many rows changed.</summary>
    List<SimCommand> ForPeople(WorldSnapshot snapshot, IReadOnlyList<CharacterId> ids, Func<CharacterSnapshot, List<SimCommand>> plan, string done,
        bool animals = false)
    {
        var commands = People(snapshot, ids, animals).SelectMany(plan).ToList();
        Status = commands.Count == 0 ? "Nothing to do." : $"{done} ({commands.Count.ToString(CultureInfo.InvariantCulture)} changes).";
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
