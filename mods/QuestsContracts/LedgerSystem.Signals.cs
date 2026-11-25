using Meitou.Simulation;

namespace Meitou.QuestsContracts;

/// <summary>
/// The signal lines (the original's <c>OnSignal</c>): a line of <c>ledger-signals.txt</c> was said; the Ledger takes, closes or changes a job,
/// then writes the context again so the next line sees the new numbers.
/// </summary>
sealed partial class LedgerSystem
{
    internal void Signalled(World world, int index, CharacterId speaker, CharacterId target)
    {
        if ((uint)index >= (uint)_tables.Signals.Count) return;

        var s = _tables.Signals[index];
        var table = world.Characters;
        var npc = table.TryResolveNext(speaker, out int sp) && table.Cold(sp) is { IsPlayer: false } ? speaker : _ctxNpc;
        if (Describe(world, npc) is not { } w) return;

        if (table.TryResolveNext(target, out int t) && table.Cold(t) is { IsPlayer: true }) _ctxPlayer = target;
        Route(world, s, w, npc);
        WriteContext(world, w, npc);
    }

    void Route(World world, Signal s, Who w, CharacterId npc)
    {
        int k = s.Kind, code = TownCode(w.Town);
        bool takes = s.Is("accept") || s.Is("heist") || s.Is("rescue") || s.Is("mark");
        if (takes && k is >= 1 and <= Kinds) Accept(world, s, w, npc, code);
        else if (k is 8 or 9 && (s.Is("complete") || s.Is("betrayed") || s.Is("fail_"))) RideEnds(world, s);
        else if (!Other(world, s, w, npc, code)) Bookkeeping(world, s, w, npc, code);
    }

    /// <summary>The guild's and the law's signals; false when <paramref name="s"/> is none of them.</summary>
    bool Other(World world, Signal s, Who w, CharacterId npc, int code)
    {
        if (s.Is("bountyrep")) BumpRep(w, RepDone, "a bounty cashed");
        else if (s.Is("promo_")) Promote(s.What[6..], w);
        else if (s.Is("trick")) _state.Done["heist:trick"] = 1;
        else if (s.Is("intro")) _state.Done["heist:intro"] = 1;
        else if (s.Is("follow")) Follow(world, npc, w);
        else if (s.Is("freed")) Freed(world);
        else if (s.Is("scouted")) Scouted(world);
        else if (s.Is("lead")) Lead(s, w, code);
        else if (s.Is("heard") && s.Targets.Count > 0) Heard(s);
        else if (s.Is("swap")) Swap(world, s, w, code);
        else if (s.Is("escaped")) Escaped();
        else return false;

        return true;
    }

    void Bookkeeping(World world, Signal s, Who w, CharacterId npc, int code)
    {
        int k = s.Kind;
        if (s.Is("decline") && k is >= 1 and <= Kinds) _declined[k] = true;
        else if (s.Is("complete") && k is >= 1 and <= Kinds) Complete(world, s, w, code);
        else if (s.Is("cancel") && k is >= 1 and <= Kinds) Cancel(world, s, w, code);
        else if (s.Is("debt_paid")) DebtPaid(world, s, w, npc, code);
        else if (s.Is("thug_talked") && FindSlot(code, k) is { State: 1 } thug) thug.State = 3;
        else if (s.What == "fail_ack") _state.FailPending = 0;
        else if (s.Is("rep+") && int.TryParse(s.What[4..], out int up)) BumpRep(w, up, s.What);
        else if (s.Is("rep-") && int.TryParse(s.What[4..], out int down)) BumpRep(w, -down, s.What);
    }

    /// <summary>
    /// The slot of kind <paramref name="k"/>: the guild's jobs and the rides are one of a kind anywhere, the others live in a town.
    /// </summary>
    Slot? SlotOf(int k, int town) => k is 8 or 9 or 12 or 13 or 14 ? RideSlot(k) : FindSlot(town, k);

    Slot? RideSlot(int k) => _state.Slots.FirstOrDefault(s => s.Kind == k);

    #region Taking a job

    void Accept(World world, Signal s, Who w, CharacterId npc, int code)
    {
        if (s.Kind is 8 or 9 && RideSlot(s.Kind) is not null) return;

        var slot = NewSlot(s, w, code);
        _state.Slots.Add(slot);
        Clocks(s, w, code);
        Shape(world, slot, s, w, npc);

        _offerTarget = CharacterId.None;
        string bound = s.Dest != 0 ? $", bound for {s.DestName}" : "";
        _state.Event(_day, $"Took {slot.Type} work (job {s.Kind}) from {w.Npc} in {w.Town}{bound}");
    }

    Slot NewSlot(Signal s, Who w, int code) => new()
    {
        Town = code, Kind = s.Kind, Type = s.Type.Length > 0 ? s.Type : s.Kind == 13 ? "rescue" : "delivery", Want = [.. s.Want],
        WantCount = s.Count, Pay = s.Pay, AcceptDay = _day, Days = s.Days, Dest = s.Dest, DestName = s.DestName,
        Item = s.Kind < 10 ? s.House : "", Giver = w, Targets = [.. s.Targets], Route = s.Route, Stash = s.Stash, NearTown = s.Camp,
    };

