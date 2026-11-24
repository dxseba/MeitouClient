using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Production;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// One counter's settings (saved): its whitelist in the order items were added, each with its target in units, and the restock switch; and its
/// status line (runtime, shown in the window).
/// </summary>
internal sealed class CounterConfig
{
    public const int MaxTarget = 10000;

    public List<(string Item, int Target)> Items { get; } = [];
    public bool Enabled { get; set; }
    public string Status { get; set; } = "";

    public int TargetOf(string item) => Items.FindIndex(i => i.Item == item) is var k and >= 0 ? Items[k].Target : 0;

    public void Set(string item, int target)
    {
        int k = Items.FindIndex(i => i.Item == item);
        if (k >= 0) Items[k] = (item, target);
        else Items.Add((item, target));
    }

    public void Remove(string item) => Items.RemoveAll(i => i.Item == item);
}

/// <summary>
/// A player building with storage the restock reads: its site, storage and where it stands, with the reach a worker unloads or loads within.
/// </summary>
internal sealed record Place(BuildSite Site, Machine Machine)
{
    public string Id => Site.Id;
    public Simulation.Items.Inventory Storage => Machine.Storage!;
    public Vector3 Position => Machine.Position;
    public float Reach => MathF.Max(Machine.Data.UseRange, Rules.MinReach);
}

/// <summary>
/// The original's fixed values (its DLL constants, README "Facts") and the eligibility tests of counters and storages.
/// </summary>
internal static class Rules
{
    public const int UnitsPerStep = 8;
    public const float TripSeconds = 120;
    public const float SourceCooldownSeconds = 60;
    public const float TownlessRange = 5000;
    public const float MinReach = 2;
    public const float FloorHeight = 15;

    /// <summary>
    /// The counter at <paramref name="id"/> when it is a complete, standing shop counter of the player's faction with storage.
    /// </summary>
    public static Place? Counter(World world, string? id) =>
        id is not null && Owned(world, world.Buildings.Find(id)) is { } site && world.Machines.Find(id) is
            { Data.Function: ShopCounterRestockMod.ShopFunction, Storage: not null } machine
            ? new Place(site, machine)
            : null;

    /// <summary>
    /// The player's resource and general storages (functions 2 and 13) the counter's worker draws from: in the same town or outpost, else (a
    /// counter in no town) within <see cref="TownlessRange"/> of it; in the building list's order.
    /// </summary>
    public static IEnumerable<Place> Storages(World world, Place counter)
    {
        foreach (var site in world.Buildings.All)
        {
            if (site == counter.Site || Owned(world, site) is null) continue;
            if (world.Machines.Find(site.Id) is not { Data.IsStorage: true, Storage: not null } m) continue;

            var place = new Place(site, m);
            if (SameTown(counter, place)) yield return place;
        }
    }

    static bool SameTown(Place counter, Place storage)
    {
        var a = counter.Site;
        var b = storage.Site;
        if (a.Town > 0) return b.Town == a.Town;
        if (a.ForeignTown is { } town) return Equals(town, b.ForeignTown);

        return Vector3.Distance(counter.Position, storage.Position) <= TownlessRange;
    }

    static BuildSite? Owned(World world, BuildSite? site) =>
        site is { Complete: true, Destroyed: false } && site.Owner == world.Player.Faction && world.Player.Faction >= 0 ? site : null;
}
