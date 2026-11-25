using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.QuestsContracts;

/// <summary>
/// The open contracts: their clock, their locked targets, the items the party carries for them and the job variables.
/// </summary>
sealed partial class LedgerSystem
{
    Slot? FindSlot(int town, int kind) => _state.Slots.FirstOrDefault(s => s.Town == town && s.Kind == kind);

    /// <summary>A barman's job fails after its days (3 unless the line says); a job left READY and unreported costs more.</summary>
    void Expire(World world)
    {
        foreach (var s in _state.Slots.Where(s => s.Kind <= 7 && _day - s.AcceptDay >= (s.Days > 0 ? s.Days : FailDays)).ToList())
        {
            bool kept = s.State >= 2;
            int rep = kept ? -RepKept : -RepFail;
            _state.Close(s, _day, "Failed", kept ? "nothing - done, never reported" : "nothing - the clock ran out");
            BumpRep(s.Giver, rep, kept ? "kept what was owed" : "failed");
            _state.Event(_day, $"Job {s.Kind} ({s.Type}) for {s.Giver.Npc} in {s.Giver.Town} FAILED");
            _state.FailPending = 1;
        }
    }

    /// <summary>A locked target that is down or dead makes its job READY (a debt counts as collected; the player loots the money or not).</summary>
    void Watch(World world)
    {
        foreach (var s in _state.Slots)
        {
            if (s.State == 2 || s.Target == 0 || s.Kind is 8 or >= 10) continue;

            var id = world.LastingIds.Find(new LastingId(s.Target));
            if (!world.Characters.TryResolveNext(id, out int i) || world.Characters.Cold(i)?.Medical is not { Incapacitated: true }) continue;

            s.State = 2;
            _state.Event(_day, $"{s.TargetName} is down: tell {s.Giver.Npc}");
        }
    }

    /// <summary>The NPC spoken to against the locked targets (by lasting id; a target never locked is matched by name).</summary>
    void MatchTarget(World world, CharacterId npc, Who w)
    {
        _targetJob = 0;
        ulong lasting = world.LastingIds.GetOrGive(npc).Value;
        int code = TownCode(w.Town);
        foreach (var s in _state.Slots)
        {
            if (s.Targets.Count == 0 || s.State == 2 || s.Kind < 10 && s.Town != code) continue;

            bool mine = s.Target != 0 ? s.Target == lasting : s.TargetName.Length > 0 && s.TargetName == w.Npc;
            if (!mine) continue;

            s.Target = lasting;
            _targetJob = s.Kind;
            return;
        }
    }

    void LockOn(World world, Slot s, CharacterId id, int kind, string name)
    {
        s.Target = world.LastingIds.GetOrGive(id).Value;
        s.TargetKind = kind;
        s.TargetName = name;
        _state.Event(_day, $"The {s.Type} target is {name}");
    }

    /// <summary>The per-kind state and carried counts, the offers and the gates; published for the dialogue.</summary>
    void WriteJobVars(World world)
    {
        Set("day", Math.Max(0, _day));
        JobStates(world);
        Offers();
        GuildVars();
        RideVars(world);
        Set("ctx_is_target", _state.Slots.Any(s => s.Targets.Count > 0 && s.State != 2) ? 1 : 0);
        Set("target_job", _targetJob);
        Publish();
    }

    /// <summary>jobK_state and jobK_have: the guild's jobs are one of a kind anywhere, the others are the context town's.</summary>
    void JobStates(World world)
    {
        for (int k = 1; k <= Kinds; k++)
        {
            var s = SlotOf(k, _ctxTown);
            int have = s is { Want.Count: > 0 } && !_ctxPlayer.IsNone ? CountAny(world, _ctxPlayer, s.Want) : 0;
            if (s is not null && k == 13) have = s.FoundIndex > 0 ? 1 : 0;

            Set($"job{k}_state", s?.State ?? 0);
            Set($"job{k}_have", have);
        }
    }

