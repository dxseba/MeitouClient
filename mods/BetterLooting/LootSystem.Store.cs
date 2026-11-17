using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Production;

namespace Meitou.BetterLooting;

/// <summary>Alt+Insert, the original's Store All: the lead's loose items into the player's storage; and the fork's ground pickup.</summary>
sealed partial class LootSystem
{
    /// <summary>The fork's cap on queued ground pick-ups per press.</summary>
    const int GroundLimit = 32;

    void Deposit(World world, int lead, CharacterCold owner)
    {
        var at = world.Characters.Next[lead].Position;
        var stores = world.Machines.All
            .Select(m => (Machine: m, Distance: PlayerStorage(world, m, at)))
            .Where(s => s.Distance is not null)
            .OrderBy(s => s.Distance)
            .Select(s => s.Machine)
            .ToList();

        if (stores.Count == 0)
        {
            world.Post($"{owner.Name} has no storage of yours within {Settings.LootRadius:0} m.");
            return;
        }

        var used = new HashSet<Machine>();
        int units = Ordered(Carried(world, owner)).Sum(c => Store(c, stores, used));
        world.Post(units > 0
            ? $"{owner.Name} stored {Plural(units, "item")} in {Plural(used.Count, "storage")}."
            : $"{owner.Name} had nothing to store.");
    }

    /// <summary>The lead's own loose items (main grid and worn bag; no worn gear, bag or money) that pass the rules.</summary>
    List<LootCandidate> Carried(World world, CharacterCold owner)
    {
        var inventory = owner.Inventory!;
        var bag = inventory.Backpack?.Contents;
        var items = inventory.Items.Where(i => i.Section == "main").Select(i => (Item: i, Owner: inventory))
            .Concat(bag?.Items.Select(i => (Item: i, Owner: bag)) ?? []);

        var list = new List<LootCandidate>();
        foreach (var (item, holder) in items.ToList())
        {
            if (item.LockedOn || item.Contents is not null || item.ItemFunction == LootTransfer.MoneyFunction) continue;
            if (Judge(world, item, holder, item.IsStolenFor(owner.Faction, data)) is { } candidate) list.Add(candidate);
        }

        return list;
    }

    /// <summary>One item into the nearest storages whose filter takes it, until it is gone; the units moved.</summary>
    static int Store(LootCandidate candidate, List<Machine> stores, HashSet<Machine> used)
    {
        var item = candidate.Item;
        int units = 0;
        foreach (var store in stores)
        {
            if (!store.Data.Accepts(item.Record, item.Type)) continue;

            int moved = InventoryCommands.MoveUnits(candidate.Owner, item, 0, store.Storage!);
            units += moved;
            if (moved > 0) used.Add(store);
            if (!candidate.Owner.Items.Contains(item)) break;
        }

        return units;
    }

    /// <summary>Queues pick-up orders for the nearest ground items that pass the rules; how many.</summary>
    int QueueGround(World world, int lead, CharacterCold looter)
    {
        var at = world.Characters.Next[lead].Position;
        var picks = world.GroundItems.All
            .Where(g => g.Slot is null && Vector3.Distance(g.Position, at) <= Settings.RadiusUnits)
            .Where(g => !g.Item.Death && Passes(world, g.Item, GroundStolen(world, g, looter)))
            .OrderBy(g => Vector3.Distance(g.Position, at))
            .Take(GroundLimit)
            .ToList();

        var id = world.Characters.IdOf(lead);
        foreach (var pick in picks)
            world.Commands.Enqueue(new PickUpItemOrder([id], pick.Id) { Tick = world.Tick, Queued = true, Alt = Settings.AllowStolen });

        return picks.Count;
    }

    bool GroundStolen(World world, GroundItem ground, CharacterCold looter) =>
        ground.OwnedBy >= 0 && ground.OwnedBy != world.Player.Faction || ground.Item.IsStolenFor(looter.Faction, data);
}
