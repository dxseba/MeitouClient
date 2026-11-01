using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Saving;

/// <summary>
/// Loose items through the zone files' <c>1-itemlist</c> (docs/formats/save.md "Zone files"; docs/simulation.md "Ground items"): every listed item whose record the game data has
/// lies in <see cref="World.GroundItems"/> after a load, with its saved position and rotation; a save writes each zone's list from them again. Entries whose item the data lacks
/// (a missing mod) are written back as read.
/// </summary>
public static class GroundItemSave
{
    /// <summary>
    /// Puts the save's loose items on the ground, in zone and list order; returns how many the data could not make. <paramref name="factionOf"/> gives a faction id's index for
    /// <see cref="GroundItem.OwnedBy"/>, -1 for one that does not count (unknown, or <c>not real</c>).
    /// </summary>
    public static int Load(SaveGame save, World world, ItemFactory factory, Func<string, int>? factionOf = null)
    {
        int missing = 0;
        var handles = factionOf is null ? null : save.BuildHandles();
        foreach (var zone in save.Zones.Values)
            foreach (var g in SaveGroundItems.Read(zone))
            {
                if (SavedKit.ItemOf(g.Item, factory) is { } item)
                    world.GroundItems.Load(item, g.Instance.Position, g.Instance.Rotation, handles is null || g.Item.Source is not { } r ? -1 : OwnerOf(r, handles, factionOf!));
                else missing++;
            }
        return missing;
    }

    /// <summary>
    /// The faction an item's <c>ownedby</c> handle names (docs/game/ui-input.md "Ground item labels"): the owning character's platoon's; a set handle whose character the save no
    /// longer holds falls back to the <c>insideBuilding</c> building's <c>owner faction ID</c> (the label's fallback, used for the pick-up too: engine choice). -1: none.
    /// </summary>
    static int OwnerOf(Meitou.Data.Fcs.FcsRecord item, SaveHandles handles, Func<string, int> factionOf)
    {
        var owner = Hand.Read(item, "ownedby");
        if (owner.IsNull) return -1;
        if (handles.Character(owner) is { } c) return factionOf(c.Platoon.FactionId);
        return handles.Building(Hand.Read(item, "insideBuilding"))?.Strings.GetValueOrDefault("owner faction ID") is { Length: > 0 } f ? factionOf(f) : -1;
    }

    /// <summary>
    /// Writes each zone's list from the items lying in it (by position). A zone whose list would name the same records it does and gains no item is left as it is (so an
    /// unchanged save keeps its zone files); a zone with items and no file gets one; a file left with no record goes (as the original deletes it).
    /// </summary>
    public static void Write(SaveGame game, World world, ItemFactory factory)
    {
        var byZone = new Dictionary<ZoneCoordinate, List<GroundItem>>();
        foreach (var g in world.GroundItems.All)
        {
            var z = WorldLayout.ZoneOf(g.Position.X, g.Position.Z);
            if (!byZone.TryGetValue(z, out var list)) byZone[z] = list = [];
            list.Add(g);
        }
        foreach (var z in game.Zones.Keys.Where(z => game.Zones[z].ItemList is not null).Concat(byZone.Keys).Distinct().ToList())
        {
            var items = byZone.GetValueOrDefault(z) ?? [];
            game.Zones.TryGetValue(z, out var zone);
            var old = zone is null ? [] : SaveGroundItems.Read(zone);
            var kept = old.Where(o => factory.Create(o.Item.BaseId) is null).ToList();
            var oldBlood = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            foreach (var entry in old) oldBlood.TryAdd(entry.Record.StringId, entry.Item.WeaponBlood);
            if (zone is not null && items.All(i => i.Item.Saved is { } s && oldBlood.TryGetValue(s.StringId, out var blood)
                && blood == new SaveItem { BaseId = i.Item.Record, WeaponBlood = i.Item.WeaponBlood }.WeaponBlood) && items.Count + kept.Count == old.Count) continue;
            if (zone is null) game.Zones[z] = zone = new SaveZone(z, new SaveFile());
            SaveGroundItems.Write(zone, [.. items.Select(i => (i.Position, i.Rotation, SavedKit.FromItem(i.Item)))], kept);
            if (zone.File.Records.Count == 0) game.Zones.Remove(z);
        }
    }
}
