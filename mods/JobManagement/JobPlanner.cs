using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Production;

namespace Meitou.JobManagement;

/// <summary>
/// Turns the window's edits into the engine's job commands (<see cref="AddJob"/>, <see cref="AddOrderJob"/>, <see cref="RemoveJob"/>,
/// <see cref="MoveJob"/>), which the world applies in order and also while paused. Each plan works on a copy of the list as it will be after the
/// commands before it, so the indices of a sequence stay right.
/// </summary>
public static class JobPlanner
{
    /// <summary>Moves the unit holding <paramref name="row"/> one unit up (<paramref name="step"/> -1) or down (+1).</summary>
    public static List<SimCommand> Shift(CharacterId who, IReadOnlyList<PermaJob> jobs, int row, int step, long tick)
    {
        var units = JobRules.Units(jobs);
        int unit = JobRules.UnitOf(units, row);
        int other = unit + step;
        if (unit < 0 || other < 0 || other >= units.Count) return [];

        // Moving down is the next unit moving up past this one.
        var (moving, before) = step < 0 ? (units[unit], units[other]) : (units[other], units[unit]);
        var commands = new List<SimCommand>();
        for (int k = 0; k < moving.Length; k++)
            commands.Add(new MoveJob(who, moving.Start + k, before.Start + k) { Tick = tick });
        return commands;
    }

    /// <summary>Removes the unit holding <paramref name="row"/>, its last row first.</summary>
    public static List<SimCommand> Remove(CharacterId who, IReadOnlyList<PermaJob> jobs, int row, long tick)
    {
        var units = JobRules.Units(jobs);
        int unit = JobRules.UnitOf(units, row);
        return unit < 0 ? [] : RemoveRows(who, Enumerable.Range(units[unit].Start, units[unit].Length), tick);
    }

    /// <summary>Empties the list, last row first.</summary>
    public static List<SimCommand> Clear(CharacterId who, IReadOnlyList<PermaJob> jobs, long tick) =>
        RemoveRows(who, Enumerable.Range(0, jobs.Count), tick);

    /// <summary>RemoveJob for each of <paramref name="rows"/>, highest first, so the others keep their indices.</summary>
    static List<SimCommand> RemoveRows(CharacterId who, IEnumerable<int> rows, long tick) =>
        [.. rows.Distinct().OrderDescending().Select(r => (SimCommand)new RemoveJob(who, r) { Tick = tick })];

    /// <summary>Removes every unit that has a row on <paramref name="site"/> (a station's worker unassigned).</summary>
    public static List<SimCommand> RemoveSite(CharacterId who, IReadOnlyList<PermaJob> jobs, string site, long tick)
    {
        var units = JobRules.Units(jobs);
        var rows = units.Where(u => Enumerable.Range(u.Start, u.Length).Any(r => jobs[r].Site == site))
            .SelectMany(u => Enumerable.Range(u.Start, u.Length));
        return RemoveRows(who, rows, tick);
    }

    /// <summary>Removes the rows <paramref name="invalid"/> names for <paramref name="who"/> (never a protected one).</summary>
    public static List<SimCommand> RemoveInvalid(CharacterId who, IReadOnlyList<PermaJob> jobs, IReadOnlySet<(CharacterId, PermaJob)> invalid,
        long tick)
    {
        var rows = Enumerable.Range(0, jobs.Count).Where(r => !JobRules.IsProtected(jobs[r].Type) && invalid.Contains((who, jobs[r])));
        return RemoveRows(who, rows, tick);
    }

    /// <summary>
    /// The healing roles the list lacks, as orders to add: Rescue brings its Put in Bed companion, so Put in Bed then is not asked again.
    /// </summary>
    public static List<SimCommand> AddHealing(CharacterId who, IReadOnlyList<PermaJob> jobs, long tick)
    {
        var have = jobs.Select(j => j.Type).ToHashSet();
        var commands = new List<SimCommand>();
        foreach (var (role, ordered) in JobRules.HealingRoles)
        {
            if (!have.Add(role)) continue;

            if (role == TaskType.FindAndRescueIfTheresBeds) have.Add(TaskType.FindBedAndPutIn);
            commands.Add(new AddOrderJob([who], ordered, CharacterId.None) { Tick = tick });
        }

        return commands;
    }

