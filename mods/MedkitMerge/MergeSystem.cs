using System.Globalization;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.MedkitMerge;

/// <summary>
/// When merges run: after a player character's Sort (the game's <see cref="ArrangeInventory"/>, also while paused, as this system is after every
/// other in <c>Inputs</c>), and when a supply item shows up in a player character's inventory that was not there at the last look (picked up,
/// given by a squad mate, bought, crafted). Sorts merge every eligible item; a received kit pours itself out; a received bolt pack has its whole
/// group poured together. Serial phases only.
/// </summary>
sealed class MergeSystem(Action<string> log) : IPausedCommandHandler
{
    /// <summary>Ticks between two looks for received supplies (engine choice; the original reacts to each add on the next world update).</summary>
    const int LookEveryTicks = 6;

    /// <summary>The supply items each player character held at the last look (by reference).</summary>
    Dictionary<CharacterId, HashSet<ItemInstance>> _known = [];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is ArrangeInventory arrange) Sorted(world, arrange.Character);
        }

        if (commands.Count > 0) LookForReceived(world);
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % LookEveryTicks == 0) LookForReceived(world);
    }

    /// <summary>The Sort trigger: every eligible supply is a donor; the inventory is arranged again when anything moved.</summary>
    void Sorted(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true, Inventory: { } root } cold) return;

        var carried = new CarriedSupplies(root);
        var donors = carried.Items.Where(i => MergeRules.Refusal(i.Item) is null).Select(i => i.Item).ToList();
        if (Pour(cold.Name, carried, donors, "sort"))
            InventoryCommands.Arrange(world, root, ItemOwner.Of(id), InventoryArrangeReason.Button);
    }

    /// <summary>Compares each player character's supplies with the last look's; a newcomer is the pickup trigger. A character seen first only notes.</summary>
    void LookForReceived(World world)
    {
        var table = world.Characters;
        var known = new Dictionary<CharacterId, HashSet<ItemInstance>>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Inventory: { } root } cold) continue;

            var id = table.IdOf(slot);
            var carried = new CarriedSupplies(root);
            if (_known.TryGetValue(id, out var before)) Received(cold.Name, carried, before);

            known[id] = new HashSet<ItemInstance>(new CarriedSupplies(root).Items.Select(i => i.Item), ReferenceEqualityComparer.Instance);
        }

        _known = known;
    }

    void Received(string who, CarriedSupplies carried, HashSet<ItemInstance> before)
    {
        var arrived = carried.Items.Select(i => i.Item).Where(i => !before.Contains(i)).ToList();
        foreach (var item in arrived)
        {
            if (!carried.Holds(item)) continue;

            List<ItemInstance> donors = item.ItemFunction == MergeRules.Ammo
                ? [.. carried.Items.Select(i => i.Item).Where(i => MergeRules.Refusal(i) is null && MergeRules.Matches(i, item))]
                : [item];
            Pour(who, carried, donors, "pickup");
        }
    }

    /// <summary>Each donor in turn pours into the matching targets; true when any supply moved.</summary>
    bool Pour(string who, CarriedSupplies carried, List<ItemInstance> donors, string trigger)
    {
        bool moved = false;
        foreach (var donor in donors)
        {
            if (carried.Holds(donor) && MergeRules.Refusal(donor) is null) moved |= PourOne(who, carried, donor, trigger);
        }

        return moved;
    }

    /// <summary>
    /// One donor (the original's per-donor step): kits fill the targets in inventory order; bolt packs fill the fullest first (ties in inventory
    /// order), each target taking what fits. A donor left empty is removed. True when anything moved.
    /// </summary>
    bool PourOne(string who, CarriedSupplies carried, ItemInstance donor, string trigger)
    {
        bool ammo = donor.ItemFunction == MergeRules.Ammo;
        int donorIndex = carried.IndexOf(donor);
        var targets = carried.Items
            .Select((entry, index) => (entry.Item, Index: index))
            .Where(t => !ReferenceEquals(t.Item, donor) && MergeRules.Refusal(t.Item) is null && MergeRules.Matches(t.Item, donor))
            .ToList();
        if (ammo) targets = [.. targets.OrderByDescending(t => t.Item.Charges).ThenBy(t => t.Index)];

        float start = donor.Charges, left = start;
        foreach (var (target, index) in targets)
        {
            if (left <= 0) break;

            float amount = ammo
                ? MergeRules.AmmoAmount(left, target.Charges, target.MaxCharges, index < donorIndex)
                : MergeRules.KitAmount(left, target.Charges, target.MaxCharges);
            if (amount <= 0) continue;

            target.Charges += amount;
            left -= amount;
        }

        if (left == start) return false;

        donor.Charges = left;
        if (left == 0) carried.Remove(donor);
        Log(who, donor, trigger, start - left, left);
        return true;
    }

    void Log(string who, ItemInstance donor, string trigger, float moved, float left) =>
        log(string.Create(CultureInfo.InvariantCulture,
            $"merged player={who} trigger={trigger} record={donor.Record} type={MergeRules.KindName(donor.ItemFunction)} transferred={moved} donor-left={left}"));
}

/// <summary>
/// A character's inventories as the original walks them: its own, then each container inside it (the worn backpack first among them where it
/// lies first) depth first, at most four deep; the items of each in order.
/// </summary>
sealed class CarriedSupplies
{
    const int MaxDepth = 4;

    readonly List<(Inventory Inventory, ItemInstance Item)> _items = [];

    public CarriedSupplies(Inventory root) => Walk(root, 0);

    /// <summary>The supply items with the inventory holding each, in walk order.</summary>
    public IReadOnlyList<(Inventory Inventory, ItemInstance Item)> Items => _items;

    public int IndexOf(ItemInstance item) => _items.FindIndex(i => ReferenceEquals(i.Item, item));

    /// <summary>Whether the item is still where the walk found it.</summary>
    public bool Holds(ItemInstance item) => IndexOf(item) is >= 0 and var k && _items[k].Inventory.Items.Contains(item);

    public void Remove(ItemInstance item)
    {
        int k = IndexOf(item);
        if (k < 0) return;

        _items[k].Inventory.Remove(item);
        _items.RemoveAt(k);
    }

    void Walk(Inventory inventory, int depth)
    {
        foreach (var item in inventory.Items)
        {
            if (MergeRules.IsSupply(item)) _items.Add((inventory, item));
        }

        if (depth >= MaxDepth) return;

        foreach (var item in inventory.Items)
        {
            if (item.Contents is { } bag) Walk(bag, depth + 1);
        }
    }
}
