using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.TauntExpansion;

/// <summary>
/// The original's <c>[Logging]</c> diagnostics: one mod-log line per fighter's own target choice (the original logs each
/// <c>AI::chooseBestAttackTarget</c> call). The score filter records the choice's candidates from any thread; the next serial step groups them by
/// chooser in id order, picks the selection as the engine does (the first strictly best answer) and writes the lines that pass the filters.
/// Nothing is recorded while <see cref="TauntSettings.LogChoices"/> is off.
/// </summary>
sealed class TauntLog(TauntSettings settings, Action<string> log)
{
    /// <summary>
    /// One scored candidate as this mod answered it.
    /// </summary>
    readonly record struct Scored(long Tick, CharacterId Chooser, CharacterId Target, bool Taunt, bool ChooserHasTarget, float Score);

    /// <summary>
    /// A chooser's last logged line: its tick and the selection (the original keeps the time and the selected character per actor).
    /// </summary>
    readonly record struct Last(long Tick, CharacterId Selected);

    /// <summary>
    /// The candidates scored since the last serial step; filled on any thread.
    /// </summary>
    readonly ConcurrentQueue<Scored> _pending = new();

    /// <summary>
    /// The last logged line per chooser; serial steps only.
    /// </summary>
    readonly Dictionary<CharacterId, Last> _last = [];

    /// <summary>
    /// Copy of <see cref="TauntSettings.LogChoices"/>, read by the filter's threads.
    /// </summary>
    volatile bool _on;

    /// <summary>
    /// Copies of the other logging settings; serial steps only.
    /// </summary>
    bool _candidates, _tauntOnly, _changedOnly;

    /// <summary>
    /// Copies of the interval and the candidate cap; serial steps only.
    /// </summary>
    int _intervalMs, _maxCandidates;

    /// <summary>
    /// Copies the settings and writes the lines of the choices recorded since the last call; the serial step.
    /// </summary>
    public void Step(World world)
    {
        _candidates = settings.LogCandidates;
        _tauntOnly = settings.LogOnlyWhenTauntPresent;
        _changedOnly = settings.LogOnlyChangedSelection;
        _intervalMs = settings.MinimumLogIntervalMs;
        _maxCandidates = settings.MaximumCandidatesPerEntry;
        _on = settings.LogChoices;

        if (_pending.IsEmpty) return;

        foreach (var choice in Choices())
            if (_on) Write(world, choice);
    }

    /// <summary>
    /// Records a candidate of a fighter's own choice; any thread. Cheap and recording nothing while off.
    /// </summary>
    public void Record(in TargetScoreAsk ask, float answer)
    {
        if (!_on || ask.Kind != TargetChoiceKind.Fight) return;

        _pending.Enqueue(new Scored(ask.Tick, ask.Chooser, ask.Target, ask.Candidate.Taunt, ask.Candidate.ChooserHasTarget, answer));
    }

    /// <summary>
    /// The recorded candidates grouped by (tick, chooser), in tick then chooser-id order; each group keeps the order its candidates were scored
    /// in (one chooser's choice runs on one thread).
    /// </summary>
    List<List<Scored>> Choices()
    {
        var groups = new Dictionary<(long, CharacterId), List<Scored>>();
        while (_pending.TryDequeue(out var scored))
        {
            var key = (scored.Tick, scored.Chooser);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];

            list.Add(scored);
        }

        return groups
            .OrderBy(g => g.Key.Item1)
            .ThenBy(g => g.Key.Item2.Slot)
            .ThenBy(g => g.Key.Item2.Generation)
            .Select(g => g.Value)
            .ToList();
    }

    /// <summary>
    /// Writes one choice's line when it passes the original's filters.
    /// </summary>
    void Write(World world, List<Scored> choice)
    {
        if (_tauntOnly && !choice.Any(c => c.Taunt)) return;

        var selected = Selected(choice);
        if (!Due(world, choice[0], selected.Target)) return;

        log(Line(world, choice, selected));
    }

    /// <summary>
    /// The engine's pick among the answers: the first strictly best.
    /// </summary>
    static Scored Selected(List<Scored> choice)
    {
        var best = choice[0];
        foreach (var c in choice)
            if (c.Score > best.Score) best = c;

        return best;
    }

    /// <summary>
    /// The original's rate filters per chooser: a changed selection (when asked for) and at least the interval since its last line; a passing
    /// line becomes the chooser's last. The interval counts game time (the original's wall-clock milliseconds), so the log stays deterministic.
    /// </summary>
    bool Due(World world, in Scored first, CharacterId selected)
    {
        bool known = _last.TryGetValue(first.Chooser, out var last);
        if (known && _changedOnly && last.Selected == selected) return false;

        long intervalTicks = (long)Math.Ceiling(_intervalMs / 1000.0 / world.TickSeconds);
        if (known && _intervalMs > 0 && first.Tick - last.Tick < intervalTicks) return false;

        _last[first.Chooser] = new Last(first.Tick, selected);
        return true;
    }

    /// <summary>
    /// The line: the original's fields Meitou knows (actor, result, selected, score, candidate count, old target valid) and, with
    /// <see cref="TauntSettings.LogCandidates"/>, up to the maximum candidates with their Taunt and Selected flags.
    /// </summary>
    string Line(World world, List<Scored> choice, in Scored selected)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"[TauntExpansion:Target] Actor=\"{Name(world, selected.Chooser)}\" Result=true");
        text.Append(CultureInfo.InvariantCulture, $" Selected=\"{Name(world, selected.Target)}\" Score={selected.Score:0.####}");
        text.Append(CultureInfo.InvariantCulture, $" CandidateCount={choice.Count} OldTargetValid={Flag(selected.ChooserHasTarget)}");
        if (_candidates) AppendCandidates(world, text, choice, selected.Target);

        return text.ToString();
    }

    /// <summary>
    /// The candidate list, cut at the maximum with the count left out.
    /// </summary>
    void AppendCandidates(World world, StringBuilder text, List<Scored> choice, CharacterId selected)
    {
        int shown = Math.Min(choice.Count, _maxCandidates);
        text.Append(" Candidates=[");
        for (int k = 0; k < shown; k++)
        {
            if (k > 0) text.Append("; ");

            var c = choice[k];
            text.Append(CultureInfo.InvariantCulture, $"{{Name=\"{Name(world, c.Target)}\",Taunt={Flag(c.Taunt)},Selected={Flag(c.Target == selected)}}}");
        }

        if (choice.Count > shown) text.Append(CultureInfo.InvariantCulture, $"; ... truncated={choice.Count - shown}");

        text.Append(']');
    }

    /// <summary>
    /// A character's name, empty when it is gone.
    /// </summary>
    static string Name(World world, CharacterId id)
    {
        var table = world.Characters;
        return table.TryResolveNext(id, out int slot) ? table.Cold(slot)?.Name ?? "" : "";
    }

    /// <summary>
    /// The original's boolean text.
    /// </summary>
    static string Flag(bool value) => value ? "true" : "false";
}
