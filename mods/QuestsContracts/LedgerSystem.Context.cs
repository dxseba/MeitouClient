using System.Numerics;
using Meitou.Simulation;

namespace Meitou.QuestsContracts;

/// <summary>
/// The talk context (the original's <c>FillContext</c> / <c>WriteContext</c>): at a player conversation's start, the NPC's reputation, town
/// window and the offers drawn for this visit.
/// </summary>
sealed partial class LedgerSystem
{
    Who? _ctx;
    CharacterId _ctxNpc, _ctxPlayer, _offerTarget;
    int _ctxTown, _ctxRep, _offer, _offerSpecial, _targetKind, _routeKind, _targetJob, _bountyOffer, _bountyOffer2, _heistOffer, _rescueOffer;
    string _offerTargetName = "";
    readonly bool[] _declined = new bool[Kinds + 1];

    /// <summary>
    /// A player conversation opens between <paramref name="npc"/> and the player's <paramref name="player"/>: a pass lasts one visit, overdue
    /// jobs fail, the NPC is matched against the locked targets and the context is written.
    /// </summary>
    internal void ConversationStarts(World world, CharacterId npc, CharacterId player)
    {
        if (Describe(world, npc) is not { } who) return;

        _ctx = who;
        _ctxNpc = npc;
        _ctxPlayer = player;
        Array.Clear(_declined);
        Expire(world);
        MatchTarget(world, npc, who);
        WriteContext(world, who, npc);
    }

    void WriteContext(World world, Who w, CharacterId speaker)
    {
        _ctxTown = TownCode(w.Town);
        _ctxRep = RepOf(w);
        _routeKind = 0;
        _offer = DrawOffer(world, w, special: false, speaker);
        _offerTarget = CharacterId.None;
        _offerTargetName = "";
        _targetKind = 0;
        bool specialOpen = _ctxRep >= SpecialRep;
        _offerSpecial = specialOpen ? DrawOffer(world, w, special: true, speaker) : 0;
        if (_offerTarget.IsNone && _targetJob != 0 && FindSlot(_ctxTown, _targetJob) is { } s) _targetKind = s.TargetKind;

        _bountyOffer = DrawBounty(10);
        _bountyOffer2 = DrawBounty(11);
        _heistOffer = DrawHeist();
        _rescueOffer = DrawRescue();
        _escortRoute = DrawRide(8);
        _caravanRoute = DrawRide(9);
        WriteContextVars(world, w, speaker, specialOpen);
        WriteJobVars(world);
    }

    void WriteContextVars(World world, Who w, CharacterId speaker, bool specialOpen)
    {
        string record = world.Characters.TryResolveNext(speaker, out int s) ? world.Characters.Cold(s)?.RecordId ?? "" : "";
        int giverKind = _tables.Givers.TryGetValue(record, out int k) ? k : -1;
        Set("ctx_giver", giverKind == 0 ? 1 : 0);
        Set("ctx_law", giverKind == 10 ? 1 : 0);
        Set("ctx_rep", _ctxRep);
        Set("ctx_days_since_job", DaysSinceJob(w));
        Set("ctx_town", _ctxTown);
        Set("ctx_town_jobs", TownJobs(w));
        Set("special_open", specialOpen ? 1 : 0);
    }

    #region Reputation and windows

    int RepOf(Who w)
    {
        int best = 0;
        foreach (string key in Keys(w)) best = Math.Max(best, LedgerState.Get(_state.Rep, key, -1));
        return best;
    }

    static IEnumerable<string> Keys(Who w)
    {
        if (w.Npc.Length > 0) yield return "npc:" + w.Npc;
        if (w.Town.Length > 0) yield return "town:" + w.Town;
        if (w.Faction.Length > 0) yield return "faction:" + w.Faction;
    }

    /// <summary>The original's weights: the NPC (and squad) 100 %, the town 60 %, the faction 25 %, each clamped to 0..100.</summary>
    void BumpRep(Who w, int delta, string why)
    {
        Bump("npc:" + w.Npc, w.Npc.Length > 0, delta);
        Bump("town:" + w.Town, w.Town.Length > 0, delta * 60 / 100);
        Bump("faction:" + w.Faction, w.Faction.Length > 0, delta * 25 / 100);
        _state.Event(_day, $"{w.Npc} of {w.Town} {delta:+0;-0} ({why})");
    }

    void Bump(string key, bool on, int delta)
    {
        if (on) _state.Rep[key] = Math.Clamp(LedgerState.Get(_state.Rep, key, 0) + delta, 0, 100);
    }

    int TownJobs(Who w)
    {
        int last = LedgerState.Get(_state.LastJob, w.Town, -1);
        return w.Town.Length == 0 || last < 0 || _day - last >= WindowDays ? 0 : LedgerState.Get(_state.Jobs, w.Town, 0);
    }

    int DaysSinceJob(Who w)
    {
        int last = LedgerState.Get(_state.LastJob, w.Town, -1);
        return w.Town.Length == 0 || last < 0 ? 999 : _day - last;
    }

    void MarkJob(Who w)
    {
        if (w.Town.Length == 0) return;

        int jobs = TownJobs(w) + 1;
        _state.LastJob[w.Town] = _day;
        _state.Jobs[w.Town] = jobs;
    }

    bool DoneThisWindow(Who w, int kind) =>
        LedgerState.Get(_state.Done, $"{w.Town}:{kind}", -1) is int d && d >= 0 && _day - d < WindowDays;

