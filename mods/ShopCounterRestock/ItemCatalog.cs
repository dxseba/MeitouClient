using Meitou.Data;
using Meitou.Data.Fcs;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// An item the whitelist may name: its record string id and shown name.
/// </summary>
internal readonly record struct CatalogItem(string Id, string Name);

/// <summary>
/// The items the settings window searches (the original's eight record types: ITEM, WEAPON, ARMOUR, CONTAINER, CROSSBOW, LIMB_REPLACEMENT,
/// BLUEPRINT, MAP_ITEM), by name then id, read once from the merged data. Search is case-insensitive on the name or the string id.
/// </summary>
internal sealed class ItemCatalog
{
    static readonly FcsRecordType[] Types =
    [
        FcsRecordType.Item, FcsRecordType.Weapon, FcsRecordType.Armour, FcsRecordType.Container, FcsRecordType.Crossbow,
        FcsRecordType.LimbReplacement, FcsRecordType.Blueprint, FcsRecordType.MapItem,
    ];

    readonly List<CatalogItem> _items;
    readonly Dictionary<string, string> _names;

    public ItemCatalog(GameDatabase data)
    {
        _items = [.. Types.SelectMany(data.OfType).Select(r => new CatalogItem(r.StringId, r.Name))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id, StringComparer.Ordinal)];
        _names = _items.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
    }

    public int Count => _items.Count;

    /// <summary>
    /// Whether the data defines the item (a saved whitelist entry whose mod was removed is kept but shown as missing).
    /// </summary>
    public bool Knows(string id) => _names.ContainsKey(id);

    /// <summary>
    /// The item's shown name, "Missing item" when the data lacks it.
    /// </summary>
    public string NameOf(string id) => _names.GetValueOrDefault(id) ?? "Missing item";

    /// <summary>
    /// The first <paramref name="limit"/> items whose name or id holds <paramref name="text"/> (ignoring case), and how many match.
    /// </summary>
    public (List<CatalogItem> Shown, int Found) Search(string text, int limit)
    {
        string query = text.Trim();
        if (query.Length == 0) return ([], 0);

        var found = _items.Where(i => Matches(i, query)).ToList();
        return ([.. found.Take(limit)], found.Count);
    }

    static bool Matches(CatalogItem item, string query) =>
        item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase);
}
