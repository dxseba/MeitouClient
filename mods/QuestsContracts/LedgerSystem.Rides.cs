using System.Numerics;
using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Dialogue;

namespace Meitou.QuestsContracts;

/// <summary>
/// The rides (the original's escort 8, caravan 9 and the rescued thief 13): the one who asked is locked and follows on the contract his accept
/// line hands his squad; in the destination town (the rescue: out of the jail town) the ride is ARRIVED and he is made to start his end talk with
/// the player (event 30, contract job ended), which pays or fails it; the closer signals end his contract.
/// </summary>
sealed partial class LedgerSystem
{
    const int EscortBetrayPct = 30, EscortRestDays = 3, EscortNearest = 3, CaravanRestHours = 2, CaravanMemory = 8, RescueAskPct = 50;
    const int TalkTries = 40, HardDays = 2;
    const float TalkEverySeconds = 3;

    static readonly MethodInfo? StartMethod = typeof(DialogueSystem).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(World), typeof(CharacterId), typeof(CharacterId), typeof(int), typeof(bool), typeof(bool)]);
    static readonly PropertyInfo? InConversation =
        typeof(DialogueSystem).GetProperty("InConversation", BindingFlags.Instance | BindingFlags.NonPublic);

    int _escortRoute, _caravanRoute, _rescueAsk;

    #region Taking and ending

    /// <summary>
    /// Accept of a ride: he is locked, the betrayal (30 % on the beg) and the gift are rolled; a beg that sides with a debtor fails the debt.
    /// </summary>
    void TakeRide(World world, Slot slot, Signal s, CharacterId npc, Who w)
    {
        LockOn(world, slot, npc, 0, w.Npc);
        slot.Betray = s.Kind == 8 && s.Betray && Roll(100) < EscortBetrayPct;
        if (s.Kind == 8 && s.Gifts.Count > 0) RollGift(slot, s);

        if (s.Kind == 8 && s.Betray && FindSlot(TownCode(w.Town), 4) is { } debt && (debt.Target == slot.Target || debt.TargetName == w.Npc))
        {
            _state.Close(debt, _day, "Failed", "nothing - you took his side");
            BumpRep(debt.Giver, -RepFail, "betrayed the barman");
            _state.FailPending = 1;
        }
    }

    void RollGift(Slot slot, Signal s)
    {
        int roll = Roll(100), sum = 0;
        slot.Gift = s.Gifts.Count;
        for (int i = 0; i < s.Gifts.Count; i++)
        {
            sum += s.Gifts[i];
            if (roll >= sum) continue;

            slot.Gift = i + 1;
            break;
        }

        slot.GiftName = slot.Gift - 1 < s.GiftNames.Count ? s.GiftNames[slot.Gift - 1] : "";
    }

    /// <summary>complete / betrayed / betrayed_fight / fail of a ride: pay and rep, the rest, the contract ended.</summary>
    void RideEnds(World world, Signal s)
    {
        if (RideSlot(s.Kind) is not { } slot) return;

        bool paid = s.Is("complete"), scam = s.Is("betrayed");
        if (paid) RidePaid(slot);
        else if (!scam) RideFailed(slot);

        string outcome = paid ? (s.Kind == 8 ? "A gift" : "Paid") : scam ? "Betrayed" : "Failed";
        string gift = slot.GiftName.Length > 0 ? slot.GiftName : "something";
        string reward = paid && s.Kind == 8 ? $"a gift: {gift}" : paid ? $"{slot.Pay} cats" : "nothing";
        _state.Event(_day, $"{(s.Kind == 9 ? "Caravan master" : "Debtor")} {slot.TargetName}: {outcome.ToLowerInvariant()}");
        Rest(world, slot);
        CutLoose(world, slot);
        _state.Close(slot, _day, outcome, reward);
    }

    void RidePaid(Slot slot)
    {
        BumpRep(slot.Giver, RepDone, "ride delivered");
        FactionRep(slot.Giver.Faction, RepDone);
        if (slot.Kind == 9) _state.Done["caravan:done"] = LedgerState.Get(_state.Done, "caravan:done", 0) + 1;
    }

    void RideFailed(Slot slot)
    {
        BumpRep(slot.Giver, -RepFail, "ride failed");
        FactionRep(slot.Giver.Faction, -RepFail);
    }

    void FactionRep(string faction, int delta)
    {
        if (faction.Length == 0) return;

        string key = "faction:" + faction;
        _state.Rep[key] = Math.Clamp(LedgerState.Get(_state.Rep, key, 0) + delta, 0, 100);
    }

    /// <summary>The debtor rests 3 days between rides; the caravan 2 hours and remembers its last 8 towns.</summary>
    void Rest(World world, Slot slot)
    {
        if (slot.Kind != 9)
        {
            _state.EscortRestDay = _day + EscortRestDays;
            return;
        }

        _state.CaravanRestHours = world.GameHours + CaravanRestHours;
        if (slot.Dest == 0 || _state.CaravanVisited.Contains(slot.Dest)) return;

        _state.CaravanVisited.Add(slot.Dest);
        if (_state.CaravanVisited.Count > CaravanMemory) _state.CaravanVisited.RemoveAt(0);
    }

    /// <summary>His contract ends (the original writes the contract's end stamp into the past): <see cref="EndContract"/> on his squad.</summary>
    void CutLoose(World world, Slot slot)
    {
        var id = world.LastingIds.Find(new LastingId(slot.Target));
        if (world.Characters.TryResolveNext(id, out int s) && world.Characters.Cold(s) is { SquadId: >= 0 } c)
            world.Defer(new EndContract(c.SquadId) { Tick = world.Tick + 1 });
    }

    #endregion

    #region The rescue's own signals

    /// <summary>follow: the freed thief is loose and on the contract; he is locked and the ride runs.</summary>
    void Follow(World world, CharacterId npc, Who w)
    {
        _rescueAsk = Roll(100) < RescueAskPct ? 1 : 0;
        if (RideSlot(13) is not { } slot) return;

        LockOn(world, slot, npc, 0, w.Npc);
        slot.State = 1;
        slot.TalkTries = 0;
        slot.Scouted = true;
    }

    /// <summary>freed: his end talk spoke; the boss pays on the next visit.</summary>
    void Freed(World world)
    {
        if (_state.Slots.FirstOrDefault(s => s.Kind == 13 && s.FoundIndex == 0) is not { } slot) return;

        (slot.FoundIndex, slot.Found, slot.State) = (1, "he is out", 3);
        if (slot.Target != 0) CutLoose(world, slot);
        _state.Event(_day, "He is out. Tell the boss.");
    }

    /// <summary>swap: he was gone; the rescue closes with no blame and a chest job opens here, stocked now.</summary>
    void Swap(World world, Signal s, Who w, int code)
    {
        if (RideSlot(13) is { } rescue) _state.Close(rescue, _day, "Lost", "nothing - he was gone before you got there");

        var chest = NewSlot(s, w, code);
        (chest.Kind, chest.Type, chest.Dest, chest.DestName, chest.NearTown) = (12, "chest", code, w.Town, w.Town);
        _state.Slots.Add(chest);
        if (!StockChest(world, chest)) (chest.Scouted, chest.Found) = (true, "the piece the fence handed you");
    }

    void Escaped()
    {
        if (RideSlot(13) is not { } rescue) return;

        _state.Close(rescue, _day, "Lost", "nothing - he was gone before you got there");
        _state.Event(_day, "He was gone before you got there.");
    }

    #endregion

    #region The tick

    /// <summary>
    /// Each ride: a dead rider ends it; arrival makes it ARRIVED and starts his talk; past its days the talk is forced, later it is dropped.
    /// </summary>
    void RideTick(World world)
    {
        foreach (int kind in (ReadOnlySpan<int>)[8, 9, 13])
        {
            if (RideSlot(kind) is { Target: not 0 } slot) Ride(world, slot);
        }
    }

    void Ride(World world, Slot slot)
    {
        var id = world.LastingIds.Find(new LastingId(slot.Target));
        if (!world.Characters.TryResolveNext(id, out int s) || world.Characters.Cold(s) is not { } c) return;

        if (c.Medical is { Dead: true })
        {
            _state.Slots.Remove(slot);
            return;
        }

        if (slot.State == 1)
        {
            Riding(world, slot, id, s);
            return;
        }

        if (slot.State == 2 && TalkDue(world, slot) && Talk(world, slot, id) && slot.Kind == 13) slot.State = 3;
    }

    void Riding(World world, Slot slot, CharacterId id, int s)
    {
        bool inDest = TownCode(TownName(world.Characters.Next[s].Position)) == slot.Dest;
        if (slot.Kind == 13 ? !inDest : inDest)
        {
            slot.State = 2;
            slot.TalkTries = 0;
            if (slot.Kind == 13) _rescueAsk = Roll(100) < RescueAskPct ? 1 : 0;
            string news = slot.Kind == 13 ? $"{slot.TargetName} is out of {slot.DestName}" : $"{slot.TargetName} reached {slot.DestName} with you";
            _state.Event(_day, news);
            Talk(world, slot, id);
            return;
        }

        int limit = slot.Days > 0 ? slot.Days : FailDays;
        if (_day - slot.AcceptDay >= limit + HardDays) _state.Slots.Remove(slot);
        else if (_day - slot.AcceptDay >= limit && TalkDue(world, slot)) Talk(world, slot, id);
    }

    bool TalkDue(World world, Slot slot) => slot.TalkTries < TalkTries && world.Tick - slot.TalkTick >= (long)(TalkEverySeconds / world.TickSeconds);

    /// <summary>
    /// The rider starts his end talk with the player's character nearest him: event 30 (contract job ended) fired as a conversation through
    /// <c>DialogueSystem.Start</c>; skipped while a conversation is open. The context is written from him first, as no talk start fills it.
    /// </summary>
    bool Talk(World world, Slot slot, CharacterId rider)
    {
        slot.TalkTick = world.Tick;
        slot.TalkTries++;
        if (world.Systems.OfType<DialogueSystem>().FirstOrDefault() is not { } dialogue || StartMethod is null) return false;
        if (InConversation?.GetValue(dialogue) is true || NearestPlayer(world, rider) is not { } player) return false;

        if (Describe(world, rider) is { } w) _ctxTown = TownCode(w.Town);
        _ctxPlayer = player;
        Set("ctx_town", _ctxTown);
        WriteJobVars(world);
        StartMethod.Invoke(dialogue, [world, rider, player, DialogueSystem.ContractJobEnded, true, false]);
        return true;
    }

    CharacterId? NearestPlayer(World world, CharacterId of)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(of, out int o)) return null;

        var at = table.Next[o].Position;
        CharacterId? best = null;
        float bd = float.MaxValue;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is not { IsPlayer: true, Medical.Incapacitated: false }) continue;

            float d = Vector3.Distance(table.Next[i].Position, at);
            if (d < bd) (best, bd) = (table.IdOf(i), d);
        }

        return best;
    }

    #endregion

    #region The draws

    /// <summary>The ride's destination: one of the nearest 3 of this town's routes (a caravan skips the towns it has just been to).</summary>
    int DrawRide(int kind)
    {
        var routes = _tables.Signals.Where(s => s.Is("accept") && s.Kind == kind && !s.Betray && s.Town == _ctxTown && s.Route != 0)
            .GroupBy(s => s.Route).Select(g => g.First()).OrderBy(s => s.Dist).ToList();
        if (kind == 9 && _state.CaravanVisited.Count > 0)
        {
            var fresh = routes.Where(s => !_state.CaravanVisited.Contains(s.Dest)).ToList();
            if (fresh.Count > 0) routes = fresh;
        }

        int pool = Math.Min(routes.Count, EscortNearest);
        return pool > 0 ? routes[Roll(pool)].Route : 0;
    }

    /// <summary>One heist at a time; a route among this town's, moved by the heists done and not the last destination twice.</summary>
    int DrawHeist()
    {
        if (RideSlot(12) is not null) return 0;

        var routes = _tables.Signals.Where(s => s.Is("heist") && s.Kind == 12 && s.Town == _ctxTown && s.Route != 0).ToList();
        int last = LedgerState.Get(_state.Done, "heist:lastdest", 0), done = LedgerState.Get(_state.Done, "heist:done", 0);
        if (routes.Count > 1 && last != 0 && routes.Where(s => s.Dest != last).ToList() is { Count: > 0 } other) routes = other;
        if (routes.Count == 0) return 0;

        uint h = (uint)Math.Max(0, _day) * 2654435761u ^ (uint)_ctxTown * 40503u ^ (uint)done * 7919u ^ 12u * 97u;
        return routes[(int)(h % (uint)routes.Count)].Route;
    }

    /// <summary>One rescue at a time, three days between them from one boss.</summary>
    int DrawRescue()
    {
        if (RideSlot(13) is not null) return 0;
        if (LedgerState.Get(_state.Done, $"rescue:{_ctxTown}", -1) is int d && d >= 0 && _day - d < WindowDays) return 0;

        var routes = _tables.Signals.Where(s => s.Is("rescue") && s.Kind == 13 && s.Town == _ctxTown && s.Route != 0).ToList();
        if (routes.Count == 0) return 0;

        uint h = (uint)Math.Max(0, _day) * 2654435761u ^ (uint)_ctxTown * 40503u ^ 13u * 97u;
        return routes[(int)(h % (uint)routes.Count)].Route;
    }

    /// <summary>The town's thieves have a mark half the time, not twice in two days, none while one runs.</summary>
    int MarkOffer()
    {
        int last = LedgerState.Get(_state.Done, $"mark:{_ctxTown}", -99);
        uint h = (uint)Math.Max(0, _day) * 2654435761u ^ (uint)_ctxTown * 40503u ^ 14u * 97u;
        return RideSlot(14) is null && _ctxTown != 0 && _day - last >= 2 && h % 100u < 50 ? 1 : 0;
    }

    #endregion
}