    int SpecialDaysLeft(Who w)
    {
        int d = LedgerState.Get(_state.Special, w.Town, -1);
        return d < 0 ? 0 : Math.Max(0, SpecialDays - (_day - d));
    }

    #endregion

    #region The draws

    Signal? AcceptSignal(int kind, int town)
    {
        Signal? any = null;
        foreach (var s in _tables.Signals)
        {
            if (!s.Is("accept") || s.Kind != kind) continue;
            if (s.Town == town) return s;

            any ??= s.Town == 0 ? s : null;
        }

        return any;
    }

    /// <summary>
    /// A job of the town's pool (deliveries, or the specials), not open there, not delivered this window and not passed this visit; a package
    /// draws one of the town's routes, a target job needs someone to point at.
    /// </summary>
    int DrawOffer(World world, Who w, bool special, CharacterId barman)
    {
        if (special ? SpecialDaysLeft(w) > 0 || _state.Slots.Any(s => s.Town == _ctxTown && s.Targets.Count > 0) : TownJobs(w) >= JobsPerWindow)
            return 0;

        var pool = new List<int>();
        for (int k = 1; k <= Kinds; k++)
        {
            if (AcceptSignal(k, _ctxTown) is not { } a || a.Special != special || k == 8) continue;
            if (FindSlot(_ctxTown, k) is null && !_declined[k] && !DoneThisWindow(w, k)) pool.Add(k);
        }

        while (pool.Count > 0)
        {
            int i = Roll(pool.Count), k = pool[i];
            if (Fits(world, AcceptSignal(k, _ctxTown)!, k, barman)) return k;

            pool.RemoveAt(i);
        }

        return 0;
    }

    bool Fits(World world, Signal a, int k, CharacterId barman)
    {
        if (a.Route != 0)
        {
            var routes = _tables.Signals.Where(s => s.Is("accept") && s.Kind == k && s.Town == _ctxTown && s.Route != 0)
                .Select(s => s.Route).ToList();
            if (routes.Count == 0) return false;

            _routeKind = routes[Roll(routes.Count)];
            return true;
        }

        if (a.Targets.Count == 0) return true;

        if (PickCandidate(world, barman, a) is not var (id, kind, name)) return false;

        (_offerTarget, _targetKind, _offerTargetName) = (id, kind, name);
        return true;
    }

    /// <summary>A standing character in the barman's town within 2500 of him whose record is one of the job's targets, not already locked.</summary>
    (CharacterId Id, int Kind, string Name)? PickCandidate(World world, CharacterId barman, Signal a)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(barman, out int b)) return null;

        var at = table.Next[b].Position;
        int town = _population.TownAt(at);
        var found = new List<(CharacterId, int, string)>();
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (Candidate(world, i, b, at, town, a) is { } c) found.Add(c);
        }

        return found.Count == 0 ? null : found[Roll(found.Count)];
    }

    (CharacterId, int, string)? Candidate(World world, int i, int barman, Vector3 at, int town, Signal a)
    {
        var table = world.Characters;
        if (i == barman || table.Cold(i) is not { IsPlayer: false } c || c.Medical is { Incapacitated: true }) return null;

        int kind = a.Targets.ToList().IndexOf(c.RecordId) + 1;
        var p = table.Next[i].Position;
        if (kind == 0 || Vector3.Distance(p, at) > PickRadius || _population.TownAt(p) != town) return null;

        var id = table.IdOf(i);
        ulong lasting = world.LastingIds.GetOrGive(id).Value;
        return _state.Slots.Any(s => s.Target == lasting) ? null : (id, kind, c.Name);
    }

    /// <summary>The law's draw: three cutthroats (kind 10) per desk and window, one big name (11); posters already read are left out.</summary>
    int DrawBounty(int kind)
    {
        string key = $"law:{_ctxTown}";
        if (kind == 10 && LawJobs(key) >= 3) return 0;
        if (kind == 11 && LedgerState.Get(_state.Special, key, -1) is int d && d >= 0 && _day - d < WindowDays) return 0;

        var routes = _tables.Signals.Where(s => s.Is("lead") && s.Kind == kind && s.Town == _ctxTown && s.Route != 0 && !PosterRead(s))
            .Select(s => s.Route).ToList();
        if (routes.Count == 0) return 0;

        uint h = (uint)Math.Max(0, _day) * 2654435761u ^ (uint)_ctxTown * 40503u ^ (uint)kind * 97u;
        return routes[(int)(h % (uint)routes.Count)];
    }

    int LawJobs(string key)
    {
        int last = LedgerState.Get(_state.LastJob, key, -1);
        return last < 0 || _day - last >= WindowDays ? 0 : LedgerState.Get(_state.Jobs, key, 0);
    }

    void LawMark(int kind)
    {
        string key = $"law:{_ctxTown}";
        if (kind == 11)
        {
            _state.Special[key] = _day;
            return;
        }

        int jobs = LawJobs(key);
        _state.LastJob[key] = _day;
        _state.Jobs[key] = jobs + 1;
    }

    bool PosterRead(Signal s) => PosterOf(s) is { } b && _state.Posters.TryGetValue(b.Poster, out var t) && (t.ReadDay >= 0 || t.ClaimedDay >= 0);

    Bounty? PosterOf(Signal s) =>
        _tables.Bounties.FirstOrDefault(b => s.Poster.Length > 0 && b.Poster == s.Poster || s.Targets.Count > 0 && b.Targets.Contains(s.Targets[0]));

    #endregion
}
