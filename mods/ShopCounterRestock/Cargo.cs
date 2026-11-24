using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// Counting and moving goods by units (stack quantities): what a storage holds, what a worker carries in its main inventory and worn backpack
/// (never worn equipment or a container that holds items), and one-unit moves that only take a unit once it has found a place.
/// </summary>
internal static class Cargo
{
    const string Main = "main";
    const string BagContent = "backpack_content";

    /// <summary>
    /// The units of <paramref name="item"/> in a building's storage.
    /// </summary>
    public static int InStorage(Inventory storage, string item) => StorageStacks(storage, item).Sum(i => i.Quantity);

    /// <summary>
    /// The units of <paramref name="item"/> the worker carries as cargo.
    /// </summary>
    public static int Carried(CharacterCold worker, string item) => CargoStacks(worker, item).Sum(s => s.Item.Quantity);

    static IEnumerable<ItemInstance> StorageStacks(Inventory storage, string item) =>
        storage.Items.Where(i => i.Record == item && !Holds(i));

    /// <summary>
    /// The worker's stacks of <paramref name="item"/> in its main grid and its worn backpack's, each with the inventory it is in.
    /// </summary>
    static IEnumerable<(ItemInstance Item, Inventory Owner)> CargoStacks(CharacterCold worker, string item)
    {
        if (worker.Inventory is not { } own) yield break;

        foreach (var i in own.Items)
        {
            if (i.Section == Main && i.Record == item && !Holds(i)) yield return (i, own);
        }

        if (own.Backpack?.Contents is not { } bag) yield break;

        foreach (var i in bag.Items)
        {
            if (i.Section == BagContent && i.Record == item && !Holds(i)) yield return (i, bag);
        }
    }

    static bool Holds(ItemInstance item) => item.Contents is { Items.Count: > 0 };

    /// <summary>
    /// Whether the worker could take one unit of the item (a stack of it in the storage as the sample).
    /// </summary>
    public static bool WorkerHasRoom(CharacterCold worker, Inventory storage, string item) =>
        worker.Inventory is { } own && StorageStacks(storage, item).FirstOrDefault() is { } sample && own.HasRoomFor(sample.WithQuantity(1));

    /// <summary>
    /// Whether the counter takes one unit of an item the worker carries.
    /// </summary>
    public static bool CounterHasRoom(Inventory counter, CharacterCold worker, string item) =>
        CargoStacks(worker, item).FirstOrDefault() is { Item: { } sample } && counter.HasRoomFor(sample.WithQuantity(1));

    /// <summary>
    /// Moves up to <paramref name="count"/> units of the item from the storage into the worker's cargo; the number moved.
    /// </summary>
    public static int Load(Inventory storage, CharacterCold worker, string item, int count)
    {
        if (worker.Inventory is not { } own) return 0;

        int moved = 0;
        while (moved < count && StorageStacks(storage, item).FirstOrDefault() is { } stack && MoveUnit(stack, storage, own)) moved++;
        return moved;
    }

    /// <summary>
    /// Moves up to <paramref name="count"/> units of the item from the worker's cargo into the counter; the number moved.
    /// </summary>
    public static int Unload(CharacterCold worker, Inventory counter, string item, int count)
    {
        int moved = 0;
        while (moved < count && CargoStacks(worker, item).FirstOrDefault() is { Item: { } stack, Owner: { } from } && MoveUnit(stack, from, counter))
            moved++;
        return moved;
    }

    /// <summary>
    /// One unit of <paramref name="stack"/> from <paramref name="from"/> into <paramref name="into"/> when it has room for it; a unit that still
    /// does not go in is put back (the original's rollback).
    /// </summary>
    static bool MoveUnit(ItemInstance stack, Inventory from, Inventory into)
    {
        var unit = stack.WithQuantity(1);
        if (!into.HasRoomFor(unit) || !from.RemoveOne(stack)) return false;
        if (into.Add(unit)) return true;

        from.Add(unit);
        return false;
    }
}