    /// <summary>Each kind's own clock: the special's, the guild's per town, the deliveries' window.</summary>
    void Clocks(Signal s, Who w, int code)
    {
        if (s.Special) _state.Special[w.Town] = _day;
        else if (s.Kind is 12 or 13 or 14) _state.Done[$"{(s.Kind == 12 ? "heist" : s.Kind == 13 ? "rescue" : "mark")}:{code}"] = _day;
        else if (s.Kind < 10) MarkJob(w);
    }

    /// <summary>What a kind sets up at the accept: a target, the rider, the mark's chest.</summary>
    void Shape(World world, Slot slot, Signal s, Who w, CharacterId npc)
    {
        switch (s.Kind)
        {
            case 8 or 9: TakeRide(world, slot, s, npc, w); break;
            case 12: slot.Found = s.House; break;
            case 13: break;
            case 14: Mark(world, slot, w); break;
            case >= 10: LawMark(s.Kind); break;
            default: if (s.Targets.Count > 0) LockTarget(world, slot, s, npc); break;
        }
    }

    void LockTarget(World world, Slot slot, Signal s, CharacterId npc)
    {
        if (!_offerTarget.IsNone)
        {
            LockOn(world, slot, _offerTarget, _targetKind, _offerTargetName);
            return;
        }

        if (PickCandidate(world, npc, s) is var (id, kind, name)) LockOn(world, slot, id, kind, name);
    }

    #endregion

    #region Closing a job

    void Complete(World world, Signal s, Who w, int code)
    {
        var slot = s.Kind is 12 or 13 or 14 ? RideSlot(s.Kind) : FindSlot(s.Origin != 0 ? s.Origin : code, s.Kind);
        if (s.Want.Count > 0 && !_ctxPlayer.IsNone) TakeAny(world, _ctxPlayer, s.Want, s.Count);

        if (slot is not null)
        {
            if (s.Kind is 12 or 13) _state.Done["heist:lastdest"] = slot.Dest;
            _state.Close(slot, _day, s.Half ? "Half pay" : "Paid", s.Half ? "half pay" : $"{slot.Pay} cats");
        }

        _state.Done[$"{w.Town}:{s.Kind}"] = _day;
        if (s.Promo.Length > 0) Promote(s.Promo, w);
        if (s.Kind == 12) _state.Done["heist:done"] = LedgerState.Get(_state.Done, "heist:done", 0) + 1;

        BumpRep(w, s.Half ? RepDone / 2 : RepDone, s.Half ? "half" : s.What);
        _state.Event(_day, $"Handed in job {s.Kind} ({(s.Type.Length > 0 ? s.Type : "delivery")}) to {w.Npc} in {w.Town}");
    }

    void Cancel(World world, Signal s, Who w, int code)
    {
        if (SlotOf(s.Kind, code) is { } slot)
        {
            if (slot.Kind is 8 or 9 or 13) CutLoose(world, slot);
            _state.Close(slot, _day, "Cancelled", "nothing");
        }

        BumpRep(w, -RepFail, s.What);
        _state.Event(_day, $"Gave up job {s.Kind} at {w.Npc} in {w.Town}");
    }

    void DebtPaid(World world, Signal s, Who w, CharacterId npc, int code)
    {
        if (FindSlot(code, s.Kind) is not { State: 1 } slot) return;

        if (slot.Target == 0) LockOn(world, slot, npc, slot.TargetKind, w.Npc);
        if (slot.Want.Count > 0) StripAll(world, npc, slot.Want[0]);

        slot.State = 2;
    }

    void Promote(string name, Who w)
    {
        string key = "promo:" + name;
        if (LedgerState.Get(_state.Done, key, 0) != 0) return;

        _state.Done[key] = 1;
        _state.Event(_day, $"A word from {w.Npc}: new work opens");
    }

    #endregion

    #region The law's posters

    PosterTrack Track(string poster)
    {
        if (!_state.Posters.TryGetValue(poster, out var t)) _state.Posters[poster] = t = new PosterTrack();
        return t;
    }

    /// <summary>The desk hands a poster over (its line's <c>give item</c> gives the poster): the window counts it and the poster is read.</summary>
    void Lead(Signal s, Who w, int code)
    {
        _ctxTown = code;
        LawMark(s.Kind);
        if (PosterOf(s) is not { } b || Track(b.Poster) is not { ReadDay: < 0 } t) return;

        (t.ReadDay, t.ReadTown) = (_day, w.Town);
        string by = b.Faction.Length > 0 ? b.Faction : "the law";
        _state.Event(_day, $"{w.Npc} handed you a poster: {b.Name}, {b.Reward} cats, wanted by {by}");
    }

    void Heard(Signal s)
    {
        foreach (var b in _tables.Bounties.Where(b => b.Targets.Contains(s.Targets[0])))
        {
            if (Track(b.Poster) is { ReadDay: < 0 } t) t.ReadDay = _day;
        }
    }

    #endregion
}