    void Offers()
    {
        Set("offer", _offer);
        Set("offer_special", _offerSpecial);
        Set("target_kind", _targetKind);
        Set("route_kind", _routeKind);
        Set("fail_pending", _state.FailPending);
        Set("bounty_offer", _bountyOffer);
        Set("bounty_offer2", _bountyOffer2);
        Set("promo_bar_due", Due("bar", _ctxRep + RepDone >= SpecialRep));
        Set("promo_desk_due", Due("desk", _ctxRep + RepDone >= PromoDeskRep));
        Set("promo_thief_due", Due("thief", LedgerState.Get(_state.Done, "heist:done", 0) >= PromoThiefHeists));
        Set("promo_caravan_due", Due("caravan", LedgerState.Get(_state.Done, "caravan:done", 0) >= PromoCaravanRides));
    }

    int Due(string promo, bool reached) => LedgerState.Get(_state.Done, "promo:" + promo, 0) == 0 && reached ? 1 : 0;

    void GuildVars()
    {
        Set("heist_offer", _heistOffer);
        Set("heist_route", RideSlot(12)?.Route ?? 0);
        Set("heist_done", LedgerState.Get(_state.Done, "heist:done", 0));
        Set("heist_intro", LedgerState.Get(_state.Done, "heist:intro", 0));
        Set("heist_trick", LedgerState.Get(_state.Done, "heist:trick", 0));
        Set("scout_here", _scoutHere);
        Set("scout_found", _scoutFound);
        Set("chest_job", RideSlot(12) is { Type: "chest" } ? 1 : 0);
        Set("rescue_offer", _rescueOffer);
        Set("rescue_route", RideSlot(13)?.Route ?? 0);
        Set("rescue_state", RideSlot(13)?.State ?? 0);
        Set("rescue_ask", _rescueAsk);
        Set("rescue_here", _rescueHere);
        Set("rescue_found", _rescueFound);
        Set("mark_offer", MarkOffer());
    }

    void RideVars(World world)
    {
        var e = RideSlot(8);
        Set("escort_open", e is not null || _state.EscortRestDay > _day ? 1 : 0);
        Set("escort_state", e?.State ?? 0);
        Set("escort_betray", e is { Betray: true } ? 1 : 0);
        Set("escort_route", _escortRoute);
        Set("escort_gift", e?.Gift ?? 0);

        var c = RideSlot(9);
        Set("caravan_open", c is not null || _state.CaravanRestHours > world.GameHours ? 1 : 0);
        Set("caravan_state", c?.State ?? 0);
        Set("caravan_route", c?.Route ?? _caravanRoute);
    }

    #region The party's items

    /// <summary>The player's characters within 6000 of <paramref name="who"/> (the talker first).</summary>
    IEnumerable<Inventory> Party(World world, CharacterId who)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(who, out int w)) yield break;

        if (table.Cold(w)?.Inventory is { } own) yield return own;

        var at = table.Next[w].Position;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (i != w && table.Cold(i) is { IsPlayer: true, Inventory: { } inv, Medical.Dead: false }
                && Vector3.Distance(table.Next[i].Position, at) <= PartyRadius)
                yield return inv;
        }
    }

    int CountAny(World world, CharacterId who, IReadOnlyList<string> want) =>
        Party(world, who).Sum(inv => inv.All().Where(i => want.Contains(i.Record)).Sum(i => i.Quantity));

    /// <summary>Takes <paramref name="n"/> of the wanted items, whichever the party carries, the talker's first.</summary>
    int TakeAny(World world, CharacterId who, IReadOnlyList<string> want, int n)
    {
        int taken = 0;
        foreach (var inv in Party(world, who).ToList())
        {
            while (taken < n && inv.All().FirstOrDefault(i => want.Contains(i.Record)) is { } item && inv.RemoveOne(item)) taken++;
        }

        return taken;
    }

    /// <summary>Every item of the record off the character (the debtor's money when he pays through the dialogue).</summary>
    static void StripAll(World world, CharacterId who, string record)
    {
        if (!world.Characters.TryResolveNext(who, out int i) || world.Characters.Cold(i)?.Inventory is not { } inv) return;

        while (inv.All().FirstOrDefault(it => it.Record == record) is { } item && inv.Remove(item)) { }
    }

    #endregion
}
