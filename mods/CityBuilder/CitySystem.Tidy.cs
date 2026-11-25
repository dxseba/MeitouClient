using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Items;
using Meitou.Simulation.Production;

namespace Meitou.CityBuilder;

/// <summary>
/// Tidy (City.ini [Tidy], the original's merged Tidy mod): idle people of the player's own squads carry what lies on the ground in a settlement
/// (an outpost with a CITY building, its radius plus Reach) into its storages: only ones with room that take the item (a storage for one kind
/// of thing takes only that kind). At most Haulers at a time; idle is a living, led character of the player's with no task and not fighting.
/// Left on the ground: other factions' items (a theft), empty bottles and cans, foul and rotten food, severed limbs (by name) and the Junk ids.
/// A hauler that cannot reach a storage, or finds none with room, keeps the item in its pack (the original's messages). Jobs are not saved: an
/// item being carried at a save stays in the pack.
/// </summary>
internal sealed partial class CitySystem
{
    /// <summary>Ticks a pick-up or a delivery may take before the hauler gives up (60 s and 100 s of game time).</summary>
    const long PickTimeout = 1800, CarryTimeout = 3000;

    /// <summary>How near the storage a hauler puts the item in.</summary>
    const float StoreReach = 60;

    sealed class Haul
    {
        public required int Ground;
        public required ItemInstance Item;
        public string? Storage;
        public long Since;
    }

    readonly SortedDictionary<int, (CharacterId Hauler, Haul Job)> _hauls = [];

    void Tidy(World world)
    {
        if (!_rules.Tidy)
        {
            _hauls.Clear();
            return;
        }

        StepHauls(world);
        foreach (var town in Settlements(world))
        {
            if (_hauls.Count >= _rules.Haulers) break;

            StartHauls(world, town.Position, town.Radius + _rules.Reach);
        }
    }

    /// <summary>The player's outposts holding a CITY building the mod looks after.</summary>
    List<PlayerTown> Settlements(World world)
    {
        var numbers = new SortedSet<int>();
        foreach (var place in _places.Values)
        {
            if (world.Buildings.Find(place.Site) is { Town: > 0 } site) numbers.Add(site.Town);
        }

        return [.. numbers.Select(world.Buildings.FindTown).OfType<PlayerTown>().Where(t => t.Owner == world.Player.Faction)];
    }

    #region Jobs

    void StepHauls(World world)
    {
        foreach (var (slotKey, (hauler, job)) in _hauls.ToList())
        {
            if (!world.Characters.TryResolveNext(hauler, out int slot) || !world.Characters.Next[slot].Alive
                || world.Characters.Cold(slot) is not { Inventory: { } pack } cold)
            {
                _hauls.Remove(slotKey);
                continue;
            }

            if (!Advance(world, slot, cold, pack, job)) _hauls.Remove(slotKey);
        }
    }

    /// <summary>One sweep of a job; false when it is over (done, given up or the item gone).</summary>
    bool Advance(World world, int slot, CharacterCold cold, Inventory pack, Haul job)
    {
        if (job.Storage is null)
        {
            if (world.GroundItems.Find(job.Ground) is not null) return world.Tick - job.Since < PickTimeout;
            if (!pack.Items.Contains(job.Item)) return false;

            if (StorageFor(world, job.Item, world.Characters.Next[slot].Position) is not { } storage) return false;

            job.Storage = storage.Site;
            job.Since = world.Tick;
            _enqueue(new MoveOrder([world.Characters.IdOf(slot)], storage.Position) { Tick = world.Tick + 1 });
            return true;
        }

        if (world.Machines.Find(job.Storage) is not { Storage: { } shelf } machine || !pack.Items.Contains(job.Item)) return false;
        if (Vector3.Distance(world.Characters.Next[slot].Position, machine.Position) > StoreReach) return world.Tick - job.Since < CarryTimeout;

        pack.Remove(job.Item);
        if (shelf.Add(job.Item))
        {
            world.Machines.Touch();
            return false;
        }

        pack.Add(job.Item);
        return false;
    }

    /// <summary>The nearest complete storage of the player's that takes the item and has its grid, within the settlement scan radius.</summary>
    Machine? StorageFor(World world, ItemInstance item, Vector3 from)
    {
        Machine? best = null;
        float bestDistance = _rules.ScanRadius * (float)_rules.ScanRadius;
        foreach (var m in world.Machines.All)
        {
            if (!m.Data.IsStorage || m.Storage is null || !m.Data.Accepts(item.Record, item.Type)) continue;
            if (world.Buildings.Find(m.Site) is not { Complete: true } site || site.Owner != world.Player.Faction) continue;

            float d = Vector3.DistanceSquared(m.Position, from);
            if (d < bestDistance) (best, bestDistance) = (m, d);
        }

        return best;
    }

    void StartHauls(World world, Vector3 centre, float radius)
    {
        var claimed = _hauls.Values.Select(h => h.Job.Ground).ToHashSet();
        foreach (var ground in world.GroundItems.All)
        {
            if (_hauls.Count >= _rules.Haulers) return;
            if (claimed.Contains(ground.Id) || !Loose(world, ground) || Flat(ground.Position, centre) > radius) continue;
            if (StorageFor(world, ground.Item, ground.Position) is null) continue;
            if (IdleHauler(world, ground.Position, centre, radius) is not { } slot) continue;

            var id = world.Characters.IdOf(slot);
            _hauls[slot] = (id, new Haul { Ground = ground.Id, Item = ground.Item, Since = world.Tick });
            _enqueue(new PickUpItemOrder([id], ground.Id) { Tick = world.Tick + 1 });
        }
    }

    /// <summary>An item to tidy: not another faction's, not junk.</summary>
    bool Loose(World world, GroundItem ground)
    {
        if (ground.OwnedBy >= 0 && ground.OwnedBy != world.Player.Faction) return false;

        string name = ground.Item.Name;
        bool junk = name.StartsWith("Empty", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Rotten", StringComparison.OrdinalIgnoreCase) || name.Contains("Foul", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Severed", StringComparison.OrdinalIgnoreCase) || _rules.Junk.Contains(ground.Item.Record);
        return !junk;
    }

    /// <summary>The idle player character in the settlement nearest the item; null for none.</summary>
    int? IdleHauler(World world, Vector3 at, Vector3 centre, float radius)
    {
        var table = world.Characters;
        int? best = null;
        float bestDistance = float.MaxValue;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (_hauls.ContainsKey(slot) || !table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold) continue;
            if (cold.Running is not null || cold.InCombat || cold.Inventory is null) continue;
            if (world.Squads.Find(cold.SquadId) is not { Independent: false } squad || squad.ModTags.ContainsKey(_modId)) continue;

            var p = table.Next[slot].Position;
            if (Flat(p, centre) > radius) continue;

            float d = Flat(p, at);
            if (d < bestDistance) (best, bestDistance) = (slot, d);
        }

        return best;
    }

    static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    #endregion

    void HashTidy(ref StateHasher hasher)
    {
        hasher.Add(_hauls.Count);
        foreach (var (slot, (hauler, job)) in _hauls)
        {
            hasher.Add(slot);
            hasher.Add(hauler);
            hasher.Add(job.Ground);
            hasher.Add(Rng.StableHash(job.Storage ?? ""));
            hasher.Add(job.Since);
        }
    }

    static void WriteTidy(BinaryWriter w) => w.Write(0);

    void ReadTidy(BinaryReader r)
    {
        r.ReadInt32();
        _hauls.Clear();
    }
}
