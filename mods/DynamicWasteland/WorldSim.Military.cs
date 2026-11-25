using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.DynamicWasteland;

/// <summary>
/// Offensives (a real campaign marching on the enemy town), the battle when the army arrives (the original's formula), conquest of the town and
/// its garrison, and the economy.
/// </summary>
sealed partial class WorldSim
{
    /// <summary>Game hours before an offensive that never started or never arrived is given up.</summary>
    const double OffensiveHours = 240, StartHours = 24;

    #region Offensives

    /// <summary>Each nation at war with no offensive in flight may send one against its enemy's nearest town.</summary>
    void LaunchOffensives(World world)
    {
        foreach (var war in _state.Wars.ToList())
        {
            foreach (var (attacker, defender) in new[] { (war.A, war.B), (war.B, war.A) })
            {
                if (_state.Offensives.Any(o => o.Attacker == attacker) || _state.Find(attacker) is not { BankruptDays: 0 } a) continue;
                if (!Chance(world, OffensiveChance[_s.Intensity], 8) || Target(world, attacker, defender) is not { } site) continue;

                Launch(world, a, defender, site);
            }
        }
    }

    /// <summary>The defender's town nearest any of the attacker's towns (ties: the lower site), none when either holds no town.</summary>
    static int? Target(World world, string attacker, string defender)
    {
        var data = Pop(world)!.Data;
        var own = TownsOf(world, attacker);
        int? best = null;
        float bestDistance = float.MaxValue;
        foreach (int site in TownsOf(world, defender))
        {
            var p = data.Sites[site].Position;
            foreach (int from in own)
            {
                float d = Vector3.DistanceSquared(p, data.Sites[from].Position);
                if (d >= bestDistance) continue;

                (best, bestDistance) = (site, d);
            }
        }

        return best;
    }

    /// <summary>
    /// Sends the attacker's army: one of its assault campaigns (FACTION <c>campaigns</c> with key ASSAULT_TOWN) requested against the town,
    /// which then gathers and marches as any campaign; without one the battle is fought at once off the map.
    /// </summary>
    void Launch(World world, Nation attacker, string defender, int site)
    {
        var pop = Pop(world)!;
        int f = Index(world, attacker.Faction);
        var campaigns = pop.Data.Factions[f].Campaigns
            .Select(c => pop.CampaignOf(c.Id))
            .Where(c => c is { Key: CampaignKey.AssaultTown, IsHostile: true })
            .Select(c => c!.Id)
            .ToList();
        string town = TownName(world, site);
        if (campaigns.Count > 0)
        {
            string campaign = campaigns[Below(world, campaigns.Count, 9)];
            if (pop.RequestCampaign(world, f, campaign, CampaignTown.OfSite(site)))
            {
                _state.Offensives.Add(new Offensive { Attacker = attacker.Faction, Defender = defender, Site = site, Campaign = campaign, Since = world.GameHours });
                Say(world, Fmt("{0} marches on {1}", Name(world, attacker.Faction), town), popup: _s.BattlePopups);
                return;
            }
        }

        // No army to march: a clash off the map, which takes the town only when WorldBattlesOnly is off.
        Battle(world, new Offensive { Attacker = attacker.Faction, Defender = defender, Site = site, Since = world.GameHours }, canConquer: !_s.WorldBattlesOnly);
    }

    /// <summary>
    /// Follows the offensives' campaigns: the battle once the army arrived; given up when the campaign never started, ended before arriving
    /// (the attack repelled on the way) or ran too long.
    /// </summary>
    void WatchOffensives(World world)
    {
        if (Pop(world) is not { } pop) return;

        foreach (var o in _state.Offensives.ToList())
        {
            var manager = pop.Wars.FirstOrDefault(m => m.Faction == Index(world, o.Attacker));
            var instance = Instance(manager, o);
            if (instance is { ArrivedAt: >= 0 })
            {
                _state.Offensives.Remove(o);
                Battle(world, o, canConquer: true);
            }
            else if (instance is null && Gone(world, manager, o) || world.GameHours - o.Since > OffensiveHours)
            {
                _state.Offensives.Remove(o);
                Say(world, Fmt("{0} repels the attack on {1}", Name(world, o.Defender), TownName(world, o.Site)), popup: _s.BattlePopups);
            }
        }
    }

