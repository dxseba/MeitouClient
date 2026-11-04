using System.Text.Json;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Saving;
using Meitou.Data.Gameplay.Ai;

namespace Meitou.Brutality;

public sealed partial class IncapacitatedCombatAdapter
{
    sealed record RememberedOrder(int Index, CharacterCold Target, string? Part);
    public sealed record SavedFocusedOrder(int Index, string Target, string? Part);
    public sealed record SavedFocusedQueue(string Owner, SavedFocusedOrder[] Orders);
    sealed record Document(int SchemaVersion, SavedFocusedQueue[] Queues);
    readonly Dictionary<string, SavedFocusedQueue> pending = new(StringComparer.Ordinal);

    void Remember(World value)
    {
        for (int slot = 0; slot < value.Characters.HighWater; slot++)
        {
            if (!value.Characters.Next[slot].Alive || value.Characters.Cold(slot) is not { } cold) continue;
            var keep = new List<RememberedOrder>();
            for (int index = 0; index < cold.OrderQueue.Count; index++)
                if (cold.OrderQueue[index] is { Detail: Focus focus } order && ReferenceEquals(order.Owner, this)
                    && value.Characters.TryResolveNext(order.Target, out int target) && value.Characters.Cold(target) is { } victim
                    && victim.Medical is { Dead: false }) keep.Add(new(index, victim, focus.Part));
            if (keep.Count == 0) remembered.Remove(cold);
            else remembered[cold] = keep;
        }
    }
    void RestorePending(World value)
    {
        if (BrutalityRuntime.For(value) is not { } gameplay) return;
        foreach (var (key, saved) in pending.ToArray())
        {
            if (gameplay.ResolveSaveKey(value, key) is not { } owner || !value.Characters.TryResolveNext(owner, out int slot)) continue;
            var resolved = new List<(int Index, OrderEntry Order)>(); bool deferred = false;
            foreach (var entry in saved.Orders)
            {
                if (gameplay.ResolveSaveKey(value, entry.Target) is not { } target) { deferred = true; break; }
                resolved.Add((entry.Index, new OrderEntry(TaskType.FOCUSED_MELEE_ATTACK, this) { Target = target, Detail = new Focus(entry.Part) }));
            }
            if (deferred) continue;
            var cold = value.Characters.Cold(slot)!;
            if (cold.SavedOrders is not null) HarmonyLib.AccessTools.Method(typeof(OrderSave), "Restore").Invoke(null, [value, slot, cold]);
            var merged = cold.OrderQueue.Where(order => !ReferenceEquals(order.Owner, this)).ToList();
            foreach (var entry in resolved.OrderBy(row => row.Index)) merged.Insert(Math.Min(entry.Index, merged.Count), entry.Order);
            cold.OrderQueue.Clear();
            foreach (var entry in merged) PlayerOrders.Give(value, slot, entry, queued: cold.OrderQueue.Count > 0, show: false);
            pending.Remove(key);
        }
    }
    public void Write(Stream stream)
    {
        var rows = pending.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        if (world is { } value && BrutalityRuntime.For(value) is { } gameplay)
        {
            Remember(value);
            foreach (var (cold, orders) in remembered)
            {
                if (gameplay.SaveKeyOf(cold) is not { } owner) continue;
                var saved = orders.Select(entry => gameplay.SaveKeyOf(entry.Target) is { } target ? new SavedFocusedOrder(entry.Index, target, entry.Part) : null)
                    .Where(entry => entry is not null).Cast<SavedFocusedOrder>().ToArray();
                if (saved.Length > 0) rows[owner] = new(owner, saved);
            }
        }
        JsonSerializer.Serialize(stream, new Document(1, rows.Values.OrderBy(row => row.Owner, StringComparer.Ordinal).ToArray()));
    }
    public void Read(Stream stream)
    {
        pending.Clear();
        try
        {
            if (JsonSerializer.Deserialize<Document>(stream) is not { SchemaVersion: 1, Queues: { } rows }) return;
            foreach (var row in rows)
                if (row is not null && row.Owner is { Length: > 0 and <= 256 } && row.Orders is { Length: > 0 and <= 512 }
                    && row.Orders.All(entry => entry is not null && entry.Index is >= 0 and <= 4096 && entry.Target is { Length: > 0 and <= 256 }
                        && entry.Part is null or { Length: > 0 and <= 256 })) pending[row.Owner] = row;
        }
        catch (JsonException) { }
    }
    void HashPending(ref StateHasher hash)
    {
        if (pending.Count == 0) return;
        hash.Add(0x47545051); hash.Add(pending.Count);
        foreach (var (key, queue) in pending.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            hash.Add(Rng.StableHash(key)); hash.Add(queue.Orders.Length);
            foreach (var entry in queue.Orders) { hash.Add(entry.Index); hash.Add(Rng.StableHash(entry.Target)); hash.Add(entry.Part is not null); if (entry.Part is { } part) hash.Add(Rng.StableHash(part)); }
        }
    }
}
