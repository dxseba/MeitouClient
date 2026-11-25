using System.Numerics;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Crime;

namespace Meitou.QuestsContracts;

/// <summary>
/// The posters (the original's <c>BountyTick</c> and <c>claimBounty</c> hook): a poster in a player character's pack is read; a wanted one of a
/// read poster near the player is seen (his town and day) and, unless the poster names a unique, priced at the poster's reward with each faction
/// it names; one handed in at a desk is claimed (the desk's pay, <see cref="BountyPatch"/>).
/// </summary>
sealed partial class LedgerSystem
{
    const float SightReach = 2500;

    void BountyTick(World world)
    {
        if (_tables.Bounties.Count == 0 || PlayerPosition(world) is not { } at) return;

        ReadPosters(world);
        var table = world.Characters;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is not { IsPlayer: false, Medical.Dead: false } c) continue;
            if (Vector3.Distance(table.Next[i].Position, at) > SightReach) continue;

            foreach (var b in _tables.Bounties)
            {
                if (Wanted(b, c) && _state.Posters.TryGetValue(b.Poster, out var t) && t.ReadDay >= 0 && t.ClaimedDay < 0) Seen(world, b, t, i, c);
            }
        }
    }

    void ReadPosters(World world)
    {
        foreach (var b in _tables.Bounties)
        {
            if (Track(b.Poster) is not { ReadDay: < 0 } t) continue;
            if (PlayerPosition(world) is not { } at || !Party(world, _ctxPlayer).Any(inv => inv.All().Any(i => i.Record == b.Poster))) continue;

            (t.ReadDay, t.ReadTown) = (_day, TownName(at));
            _state.Event(_day, $"Read a poster: {b.Name}, {b.Reward} cats, wanted by {(b.Faction.Length > 0 ? b.Faction : "someone")}");
        }
    }

    static bool Wanted(Bounty b, CharacterCold c) =>
        b.Targets.Contains(c.RecordId) || c.Name.Length > 0 && b.TargetNames.Contains(c.Name, StringComparison.Ordinal);

    void Seen(World world, Bounty b, PosterTrack t, int slot, CharacterCold c)
    {
        (t.SeenDay, t.SeenTown) = (_day, TownName(world.Characters.Next[slot].Position));
        ulong lasting = world.LastingIds.GetOrGive(world.Characters.IdOf(slot)).Value;
        if (b.Hunt || t.Priced.Contains(lasting)) return;

        t.Priced.Add(lasting);
        if (world.Systems.OfType<CrimeSystem>().FirstOrDefault() is not { } crimes) return;

        foreach (string name in b.Faction.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int f = FactionByName(name);
            if (f < 0) continue;

            int now = crimes.AssignBounty(world, world.Characters.IdOf(slot), f, 1);
            if (now >= 0 && now < b.Reward) crimes.AssignBounty(world, world.Characters.IdOf(slot), f, b.Reward - now);
        }
    }

    int FactionByName(string name)
    {
        var relations = _population.Relations;
        for (int f = 0; f < relations.Count; f++)
        {
            if (string.Equals(relations.Factions[f].Name, name, StringComparison.OrdinalIgnoreCase)) return f;
        }

        return -1;
    }

    /// <summary>
    /// A desk paid for <paramref name="claimed"/>: a poster that names him is claimed with its reward, rep with the desk's faction.
    /// </summary>
    internal void Claimed(World world, CharacterId claimed, int faction)
    {
        if (!world.Characters.TryResolveNext(claimed, out int s) || world.Characters.Cold(s) is not { } c) return;

        string town = TownName(world.Characters.Next[s].Position);
        string by = faction >= 0 && faction < _population.Relations.Count ? _population.Relations.Factions[faction].Name : "the law";
        var b = _tables.Bounties.FirstOrDefault(b => Wanted(b, c) && Track(b.Poster).ClaimedDay < 0);
        if (b is null)
        {
            _state.Event(_day, $"Claimed a bounty on {c.Name} ({by})");
            return;
        }

        var t = Track(b.Poster);
        if (t.ReadDay < 0) (t.ReadDay, t.ReadTown) = (_day, town);
        t.ClaimedDay = _day;
        FactionRep(by, RepDone);
        _state.Event(_day, $"Claimed the bounty on {b.Name}: {b.Reward} cats from {by}");
    }
}

/// <summary>
/// The claim (the original hooks the bounty manager's claim): Harmony prefix and postfix on
/// <c>Meitou.Simulation.Crime.ArrestSystem.PayBounty</c>, the desk's DA_PAY_BOUNTY; the body the carrier holds before the pay is the one claimed.
/// </summary>
static class BountyPatch
{
    static Action<string> s_log = _ => { };
    static bool s_faulted;

    internal static void Install(ModContext context)
    {
        s_log = context.Log;
        var pay = AccessTools.Method(typeof(ArrestSystem), nameof(ArrestSystem.PayBounty), [typeof(World), typeof(CharacterId), typeof(CharacterId)]);
        if (pay is null)
        {
            context.Log("ArrestSystem.PayBounty not found: posters are not marked claimed (the port needs an update)");
            return;
        }

        var before = new HarmonyMethod(typeof(BountyPatch), nameof(Before));
        context.Harmony.Patch(pay, prefix: before, postfix: new HarmonyMethod(typeof(BountyPatch), nameof(After)));
    }

#pragma warning disable IDE1006 // Harmony injects these by name
    static void Before(World world, CharacterId carrier, out CharacterId __state)
    {
        __state = world.Characters.TryResolveNext(carrier, out int c) ? world.Characters.Cold(c)?.Carrying ?? CharacterId.None : CharacterId.None;
    }

    static void After(World world, CharacterId speaker, int __result, CharacterId __state)
#pragma warning restore IDE1006
    {
        if (s_faulted || __result <= 0 || __state.IsNone) return;

        try
        {
            var table = world.Characters;
            int faction = table.TryResolveNext(speaker, out int s) ? table.Cold(s)?.Faction ?? -1 : -1;
            LedgerSystem.Of(world)?.Claimed(world, __state, faction);
        }
        catch (Exception e)
        {
            s_faulted = true;
            s_log($"the bounty claim stopped after an error: {e.Message}");
        }
    }
}
