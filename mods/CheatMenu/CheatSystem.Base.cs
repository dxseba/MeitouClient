using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Production;

namespace Meitou.CheatMenu;

/// <summary>Fill storages, Grow farms, Ruin buildings or Repair ruins round <paramref name="At"/> (the camera's point).</summary>
public sealed record BaseDeed(BaseDeedKind Which, Vector3 At) : SimCommand, IPausedCommand;

/// <summary>The Base page's one-shot buttons the system applies.</summary>
public enum BaseDeedKind
{
    FillStorages,
    GrowFarms,
    Ruin,
    Repair,
}

/// <summary>
/// Base and research cheats in the tick (<b>Verified</b> against the original's functions, README "Facts"):
/// <list type="bullet">
/// <item>Free construction: once a second every unfinished site of the player's within 1000 of the camera has each material delivered in full (the
/// original's <c>FUN_1800037d0(false, 1000)</c>; Finish buildings is the same at 300 that also completes them);</item>
/// <item>Generators: the player's generators keep a unit of fuel in each fuel input (their burn is skipped by a patch);</item>
/// <item>Fill storages near camera: the player's storages within 300 that name their goods are filled with them until full;</item>
/// <item>Grow farms near camera: the player's complete farms within 300 skip one growth time, water ignored (<c>FarmBuilding::timeSkip</c>);</item>
/// <item>Ruin buildings / Repair ruins near camera: every building within 150 (the original's radius for these two) is made a ruin or whole.</item>
/// </list>
/// </summary>
internal sealed partial class CheatSystem
{
    const float FreeBuildRadius = 1000, StorageRadius = 300, FarmRadius = 300, RuinRadius = 150;
    const double FreeBuildSeconds = 1;

    /// <summary>The most stacks one fill adds to a storage (a bound on the loop; a storage fills long before).</summary>
    const int MaxStacks = 1000;

    bool BaseInputs(World world, SimCommand command)
    {
        if (command is not BaseDeed deed) return false;

        _camera = deed.At;
        int count = deed.Which switch
        {
            BaseDeedKind.FillStorages => FillStorages(world, deed.At),
            BaseDeedKind.GrowFarms => GrowFarms(world, deed.At),
            BaseDeedKind.Ruin => BaseWorld.SetRuins(world, data, deed.At, RuinRadius, ruin: true),
            _ => BaseWorld.SetRuins(world, data, deed.At, RuinRadius, ruin: false),
        };
        world.Post(Said(deed.Which, count));
        return false;
    }

    static string Said(BaseDeedKind which, int count) => which switch
    {
        BaseDeedKind.FillStorages => $"CheatMenu: filled {count} storage slots",
        BaseDeedKind.GrowFarms => $"CheatMenu: grew {count} farms",
        BaseDeedKind.Ruin => $"CheatMenu: ruined {count} buildings",
        _ => $"CheatMenu: repaired {count} buildings",
    };

    void BaseUpkeep(World world)
    {
        if (On(Toggle.FreeConstruction) && Every(world, FreeBuildSeconds)) DeliverAll(world);
        if (On(Toggle.Generators)) KeepFuel(world);
    }

    /// <summary>The player's site a machine stands in, when it is complete; null otherwise.</summary>
    static BuildSite? OwnSite(World world, Machine m) =>
        world.Buildings.Find(m.Site) is { Complete: true } site && site.Owner == world.Player.Faction ? site : null;

    void DeliverAll(World world)
    {
        foreach (var site in world.Buildings.All)
        {
            if (site.Complete || site.Owner != world.Player.Faction || Vector3.Distance(site.Position, _camera) > FreeBuildRadius) continue;

            foreach (var slot in site.Materials) slot.Delivered = slot.Required;
            world.Buildings.Changed();
        }
    }

    static void KeepFuel(World world)
    {
        foreach (var m in world.Machines.All)
        {
            if (!m.Data.IsGenerator || m.Inputs.Length == 0 || OwnSite(world, m) is null) continue;

            foreach (var input in m.Inputs) input.Level = MathF.Max(input.Level, 1);
        }
    }

    /// <summary>Fills each of the player's storages near the point that names its goods (a resource storage) with them.</summary>
    int FillStorages(World world, Vector3 at)
    {
        int filled = 0;
        foreach (var m in world.Machines.All)
        {
            if (!m.Data.IsStorage || m.Storage is not { } store || m.Data.StorageLimit.Count == 0) continue;
            if (OwnSite(world, m) is not { } site || Vector3.Distance(site.Position, at) > StorageRadius) continue;

            foreach (string item in m.Data.StorageLimit) filled += Fill(store, item) ? 1 : 0;
        }

        return filled;
    }

    bool Fill(Simulation.Items.Inventory store, string item)
    {
        bool added = false;
        for (int i = 0; i < MaxStacks; i++)
        {
            if (data.Items.Create(item) is not { } one) break;

            one.Quantity = Math.Max(1, store.LimitOf(one));
            if (!store.Add(one)) break;

            added = true;
        }

        return added;
    }

    int GrowFarms(World world, Vector3 at)
    {
        if (world.Systems.OfType<ProductionSystem>().FirstOrDefault() is not { } production) return 0;

        int grown = 0;
        foreach (var m in world.Machines.All)
        {
            if (m.Farm is null || m.Data.Farm is not { } farm) continue;
            if (OwnSite(world, m) is not { } site || Vector3.Distance(site.Position, at) > FarmRadius) continue;

            float hours = farm.GrowthTime / MathF.Max(m.Yield, 1e-6f);
            if (BaseWorld.TimeSkip(production, world, m, hours)) grown++;
        }

        return grown;
    }
}
