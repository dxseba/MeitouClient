using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.BestMinimap;

/// <summary>
/// The attack alert (the original's <c>AttackAlert</c>): each landed hit on a character of the player's faction (the combat hit feed, display only)
/// keeps a ring where it happened for <c>AlertSeconds</c> after the last one, and, with <c>AlertMessage</c>, "Under attack: " and the names go to
/// the game's message line at most once in 3 seconds (the original's hint).
/// </summary>
static class Alerts
{
    const double MessageGap = 3;

    sealed record Alert(Vector2 At, double Time);

    static readonly Dictionary<CharacterId, Alert> Active = [];
    static readonly HashSet<CharacterId> Unsaid = [];
    static double s_lastMessage = -MessageGap;
    static bool s_failed;

    public static Action<string>? Log;

    /// <summary>The rings to draw now (world X, Z) and how long each has been on.</summary>
    public static IEnumerable<(Vector2 At, double Age)> Current
    {
        get
        {
            double now = Minimap.Now, keep = Minimap.Settings.AlertSeconds;
            foreach (var (id, a) in Active.ToArray())
            {
                if (now - a.Time > keep) Active.Remove(id);
                else yield return (a.At, now - a.Time);
            }
        }
    }

    public static bool Any => Active.Count > 0;

    public static void Hits(IReadOnlyList<CombatHit> hits)
    {
        if (s_failed || !Minimap.Settings.AttackAlert || Minimap.Snapshot is not { } snap) return;

        try
        {
            foreach (var hit in hits)
            {
                if (!IsOwn(snap, hit.Victim)) continue;

                Active[hit.Victim] = new(new(hit.VictimPosition.X, hit.VictimPosition.Z), Minimap.Now);
                Unsaid.Add(hit.Victim);
            }
        }
        catch (Exception e)
        {
            s_failed = true;
            Log?.Invoke($"attack alert failed, left off for the session: {e.Message}");
        }
    }

    /// <summary>The message line, at most once in 3 seconds.</summary>
    public static void Say(ModFrame frame, Func<string, string> tr)
    {
        if (Unsaid.Count == 0 || Minimap.Now - s_lastMessage < MessageGap) return;

        if (Minimap.Settings.AlertMessage && Minimap.Settings.AttackAlert && Minimap.Snapshot is { } snap)
        {
            var names = snap.Characters.Where(c => Unsaid.Contains(c.Id)).Select(c => c.Name).Where(n => n.Length > 0).ToList();
            if (names.Count > 0) frame.Notify(tr("Under attack: ") + string.Join(", ", names));
        }

        Unsaid.Clear();
        s_lastMessage = Minimap.Now;
    }

    static bool IsOwn(WorldSnapshot snap, CharacterId id)
    {
        foreach (var c in snap.Characters)
        {
            if (c.Id == id) return c.IsPlayer;
        }

        return false;
    }
}
