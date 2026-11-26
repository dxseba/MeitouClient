using Meitou.Modding;

namespace Meitou.Nitoryu;

/// <summary>Support log lines with a cap per topic, so a report shows what happened without flooding the log (display only).</summary>
static class Diag
{
    static readonly Dictionary<string, int> Counts = [];
    static readonly Lock Gate = new();

    public static ModContext? Context { get; set; }

    /// <summary>Logs <paramref name="text"/> while <paramref name="topic"/> has logged fewer than <paramref name="cap"/> lines.</summary>
    public static void Log(string topic, int cap, Func<string> text)
    {
        lock (Gate)
        {
            int n = Counts.GetValueOrDefault(topic);
            if (n >= cap) return;

            Counts[topic] = n + 1;
        }

        Context?.Log($"[{topic}] {text()}");
    }
}
