using System.Numerics;
using Meitou.Data.Gameplay.Construction;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Production;

namespace Meitou.BetterLooting;

/// <summary>Alt + the looting key, the original's Store All: the character's loose items into the player's storage; and the fork's ground pickup.</summary>
sealed partial class LootSystem
{
    /// <summary>The fork's cap on queued ground pick-ups per press.</summary>
    const int GroundLimit = 32;

    /// <summary>
    /// The fork's cap on ground items examined per press (the nearest).
    /// </summary>
    const int GroundExamined = 256;

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
        int units = Ordered(Carried(world, owner)).Sum(c => Store(c, Tiered(stores, c.Item), used));
        world.Post(units > 0
            ? $"{owner.Name} stored {Plural(units, "item")} in {Plural(used.Count, "storage")}."
            : $"{owner.Name} had nothing to store.");
    }

    /// <summary>
    /// The character's own loose items (main grid and worn bag; no worn gear, bag or money): every one, or with <see cref="LootSettings.DepositByRules"/>
    /// those that pass the looting rules.
    /// </summary>
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
            if (!Settings.DepositByRules) list.Add(new(item, holder, ValueOf(world, item), item.UnitWeight));
            else if (Judge(world, item, holder, item.IsStolenFor(owner.Faction, data)) is { } candidate) list.Add(candidate);
        }

        return list;
    }

    /// <summary>
    /// The stores in the order the original's deposit picks them for an item: those limited to it (an ore box for ore, a weapon rack for a weapon)
    /// before the general ones, each tier nearest first (the list comes sorted by distance).
    /// </summary>
    static List<Machine> Tiered(List<Machine> stores, ItemInstance item) =>
        [.. stores.Where(m => Specialised(m.Data, item)), .. stores.Where(m => !Specialised(m.Data, item))];

    /// <summary>
    /// A storage whose filter names the item's record, or that takes only a record type other than ITEM.
    /// </summary>
    static bool Specialised(MachineData data, ItemInstance item) =>
        data.StorageLimit.Contains(item.Record) || data.ItemTypeLimit != Meitou.Data.Fcs.FcsRecordType.Item && data.ItemTypeLimit == item.Type;

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
        var candidates = world.GroundItems.All
            .Where(g => g.Slot is null && Vector3.Distance(g.Position, at) <= Settings.RadiusUnits)
            .OrderBy(g => Vector3.DistanceSquared(g.Position, at))
            .ThenBy(g => g.Id)
            .Take(GroundExamined)
            .Where(g => GroundPickable(g.Item) && Passes(world, g.Item, GroundStolen(world, g, looter)))
            .ToList();
        var picks = Route(candidates, at);

        var id = world.Characters.IdOf(lead);
        foreach (var pick in picks)
            world.Commands.Enqueue(new PickUpItemOrder([id], pick.Id) { Tick = world.Tick, Queued = true, Alt = Settings.AllowStolen });

        return picks.Count;
    }

    /// <summary>
    /// The fork's pickup route: the nearest item to the character first, then each time the nearest left to the previous one (ties by item id),
    /// up to <see cref="GroundLimit"/>.
    /// </summary>
    static List<GroundItem> Route(List<GroundItem> left, Vector3 from)
    {
        var route = new List<GroundItem>();
        while (route.Count < GroundLimit && left.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < left.Count; i++)
            {
                float d = Vector3.DistanceSquared(left[i].Position, from), bestD = Vector3.DistanceSquared(left[best].Position, from);
                if (d < bestD || d == bestD && left[i].Id < left[best].Id) best = i;
            }

            route.Add(left[best]);
            from = left[best].Position;
            left.RemoveAt(best);
        }

        return route;
    }

    /// <summary>
    /// A ground item the fork may order picked up: no death item, no money and no container holding anything.
    /// </summary>
    static bool GroundPickable(ItemInstance item) =>
        !item.Death && item.ItemFunction != LootTransfer.MoneyFunction && item.Contents is not { Items.Count: > 0 };

    bool GroundStolen(World world, GroundItem ground, CharacterCold looter) =>
        ground.OwnedBy >= 0 && ground.OwnedBy != world.Player.Faction || ground.Item.IsStolenFor(looter.Faction, data);
}
