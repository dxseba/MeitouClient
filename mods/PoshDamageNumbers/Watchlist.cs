using System.Diagnostics;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.PoshDamageNumbers;

/// <summary>
/// The original's watch list (<b>Verified</b>, <c>damage_numbers.lua</c> <c>rescan</c>, every 0.5 s): of the characters that concern you (yours,
/// those fighting yours, those within Fight range of yours, 15% more for one already watched) and are on screen or were hit in the last 6 s, a
/// character hit while on screen comes first, then the rest on screen by their distance from the screen centre, then those out of view; only the
/// first 4 x <c>MaxWatched</c> are read, so a hit on anyone after them shows nothing. Host thread only.
/// </summary>
static class Watchlist
{
    const float ScanSeconds = 0.5f, KeepAfterHitSeconds = 6, UnitsPerMetre = 10, WatchedReach = 1.15f, ScreenMargin = 0.2f;
    const int SlowFactor = 3;

    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly Dictionary<CharacterId, double> HitUntil = [];
    static HashSet<CharacterId> s_watched = [];
    static double s_nextScan;
    static bool s_scanned;

    /// <summary>Whether a hit, a heal or a word on <paramref name="victim"/> is read; it also keeps the victim watched for a while.</summary>
    public static bool Reads(CharacterId victim)
    {
        bool read = !s_scanned || s_watched.Contains(victim);
        if (read) HitUntil[victim] = Clock.Elapsed.TotalSeconds + KeepAfterHitSeconds;
        return read;
    }

    /// <summary>Rebuilds the list every half second of real time.</summary>
    public static void Scan(ModFrame frame, DamageSettings settings)
    {
        double now = Clock.Elapsed.TotalSeconds;
        if (now < s_nextScan) return;

        s_nextScan = now + ScanSeconds;
        foreach (var gone in HitUntil.Where(h => h.Value < now).Select(h => h.Key).ToList()) HitUntil.Remove(gone);

        var ranked = Candidates(frame, settings, now).OrderBy(c => c.Rank).Take(settings.MaxWatched * (1 + SlowFactor));
        s_watched = ranked.Select(c => c.Id).ToHashSet();
        s_scanned = true;
    }

    static IEnumerable<(CharacterId Id, float Rank)> Candidates(ModFrame frame, DamageSettings settings, double now)
    {
        var characters = frame.Snapshot.Characters;
        var squad = characters.Where(c => c.IsPlayer).Select(c => new Vector2(c.Position.X, c.Position.Z)).ToList();
        var engaged = HitWatch.Engaged;
        var size = frame.ScreenSize;
        var centre = size / 2;
        foreach (var c in characters)
        {
            bool hit = HitUntil.GetValueOrDefault(c.Id) > now;
            var at = frame.Project(c.Position);
            bool seen = at is { } p && OnScreen(p, size);
            if (!(seen || hit) || !Concerns(c, squad, engaged, settings)) continue;

            float rank = !seen ? float.MaxValue : hit ? -1 : Vector2.DistanceSquared(at!.Value, centre);
            yield return (c.Id, rank);
        }
    }

    static bool OnScreen(Vector2 p, Vector2 size)
    {
        float margin = ScreenMargin * size.X;
        return p.X >= -margin && p.X <= size.X + margin && p.Y >= -margin && p.Y <= size.Y + margin;
    }

    static bool Concerns(CharacterSnapshot c, List<Vector2> squad, IReadOnlySet<CharacterId> engaged, DamageSettings settings)
    {
        if (c.IsPlayer || engaged.Contains(c.Id)) return true;

        float range = settings.FightRange * UnitsPerMetre * (s_watched.Contains(c.Id) ? WatchedReach : 1);
        var here = new Vector2(c.Position.X, c.Position.Z);
        return squad.Any(p => Vector2.DistanceSquared(p, here) <= range * range);
    }
}