    /// <summary>The offensive's campaign: found by id once started, else the first active one of its record against its town (then bound).</summary>
    static CampaignInstance? Instance(WarManager? manager, Offensive o)
    {
        if (manager is null) return null;
        if (o.Instance >= 0) return manager.Actives.FirstOrDefault(c => c.Id == o.Instance);

        var found = manager.Actives.FirstOrDefault(c => c.CampaignId == o.Campaign && c.Target.Site == o.Site);
        if (found is not null) o.Instance = found.Id;
        return found;
    }

    /// <summary>Whether the campaign is gone: started and ended, or never started and no longer requested after a day.</summary>
    static bool Gone(World world, WarManager? manager, Offensive o)
    {
        if (o.Instance >= 0) return true;

        bool requested = manager?.Requests.Any(r => r.CampaignId == o.Campaign && r.Target.Site == o.Site) ?? false;
        return !requested && world.GameHours - o.Since > StartHours;
    }

    #endregion

    #region Battle

    /// <summary>
    /// The original's battle: each side's score from its power, military and exhaustion (the defender's also from the town) plus a roll of
    /// -40 to 40; both pay for it (attacker 800-1999 Cats, defender 500-1499); the loser loses more military and gains more exhaustion. An
    /// attacker's win with <paramref name="canConquer"/> takes the town.
    /// </summary>
    void Battle(World world, Offensive o, bool canConquer)
    {
        if (_state.Find(o.Attacker) is not { } a || _state.Find(o.Defender) is not { } d || _state.WarOf(a.Faction, d.Faction) is not { } war) return;

        a.Treasury -= 800 + Below(world, 1200, 10);
        d.Treasury -= 500 + Below(world, 1000, 11);
        int attack = Power(world, a) + a.Military - 4 * a.Exhaustion + Below(world, 81, 12) - 40;
        int defence = Power(world, d) + d.Military + Defence(world, d, o.Site) - 4 * d.Exhaustion + Below(world, 81, 13) - 40;
        bool won = attack > defence;
        var (winner, loser) = won ? (a, d) : (d, a);
        winner.Military = Math.Max(0, winner.Military - (2 + Below(world, 8, 14)));
        loser.Military = Math.Max(0, loser.Military - (2 + Below(world, 8, 15) + 3 + Below(world, 6, 16)));
        winner.Exhaustion += 3 + Below(world, 6, 17);
        loser.Exhaustion += 7 + Below(world, 8, 18);
        if (winner.Faction == war.A) war.WinsA++;
        else war.WinsB++;

        string town = TownName(world, o.Site);
        WarCasualties(world, o.Site, d.Faction, conquered: won && canConquer && _s.TerritoryChanges);
        if (_state.Career.Patron == winner.Faction) Merit(5);
        if (!won)
        {
            Say(world, Fmt("{0} repels the attack on {1}", Name(world, d.Faction), town), popup: _s.BattlePopups);
            return;
        }

        if (!canConquer || !_s.TerritoryChanges)
        {
            Say(world, Fmt("{0} wins near {1}", Name(world, a.Faction), town), popup: _s.BattlePopups);
            return;
        }

        if (a.Faction == war.A) war.TownsA++;
        else war.TownsB++;

        // Sacking: the conqueror takes a share of the loser's treasury (the original's WarLooting; port share a tenth).
        long loot = Math.Max(0, d.Treasury / 10);
        d.Treasury -= loot;
        a.Treasury += loot;
        d.Stability -= 5;
        _state.Pending.RemoveAll(c => c.Site == o.Site);
        _state.Pending.Add(new Conquest { Site = o.Site, Faction = a.Faction });
    }

    /// <summary>A nation's power: six per town it holds (port stand-in for the original's power score).</summary>
    int Power(World world, Nation n) => 6 * TownsOf(world, n.Faction).Count + Hired(n);

    /// <summary>A town's defence: 40 plus a third of its size (town 90, village 45), 25 more for the nation's capital (its first town).</summary>
    static int Defence(World world, Nation n, int site)
    {
        var data = Pop(world)!.Data;
        int size = data.Sites[site].Town.Type == TownType.Town ? 90 : 45;
        bool capital = TownsOf(world, n.Faction) is [var first, ..] && first == site;
        return 40 + size / 3 + (capital ? 25 : 0);
    }

    #endregion

    #region Conquest

