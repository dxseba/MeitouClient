namespace Meitou.ItemMarkers;

/// <summary>
/// How an item changed hands in the open trade window (the original asks <c>InventoryItemBase::getBuyBackGUIInfo</c> and climbs the item's
/// holders to a character: a player character's item was bought, any other sold).
/// </summary>
public enum TradeChange : byte
{
    None,
    Sold,
    Bought,
}

/// <summary>
/// What the marks of one item cell are made from, read from the cell's tooltip and the snapshot: its records, gear level, stolen state for its
/// holder, whether it is a faction's uniform, and how it changed hands in the open trade.
/// </summary>
public readonly record struct ItemFacts(string Record, string Material, string Company, int Level, bool Stolen, bool Uniform, TradeChange Trade);
