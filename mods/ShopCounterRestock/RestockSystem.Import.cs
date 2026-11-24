using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using static Meitou.ShopCounterRestock.ShopCounterRestockMod;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// After a load, the counters an original-plugin save holds (<see cref="OriginalSave"/>) that the port's own data lacks: their whitelists,
/// targets and switches, and the saved worker's job when it has none and the counter has no worker yet. Read once per load, serially.
/// </summary>
internal sealed partial class RestockSystem : IWorldReady
{
    public void Ready(World world, bool fromSave)
    {
        if (!fromSave || world.Start.SaveFolder is not { } folder) return;

        try
        {
            Import(world, folder);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            Log?.Invoke($"the original plugin's counters were not read: {e.Message}");
        }
    }

    /// <summary>
    /// The mod's log, set by the mod.
    /// </summary>
    internal Action<string>? Log { get; init; }

    void Import(World world, string folder)
    {
        var saved = OriginalSave.Read(folder);
        if (saved.Count == 0) return;

        var sites = OriginalSave.SiteIds(folder, saved.Select(s => s.Counter).ToHashSet());
        int taken = 0;
        foreach (var counter in saved)
        {
            if (!sites.TryGetValue(counter.Counter, out var site) || _counters.ContainsKey(site) || world.Buildings.Find(site) is null) continue;

            Adopt(world, site, counter);
            taken++;
        }

        Log?.Invoke($"original plugin counters imported: {taken} of {saved.Count}");
    }

    void Adopt(World world, string site, SavedCounter saved)
    {
        var config = ConfigOf(site);
        foreach (var (item, target) in saved.Items)
        {
            if (target is >= 1 and <= CounterConfig.MaxTarget) config.Set(item, target);
        }

        config.Enabled = saved.Enabled;
        config.Status = "Restock restored from save.";
        if (saved.Worker.IsNull || WorkerOf(site) is not null) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold || cold.Save?.Source.Handle != saved.Worker) continue;
            if (JobCounter(cold) is null) Give(world, slot, cold, site);

            return;
        }
    }
}
