using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Trading;

namespace Meitou.SentientSands;

/// <summary>
/// The item actions of a deal (server <c>chat/offers.py</c> <c>_acts</c>): <c>GIVE_ITEM: name: count</c> (the NPC hands over what it carries),
/// <c>SELL_ITEM</c> (from its shop's stock), <c>SPAWN_ITEM: name</c> (a charity item, made), <c>TAKE_ITEM: name: count</c> (from the player's
/// speaker, or the squad mate after <c>&gt;</c>, to the NPC) and <c>BUY_ITEM</c> (into the shop's stock). Items are found by their shown name.
/// </summary>
sealed partial class SentientSystem
{
    Dictionary<string, string>? _recordByName;

    bool RunItem(World world, ChatAction a, CharacterCold npc)
    {
        var (name, count) = ItemArgs(a.Args);
        var player = Inventory(world, a.Holder.IsNone ? a.Player : a.Holder);
        var shop = TradeSystem.ShopOf(world, npc)?.Stock;
        switch (a.Name)
        {
            case "GIVE_ITEM": Move(npc.Inventory, Inventory(world, a.Player), name, count); return true;
            case "SELL_ITEM": Move(shop ?? npc.Inventory, Inventory(world, a.Player), name, count); return true;
            case "TAKE_ITEM": Move(player, npc.Inventory, name, count); return true;
            case "BUY_ITEM": Move(player, shop ?? npc.Inventory, name, count); return true;
            case "SPAWN_ITEM": Spawn(Inventory(world, a.Player), name, Math.Max(1, count)); return true;
            default: return false;
        }
    }

    static (string Name, int Count) ItemArgs(string args)
    {
        var parts = args.Split(':');
        int count = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int c) ? c : 1;
        return (parts[0].Trim(), count);
    }

    static Inventory? Inventory(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int s) ? world.Characters.Cold(s)?.Inventory : null;

    /// <summary>Moves up to <paramref name="count"/> units of the item named <paramref name="name"/>; a unit with no room goes back.</summary>
    static void Move(Inventory? from, Inventory? into, string name, int count)
    {
        if (from is null || into is null) return;

        while (count > 0 && from.All().FirstOrDefault(i => Named(i, name)) is { } item)
        {
            int n = Math.Min(count, item.Quantity);
            var moved = n == item.Quantity ? item : item.WithQuantity(n);
            if (ReferenceEquals(moved, item)) from.Remove(item);
            else item.Quantity -= n;

            if (!into.Add(moved))
            {
                if (ReferenceEquals(moved, item)) from.Add(item);
                else item.Quantity += n;
                return;
            }

            count -= n;
        }
    }

    static bool Named(ItemInstance item, string name) =>
        string.Equals(item.ShownName(), name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase);

    void Spawn(Inventory? into, string name, int count)
    {
        if (into is null || RecordNamed(name) is not { } record || population.Items.Create(record, count) is not { } item) return;

        into.Add(item);
    }

    string? RecordNamed(string name)
    {
        _recordByName ??= BuildNames();
        return _recordByName.GetValueOrDefault(name.Trim());
    }

    Dictionary<string, string> BuildNames()
    {
        FcsRecordType[] types = [FcsRecordType.Item, FcsRecordType.Weapon, FcsRecordType.Armour, FcsRecordType.Crossbow, FcsRecordType.Container,
            FcsRecordType.MapItem, FcsRecordType.LimbReplacement];
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in types.SelectMany(population.Db.OfType).OrderBy(r => r.StringId, StringComparer.Ordinal)) names.TryAdd(r.Name, r.StringId);

        return names;
    }
}