    /// <summary>Whether the list has every healing role.</summary>
    public static bool HasHealing(IReadOnlyList<PermaJob> jobs) => JobRules.HealingRoles.All(r => jobs.Any(j => j.Type == r.Role));

    /// <summary>
    /// Prioritize Healing: a stable reorder by <see cref="JobRules.Rank"/>. With <paramref name="dedupe"/> a second copy of the same healing row is
    /// removed first (Rescue's companion added to a list that had Put in Bed).
    /// </summary>
    public static List<SimCommand> Prioritize(CharacterId who, IReadOnlyList<PermaJob> jobs, bool dedupe, long tick)
    {
        var rows = jobs.ToList();
        var commands = new List<SimCommand>();
        if (dedupe) RemoveHealingCopies(who, rows, commands, tick);

        var order = Enumerable.Range(0, rows.Count).OrderBy(i => JobRules.Rank(rows[i].Type)).ToList();
        var current = Enumerable.Range(0, rows.Count).ToList();
        for (int at = 0; at < order.Count; at++)
        {
            int from = current.IndexOf(order[at]);
            if (from == at) continue;

            commands.Add(new MoveJob(who, from, at) { Tick = tick });
            current.RemoveAt(from);
            current.Insert(at, order[at]);
        }

        return commands;
    }

    /// <summary>Removes (from the end) each healing row equal to one before it.</summary>
    static void RemoveHealingCopies(CharacterId who, List<PermaJob> rows, List<SimCommand> commands, long tick)
    {
        for (int k = rows.Count - 1; k > 0; k--)
        {
            if (!JobRules.IsHealing(rows[k].Type) || rows.IndexOf(rows[k]) == k) continue;

            commands.Add(new RemoveJob(who, k) { Tick = tick });
            rows.RemoveAt(k);
        }
    }

    /// <summary>
    /// The command that gives <paramref name="to"/> the unit holding <paramref name="row"/> of <paramref name="from"/>'s list (its first row: the
    /// engine adds the companion), or the reason it cannot: the receiver would refuse it, or the job cannot be named again.
    /// </summary>
    public static (SimCommand? Add, string? Refused) Give(CharacterSnapshot from, int row, CharacterSnapshot to, long tick)
    {
        var units = JobRules.Units(from.Jobs);
        int unit = JobRules.UnitOf(units, row);
        if (unit < 0) return (null, "That job is gone.");

        var job = from.Jobs[units[unit].Start];
        if (Refusal(job, to) is { } refused) return (null, refused);
        return Give(job, to.Id, tick) is { } add ? (add, null) : (null, $"{JobRules.Name(job.Type)} cannot be given again.");
    }

    /// <summary>Whether <paramref name="jobs"/> has a job like <paramref name="job"/> (its type on its target): a give arrived.</summary>
    public static bool Holds(IReadOnlyList<PermaJob> jobs, PermaJob job) => jobs.Any(j => j.Type == job.Type && JobRules.SameTarget(j, job));

    /// <summary>Why <paramref name="to"/> would not take <paramref name="job"/>, or null.</summary>
    static string? Refusal(PermaJob job, CharacterSnapshot to)
    {
        if (JobRules.Refuses(to.Jobs, job)) return $"{to.Name} already has {JobRules.Name(job.Type)}.";
        if (JobRules.IsAnimal(to) && !JobRules.AnimalTakes(job.Type)) return $"{to.Name} is an animal.";
        if (job.Character == to.Id) return $"{to.Name} cannot follow itself.";
        return null;
    }

    /// <summary>
    /// The command that gives <paramref name="to"/> a job like <paramref name="job"/>, or null when the engine has no order for it.
    /// </summary>
    static SimCommand? Give(PermaJob job, CharacterId to, long tick)
    {
        if (job.Type == TaskType.Unnamed289 || PermaJobs.JobOf(job.Type) != job.Type) return null;
        if (job.Site is { } site) return new AddJob([to], job.Type, site) { Tick = tick };
        if (!PermaJobs.Follows(job.Type)) return new AddOrderJob([to], job.Type, CharacterId.None) { Tick = tick };
        return job.Character.IsNone ? null : new AddOrderJob([to], job.Type, job.Character) { Tick = tick };
    }
}
