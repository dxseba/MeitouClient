using System.Globalization;
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

public sealed partial class WeaponBloodState
{
    World? hashWorld;
    internal void BindWorld(World world, ulong? restoredSeed = null)
    {
        hashWorld = world;
        if (namespaceInitialized) return;
        ulong seed = restoredSeed ?? world.Seed;
        ulong domain = Rng.StableHash("brutality-item:v1");
        ulong generation = 0;
        do
        {
            containerNamespace = Rng.Hash(seed, domain, purpose, generation * 2).ToString("x16", CultureInfo.InvariantCulture)
                + Rng.Hash(seed, domain, purpose, generation * 2 + 1).ToString("x16", CultureInfo.InvariantCulture);
            generation++;
        }
        while (reservedNamespaces.Contains(containerNamespace));
        namespaceInitialized = true;
    }

    // Render revisions and reference identities are not semantic state. Inventory paths distinguish
    // identical unsaved weapons; opaque saved keys cover lazy/unmaterialized container contents.
    public void Hash(ref StateHasher hash)
    {
        hash.Add(namespaceInitialized); hash.Add(Rng.StableHash(containerNamespace)); hash.Add(containerSerial);
        hash.Add(authoritativeSidecar); hash.Add(durable.Count);
        foreach (var (key, blood) in durable.OrderBy(row => row.Key, StringComparer.Ordinal))
        { hash.Add(Rng.StableHash(key)); HashBlood(ref hash, blood); }
        if (!stains.Values.Any(stain => stain.Key is null)) { hash.Add(0); return; }
        var paths = new Dictionary<ItemInstance, string>(ReferenceEqualityComparer.Instance);
        if (hashWorld is { } world)
        {
            for (int slot = 0; slot < world.Characters.HighWater; slot++)
                if (world.Characters.Cold(slot)?.Inventory is { } inventory)
                    InventoryPaths(inventory, FormattableString.Invariant($"character:{Rng.Key(world.Characters.IdOf(slot)):x16}"), paths, 0);
            foreach (var ground in world.GroundItems.All.OrderBy(item => item.Id))
            {
                string path = FormattableString.Invariant($"ground:{ground.Id}");
                paths.TryAdd(ground.Item, path);
                if (ground.Item.Contents is { } bag) InventoryPaths(bag, path, paths, 0);
            }
        }
        var live = stains.Where(row => row.Value.Key is null).Select(row =>
            (Path: paths.GetValueOrDefault(row.Key, "unresolved"), row.Key.Record, row.Value.Blood))
            .OrderBy(row => row.Path, StringComparer.Ordinal).ThenBy(row => row.Record, StringComparer.Ordinal)
            .ThenBy(row => row.Blood.X).ThenBy(row => row.Blood.Y).ThenBy(row => row.Blood.Z).ThenBy(row => row.Blood.W).ToArray();
        hash.Add(live.Length);
        foreach (var row in live)
        { hash.Add(Rng.StableHash(row.Path)); hash.Add(Rng.StableHash(row.Record)); HashBlood(ref hash, row.Blood); }
    }
    static void HashBlood(ref StateHasher hash, Vector4 blood)
    { hash.Add(blood.X); hash.Add(blood.Y); hash.Add(blood.Z); hash.Add(blood.W); }
    static void InventoryPaths(Inventory inventory, string root, Dictionary<ItemInstance, string> paths, int depth)
    {
        if (depth > 32) return;
        for (int index = 0; index < inventory.Items.Count; index++)
        {
            var item = inventory.Items[index]; string path = FormattableString.Invariant($"{root}/{index}");
            if (!paths.TryAdd(item, path)) continue;
            if (item.Contents is { } bag) InventoryPaths(bag, path, paths, depth + 1);
        }
    }
}
