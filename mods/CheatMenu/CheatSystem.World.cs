using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Crime;

namespace Meitou.CheatMenu;

/// <summary>Unlock doors near camera: every locked lock within 300 of <paramref name="At"/> opens.</summary>
public sealed record UnlockDoorsNear(Vector3 At) : SimCommand, IPausedCommand;

/// <summary>Restock selected trader: the clicked NPC's squad shop restocks now.</summary>
public sealed record RestockTrader(CharacterId Trader) : SimCommand, IPausedCommand;

/// <summary>Take over town of selected: the town the character stands in becomes the player's faction's.</summary>
public sealed record TakeOverTown(CharacterId Character) : SimCommand, IPausedCommand;

/// <summary>Town alarm on or off (selected): the alarm of the town the character stands in.</summary>
public sealed record SetTownAlarm(CharacterId Character, bool On) : SimCommand, IPausedCommand;

/// <summary>
/// The world and town cheats in the tick (README "Facts"):
/// <list type="bullet">
/// <item>Unlock doors near camera: the original clears the locked byte of every object's lock within 300 of the camera; here every locked fixture
/// (door, container, cage) within 300 is unlocked through <c>FixtureRegistry.SetLocked</c>.</item>
/// <item>Restock selected trader: the original's <c>ActivePlatoon::_forceRefreshInventory</c> of the clicked NPC's platoon; here its squad shop's
/// next restock is set to now, so the trade system refills it at its next pass.</item>
/// <item>Take over town of selected: the original sets the town's faction to the player's; here the town gets a replacement of its record naming
/// the player's faction (<c>PopulationData.SetReplacement</c>), kept by <see cref="WorldUpkeep"/> for the world.</item>
/// <item>Town alarm on / off: the original's town alarm switch; here an alarm of the town raised at the character (crime Assault, against the
/// town's faction), and off ends the town's alarms.</item>
/// </list>
/// </summary>
internal sealed partial class CheatSystem
{
    /// <summary>The original's sweep radius round the camera for doors.</summary>
    const float DoorReach = 300;

    /// <summary>Town sites taken over in this world, by site index.</summary>
    readonly SortedSet<int> _taken = [];

    /// <summary>The towns taken over in this world (for the mod's save data).</summary>
    internal SortedSet<int> TakenTowns => _taken;

    /// <summary>Handles a world command; the published view does not change.</summary>
    bool WorldInputs(World world, SimCommand command)
    {
        switch (command)
        {
            case UnlockDoorsNear unlock: UnlockNear(world, unlock.At); break;
            case RestockTrader restock: Restock(world, restock.Trader); break;
            case TakeOverTown take: TakeOver(world, take.Character); break;
            case SetTownAlarm alarm: Alarm(world, alarm.Character, alarm.On); break;
        }

        return false;
    }

    /// <summary>Keeps the taken towns the player's: an override that put the town's own record back is undone.</summary>
    void WorldUpkeep(World world)
    {
        if (_taken.Count == 0 || data.FactionId(world.Player.Faction) is not { } player) return;

        foreach (int site in _taken) Own(site, player);
    }

    void UnlockNear(World world, Vector3 at)
    {
        int count = 0;
        foreach (var fixture in world.Fixtures.All)
        {
            if (fixture.Lock is not { Locked: true } || Vector3.Distance(fixture.Position, at) > DoorReach) continue;

            world.Fixtures.SetLocked(fixture, false);
            count++;
        }

        world.Post($"CheatMenu: unlocked {count} doors");
    }

    static void Restock(World world, CharacterId trader)
    {
        var table = world.Characters;
        if (table.TryResolveNext(trader, out int slot) && table.Cold(slot) is { IsPlayer: false } cold
            && world.Squads.Find(cold.SquadId)?.Shop is { } shop)
        {
            shop.NextRestockTick = world.Tick;
            return;
        }

        world.Post("CheatMenu: restock: click a trader first");
    }

    /// <summary>The town site the character stands in; -1 for none or a character not in the world.</summary>
    int TownOf(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) ? data.TownAt(world.Characters.Next[slot].Position) : -1;

    void TakeOver(World world, CharacterId id)
    {
        int site = TownOf(world, id);
        if (site < 0 || data.FactionId(world.Player.Faction) is not { } player)
        {
            world.Post("CheatMenu: take over: the selected character is in no town");
            return;
        }

        _taken.Add(site);
        Own(site, player);
        world.Post("CheatMenu: took over " + data.Sites[site].Town.ShownName());
    }

    /// <summary>The town's record, with the player's faction, in its place (nothing when it is the player's already).</summary>
    void Own(int site, string player)
    {
        var town = data.Sites[site].Town;
        if (town.Faction == player) return;

        data.SetReplacement(site, town with { Faction = player });
    }

    void Alarm(World world, CharacterId id, bool on)
    {
        int site = TownOf(world, id);
        if (site < 0 || world.Systems.OfType<CrimeSystem>().FirstOrDefault() is not { } crime)
        {
            world.Post("CheatMenu: alarm: the selected character is in no town");
            return;
        }

        if (on)
        {
            int faction = site < data.SiteFactions.Count ? data.SiteFactions[site] : -1;
            crime.TownAlarm(world, id.Slot, site, CrimeCode.Assault, faction);
            return;
        }

        foreach (var alarm in crime.Alarms)
        {
            if (alarm.Town == site) alarm.Life = -1;
        }
    }
}
