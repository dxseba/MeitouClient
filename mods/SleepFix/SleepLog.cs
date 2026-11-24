namespace Meitou.SleepFix;

/// <summary>
/// The original's diagnostic log (<c>debug=1</c> wrote <c>SleepFix.log</c>), here into the mod log: the settings once, then with <c>debug</c>
/// each bed order, each release and its reason, and with <c>probeBeds</c> / <c>dumpParts</c> each bed search and the part table of a character
/// sent. Each category stops after <c>logLimit</c> lines, with one line saying so, as the original's per-category cap; a line equal to the category's last is
/// counted, not repeated, and the count is written before the next different one (the original's repeat folding). Off, it costs one test.
/// </summary>
sealed class SleepLog(SleepSettings settings, Action<string> log)
{
    readonly Dictionary<string, (int Lines, string Last, int Repeats)> _categories = [];

    /// <summary>Whether <paramref name="category"/> is written now (<c>debug</c> and, for a probe, its own switch).</summary>
    public bool On(string category) => settings.Debug && category switch
    {
        "beds" => settings.ProbeBeds,
        "parts" => settings.DumpParts,
        _ => true,
    };

    /// <summary>Writes one line of <paramref name="category"/> while it is on and under the cap.</summary>
    public void Write(string category, Func<string> line)
    {
        if (!On(category)) return;

        var (lines, last, repeats) = _categories.GetValueOrDefault(category, (0, "", 0));
        if (lines > settings.LogLimit) return;

        string text = line();
        if (text == last)
        {
            _categories[category] = (lines, last, repeats + 1);
            return;
        }

        if (repeats > 0) log($"SleepFix [{category}] (last line {repeats} more times)");
        lines++;
        log(lines > settings.LogLimit ? $"SleepFix [{category}] log limit reached, raise logLimit" : $"SleepFix [{category}] {text}");
        _categories[category] = (lines, text, 0);
    }
}
