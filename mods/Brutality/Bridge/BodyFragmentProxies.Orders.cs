using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

public sealed partial class BodyFragmentProxySystem
{
    public sealed record SavedHalfOrder(int Index, string Target, int StableKey, int Row, int Kind, float X, float Y, float Z);
    public sealed record SavedHalfQueue(string Owner, SavedHalfOrder[] Orders);
    sealed record RememberedHalfOrder(int Index, CharacterCold Source, int StableKey, TaskType Row, int Kind, Vector3 Point);
    sealed class RememberedHalfQueue { public RememberedHalfOrder[] Orders = []; }
    readonly ConditionalWeakTable<CharacterCold, RememberedHalfQueue> rememberedQueues = new();
    readonly Dictionary<string, SavedHalfQueue> pendingQueues = new(StringComparer.Ordinal);
    bool restoringQueue;
    sealed class CapturedKey(string value) { public string Value = value; }
    readonly ConditionalWeakTable<CharacterCold, CapturedKey> capturedKeys = new();
    internal void CapturedSaveIdentity(CharacterCold cold, Hand handle)
    {
        if (SaveIdentityAdapter.KeyFor(handle) is not { } key) return;
        capturedKeys.TryGetValue(cold, out var captured);
        string? before = captured?.Value ?? (cold.Save is { } saved ? SaveIdentityAdapter.KeyFor(saved.Source.Handle) : null);
        if (captured is null) capturedKeys.Add(cold, new(key)); else captured.Value = key;
        if (before is null || before == key) return;
        if (pending.Remove(before, out var half)) pending[key] = half with { Owner = key };
        foreach (var (owner, row) in pending.ToArray()) if (row.Carrier == before) pending[owner] = row with { Carrier = key };
        if (pendingQueues.Remove(before, out var queue)) pendingQueues[key] = queue with { Owner = key };
        foreach (var (owner, row) in pendingQueues.ToArray())
            if (row.Orders.Any(order => order.Target == before)) pendingQueues[owner] = row with
            { Orders = row.Orders.Select(order => order.Target == before ? order with { Target = key } : order).ToArray() };
        foreach (var active in halves.Values) if (active.LastCarrier == before) active.LastCarrier = key;
    }
    internal static void InstallOrderPersistence(Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(OrderSave), "Restore"),
        prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(BeforeNativeOrderRestore)));
    static bool BeforeNativeOrderRestore(World world, CharacterCold cold) => ForWorld(world) is not { } proxies || !proxies.BlocksRestore(world, cold);
    bool BlocksRestore(World value, CharacterCold cold) => !restoringQueue && BrutalityRuntime.For(value)?.SaveKeyOf(cold) is { } key && pendingQueues.ContainsKey(key);
    internal void BindSaveWorld(World value) => Bind(value);
    internal static bool HalfOrder(TaskType row) => row is TaskType.LIFT_PERSON_PLAYER_ORDER or TaskType.KIDNAP_ORDER or TaskType.LOOT_TARGET or TaskType.PUT_DOWN_OBJECT;
    static bool ValidQueue(SavedHalfQueue? queue) => queue is { Owner.Length: > 0 and <= 256, Orders.Length: > 0 and <= 512 }
        && queue.Orders.All(row => row is not null && row.Index is >= 0 and <= 4096 && row.Target is { Length: > 0 and <= 256 }
            && row.StableKey is 0 or 1 && HalfOrder((TaskType)row.Row) && row.Kind is 0 or 1
            && float.IsFinite(row.X) && float.IsFinite(row.Y) && float.IsFinite(row.Z))
        && queue.Orders.Select(row => row.Index).Distinct().Count() == queue.Orders.Length;
    internal void ImportLegacyQueue(SavedHalfQueue queue) { if (ValidQueue(queue)) pendingQueues.TryAdd(queue.Owner, queue); }
    internal static IEnumerable<string> ReadQueuedTargets(byte[] bytes)
    {
        Document? document;
        try { document = JsonSerializer.Deserialize<Document>(bytes); } catch (JsonException) { return []; }
        return document is { SchemaVersion: 1, Queues: { Length: <= 100000 } queues }
            ? queues.Where(ValidQueue).SelectMany(queue => queue.Orders.Select(row => row.Target)).Distinct(StringComparer.Ordinal).ToArray() : [];
    }
    void RememberQueues(World value)
    {
        var byProxy = halves.Values.ToDictionary(half => half.Proxy);
        var gameplay = BrutalityRuntime.For(value);
        if (gameplay is null) return;
        for (int slot = 0; slot < value.Characters.HighWater; slot++)
        {
            if (!value.Characters.Next[slot].Alive || value.Characters.Cold(slot) is not { } cold) continue;
            // A native queue that still awaits its mod targets must survive another save unchanged.
            if (gameplay.SaveKeyOf(cold) is { } heldKey && pendingQueues.ContainsKey(heldKey))
            { if (cold.OrderQueue.Count == 0) continue; pendingQueues.Remove(heldKey); }
            var rows = new List<RememberedHalfOrder>(); int index = 0;
            foreach (var order in cold.OrderQueue)
            {
                int kind = order.Row == TaskType.None ? -1 : order.Owner.SaveKind(order);
                if (kind < 0) continue;
                if (order.Owner is CarrySystem && HalfOrder(order.Row) && kind is 0 or 1)
                {
                    CharacterCold? source = null; int stableKey = 0;
                    if (byProxy.TryGetValue(order.Target, out var half)) { source = half.Source; stableKey = 1; }
                    else if (value.Characters.TryResolveNext(order.Target, out int target) && value.Characters.Cold(target) is { } original
                        && gameplay.DurableFor(original, "queued-order")?.Torso is not null) source = original;
                    if (source is not null) rows.Add(new(index, source, stableKey, order.Row, kind, order.Point));
                }
                index++;
            }
            if (rows.Count == 0) rememberedQueues.Remove(cold);
            else rememberedQueues.GetValue(cold, static _ => new()).Orders = rows.ToArray();
        }
    }
    SavedHalfQueue[] SavedQueues()
    {
        var result = pendingQueues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (world is { } value && BrutalityRuntime.For(value) is { } gameplay)
        {
            RememberQueues(value);
            foreach (var (cold, remembered) in rememberedQueues)
            {
                if (gameplay.SaveKeyOf(cold) is not { } owner) continue;
                var rows = remembered.Orders.Select(row => gameplay.SaveKeyOf(row.Source) is { } target
                    ? new SavedHalfOrder(row.Index, target, row.StableKey, (int)row.Row, row.Kind, row.Point.X, row.Point.Y, row.Point.Z) : null)
                    .Where(row => row is not null).Cast<SavedHalfOrder>().ToArray();
                if (rows.Length > 0) result[owner] = new(owner, rows);
            }
        }
        return result.Values.OrderBy(queue => queue.Owner, StringComparer.Ordinal).ToArray();
    }
    void RestoreQueues(World value, BrutalityGameplaySystem gameplay)
    {
        foreach (var (key, saved) in pendingQueues.ToArray())
        {
            if (gameplay.ResolveSaveKey(value, key) is not { } id || !value.Characters.TryResolveNext(id, out int slot)) continue;
            var cold = value.Characters.Cold(slot)!;
            if (cold.OrderQueue.Count > 0) { pendingQueues.Remove(key); continue; } // A newer explicit command wins.
            var resolved = new List<(int Index, OrderEntry Order)>(); bool deferred = false;
            foreach (var row in saved.Orders.OrderBy(row => row.Index))
            {
                if (gameplay.ResolveSaveKey(value, row.Target) is not { } owner || !value.Characters.TryResolveNext(owner, out int targetSlot)) { deferred = true; break; }
                var target = owner;
                if (row.StableKey == 1)
                {
                    if (!halves.TryGetValue(owner, out var half)) half = Ensure(value, owner, value.Characters.Cold(targetSlot)!, null);
                    if (half is null) { deferred = true; break; } target = half.Proxy;
                }
                var task = (TaskType)row.Row;
                if (value.Systems.OfType<CarrySystem>().FirstOrDefault() is not IOrderOwner native) { deferred = true; break; }
                resolved.Add((row.Index, new OrderEntry(task, native) { Target = target, Point = new(row.X, row.Y, row.Z),
                    Detail = native.LoadDetail(value, id, task, row.Kind, target) }));
            }
            if (deferred) continue;
            var indexes = saved.Orders.Select(row => row.Index).ToHashSet();
            if (cold.SavedOrders is { } nativeRows) cold.SavedOrders = nativeRows.Where((_, index) => !indexes.Contains(index)).ToArray();
            restoringQueue = true;
            try { AccessTools.Method(typeof(OrderSave), "Restore").Invoke(null, [value, slot, cold]); }
            finally { restoringQueue = false; }
            var merged = cold.OrderQueue.ToList();
            foreach (var row in resolved) merged.Insert(Math.Min(row.Index, merged.Count), row.Order);
            cold.OrderQueue.Clear();
            foreach (var order in merged) AccessTools.Method(typeof(OrderQueue), "Add").Invoke(cold.OrderQueue, [order]);
            DecisionSystem.Request(cold); pendingQueues.Remove(key);
        }
    }
    void HashQueues(ref StateHasher hash)
    {
        if (pendingQueues.Count == 0) return;
        hash.Add(0x51505258); hash.Add(pendingQueues.Count);
        foreach (var (key, queue) in pendingQueues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(Rng.StableHash(key)); hash.Add(queue.Orders.Length);
            foreach (var row in queue.Orders)
            { hash.Add(row.Index); hash.Add(Rng.StableHash(row.Target)); hash.Add(row.StableKey); hash.Add(row.Row); hash.Add(row.Kind); hash.Add(new Vector3(row.X, row.Y, row.Z)); }
        }
    }
}
