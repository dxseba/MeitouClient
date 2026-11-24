using System.Text;
using Meitou.Simulation;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The saved state: each counter's whitelist (item string ids, kept byte for byte even when the data no longer defines one), targets and switch,
/// by the counter's site id. The worker is not saved here: its job is (<c>meitou mjob</c> with the counter's id), and the sweep finds it again.
/// Trips and cooldowns start empty after a load, as in the original.
/// </summary>
internal sealed partial class RestockSystem
{
    const int Version = 1;
    const int MaxCounters = 4096;
    const int MaxItems = 4096;

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_counters.Count);
        foreach (var (site, config) in _counters)
        {
            writer.Write(site);
            writer.Write(config.Enabled);
            writer.Write(config.Items.Count);
            foreach (var (item, target) in config.Items)
            {
                writer.Write(item);
                writer.Write(target);
            }
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) throw new InvalidDataException("Unknown ShopCounterRestock save version");

        int count = reader.ReadInt32();
        if (count is < 0 or > MaxCounters) throw new InvalidDataException("ShopCounterRestock: refusing oversized counter save list.");

        _counters.Clear();
        _assigned.Clear();
        _trips.Clear();
        _sourceCooldown.Clear();
        _settled = false;
        for (int i = 0; i < count; i++) ReadCounter(reader);
    }

    void ReadCounter(BinaryReader reader)
    {
        string site = reader.ReadString();
        var config = new CounterConfig { Enabled = reader.ReadBoolean(), Status = "Restock restored from save." };
        int items = reader.ReadInt32();
        if (items is < 0 or > MaxItems) throw new InvalidDataException("ShopCounterRestock: skipped malformed whitelist in save.");

        for (int k = 0; k < items; k++)
        {
            string item = reader.ReadString();
            int target = reader.ReadInt32();
            if (target is >= 1 and <= CounterConfig.MaxTarget) config.Set(item, target);
        }

        _counters[site] = config;
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_counters.Count);
        foreach (var (site, config) in _counters)
        {
            hasher.Add(Rng.StableHash(site));
            hasher.Add(config.Enabled);
            hasher.Add(config.Items.Count);
            foreach (var (item, target) in config.Items)
            {
                hasher.Add(Rng.StableHash(item));
                hasher.Add(target);
            }
        }

        hasher.Add(_assigned.Count);
        foreach (var (worker, counter) in _assigned.OrderBy(a => a.Key.Slot))
        {
            hasher.Add(worker);
            hasher.Add(Rng.StableHash(counter));
        }

        hasher.Add(_trips.Count);
        foreach (var (worker, trip) in _trips.OrderBy(t => t.Key.Slot))
        {
            hasher.Add(worker);
            hasher.Add(trip.Fetch);
            hasher.Add(Rng.StableHash(trip.Target));
            hasher.Add(Rng.StableHash(trip.Item));
            hasher.Add(trip.Tick);
        }

        hasher.Add(_sourceCooldown.Count);
        foreach (var (site, until) in _sourceCooldown)
        {
            hasher.Add(Rng.StableHash(site));
            hasher.Add(until);
        }
    }
}