    /// <summary>
    /// Changes the owner of each won town that is not loaded (<c>ApplyTownVersion</c>: the occupied version, or the town's own record when its
    /// first owner takes it back), holds it against the game's own overrides and garrisons it; a loaded town waits.
    /// </summary>
    void ApplyConquests(World world)
    {
        var pop = Pop(world)!;
        foreach (var c in _state.Pending.ToList())
        {
            var site = pop.Data.Sites[c.Site];
            string? version = site.Record.Faction == c.Faction ? null : TownVersions.Id(site.Record.Id, c.Faction);
            if (version is not null && pop.Data.Db.Find(version) is null)
            {
                _state.Pending.Remove(c);
                continue;
            }

            string? from = pop.Data.FactionId(pop.Data.SiteFactions[c.Site]);
            if (!pop.ApplyTownVersion(world, c.Site, version)) continue;

            _state.Pending.Remove(c);
            pop.HoldTown(c.Site, version is not null);
            if (version is null) _state.Owners.Remove(c.Site);
            else _state.Owners[c.Site] = c.Faction;

            string previous = from is null ? tr("the former owner") : Name(world, from);
            Say(world, Fmt("{0} took {1} from {2}.", Name(world, c.Faction), TownName(world, c.Site), previous), popup: true);
            if (_s.Garrison) Garrison(world, c.Site, c.Faction);
        }
    }

    /// <summary>ConquestGarrisonMax squads of the new owner's <c>squads</c> list, homed at the town, in a ring of 200 to 700 round it.</summary>
    void Garrison(World world, int site, string faction)
    {
        var pop = Pop(world)!;
        int f = Index(world, faction);
        var squads = pop.Data.Factions[f].Squads
            .Where(s => pop.Data.Db.Find(s.Id) is { Type: FcsRecordType.SquadTemplate })
            .Select(s => s.Id)
            .ToList();
        if (squads.Count == 0) return;

        var p = pop.Data.Sites[site].Position;
        for (int i = 0; i < _s.GarrisonMax; i++)
        {
            int id = pop.SpawnPlatoon(world, f, squads[Below(world, squads.Count, 19)], site, new Vector2(p.X, p.Z), 200, 700);
            if (world.Platoons.Find(id) is { } platoon) platoon.ModTags[modId] = "garrison:" + site;
        }
    }

    #endregion

    #region Economy

    /// <summary>
    /// A step's share of a day: taxes (TaxPerWorker for each of the towns' workers, 300 a town and 80 a village, scaled by economy / 50) and
    /// trade (TradeIncomePerEconomyPoint per economy point), less the army's wages (SoldierWage for ten soldiers per military point). An empty
    /// treasury counts bankrupt days, which cost military and stability.
    /// </summary>
    void Economy(World world)
    {
        double share = _s.TickHours / 24.0;
        var data = Pop(world)!.Data;
        foreach (var n in _state.Nations)
        {
            long workers = TownsOf(world, n.Faction).Sum(s => data.Sites[s].Town.Type == TownType.Town ? 300L : 80L);
            long income = workers * _s.TaxPerWorker * n.Economy / 50 + (long)_s.TradeIncome * n.Economy;
            long upkeep = (long)_s.SoldierWage * n.Military * 10;
            n.Treasury += (long)((income - upkeep) * share);
            if (n.Treasury > 0)
            {
                n.BankruptDays = 0;
                continue;
            }

            if (n.BankruptDays++ == 0) Say(world, Fmt("{0} can no longer pay its soldiers. Desertions and unrest are spreading.", Name(world, n.Faction)),
                popup: true);
            n.Military = Math.Max(0, n.Military - 2);
            n.Stability = Math.Max(0, n.Stability - 1);
        }
    }

    /// <summary>
    /// Port drift each step: a nation at peace rebuilds its army (military +1 up to 100 while it can pay) and rests (exhaustion -2); stability
    /// drifts toward 60; scores stay within 0 to 100.
    /// </summary>
    void Recover(World world)
    {
        foreach (var n in _state.Nations)
        {
            bool atWar = _state.Wars.Any(w => w.Involves(n.Faction));
            if (!atWar)
            {
                n.Exhaustion = Math.Max(0, n.Exhaustion - 2);
                if (n.Treasury > 0) n.Military = Math.Min(100, n.Military + 1);
            }

            n.Stability += Math.Sign(60 - n.Stability);
            n.Stability = Math.Clamp(n.Stability, 0, 100);
            n.Exhaustion = Math.Clamp(n.Exhaustion, 0, 100);
        }
    }

    #endregion
}
