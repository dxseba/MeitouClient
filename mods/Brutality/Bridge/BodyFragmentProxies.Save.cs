using System.Numerics;
using System.Text.Json;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Brutality;

public sealed partial class BodyFragmentProxySystem
{
    public sealed record SavedHalf(string Owner, int StableKey, string? Carrier, float X, float Y, float Z, float Yaw);
    sealed record Document(int SchemaVersion, SavedHalf[] Halves, SavedHalfQueue[]? Queues = null);
    readonly Dictionary<string, SavedHalf> pending = new(StringComparer.Ordinal);
    static readonly System.Reflection.MethodInfo link = AccessTools.Method(typeof(CarrySystem), "Link");
    void Restore(World value, BrutalityGameplaySystem gameplay)
    {
        foreach (var (key, saved) in pending.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray())
        {
            if (gameplay.ResolveSaveKey(value, key) is not { } owner || !value.Characters.TryResolveNext(owner, out int ownerSlot)
                || value.Characters.Cold(ownerSlot) is not { Medical.Dead: true, Race: not null } source) continue;
            bool hasCut = gameplay.DurableFor(source, key)?.Torso is { IsValid: true }
                || gameplay.ActorViews.Any(actor => actor.Character == owner && actor.Torso is { IsValid: true });
            if (!hasCut) continue;
            if (Ensure(value, owner, source, new(new(saved.X, saved.Y, saved.Z), saved.Yaw)) is not { } half) continue;
            if (saved.Carrier is null) { pending.Remove(key); continue; }
            if (gameplay.ResolveSaveKey(value, saved.Carrier) is not { } carrier || !value.Characters.TryResolveNext(carrier, out int carrierSlot)) continue;
            var holder = value.Characters.Cold(carrierSlot)!;
            if (holder.Carrying == half.Proxy && half.Cold.CarriedBy == carrier) { pending.Remove(key); continue; }
            // An already active new carry is authoritative; never replace it with an older deferred save link.
            if (!holder.Carrying.IsNone || !half.Cold.CarriedBy.IsNone || !CarrySystem.CanCarry(value, holder, ordered: true)) { pending.Remove(key); continue; }
            link.Invoke(null, [value.Characters, carrierSlot, half.Proxy.Slot]); pending.Remove(key);
        }
    }
    SavedHalf? Row(Half half)
    {
        if (world is not { } value || BrutalityRuntime.For(value) is not { } gameplay || gameplay.SaveKeyOf(half.Source) is not { } ownerKey) return null;
        var placement = half.LastPlacement; string? carrier = half.LastCarrier;
        if (value.Characters.TryResolveNext(half.Proxy, out int slot))
        {
            ref readonly var hot = ref value.Characters.Next[slot]; placement = new(hot.Position, hot.Yaw);
            carrier = value.Characters.TryResolveNext(half.Cold.CarriedBy, out int carriedBy) && value.Characters.Cold(carriedBy) is { } holder ? gameplay.SaveKeyOf(holder) : null;
        }
        if (carrier is null && pending.TryGetValue(ownerKey, out var deferred)) carrier = deferred.Carrier;
        return new(ownerKey, 1, carrier, placement.Position.X, placement.Position.Y, placement.Position.Z, placement.Yaw);
    }
    internal void ImportLegacy(SavedHalf row) => pending.TryAdd(row.Owner, row);
    void Remember(Half half)
    {
        if (Row(half) is { } row) pending[row.Owner] = row;
    }
    public void Write(Stream stream)
    {
        var saved = pending.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        foreach (var half in halves.Values) if (Row(half) is { } row) saved[row.Owner] = row;
        JsonSerializer.Serialize(stream, new Document(1, saved.Values.OrderBy(row => row.Owner, StringComparer.Ordinal).ToArray(), SavedQueues()));
    }
    public void Read(Stream stream)
    {
        pending.Clear(); pendingQueues.Clear(); rememberedQueues.Clear(); history.Clear(); frames = [];
        try
        {
            if (JsonSerializer.Deserialize<Document>(stream) is not { SchemaVersion: 1, Halves: { Length: <= 100000 } rows } document) return;
            if (document.Queues is { Length: <= 100000 } queues) foreach (var queue in queues.Where(ValidQueue)) pendingQueues[queue.Owner] = queue;
            foreach (var row in rows)
                if (row is not null && row.StableKey == 1 && row.Owner is { Length: > 0 and <= 256 }
                    && row.Carrier is null or { Length: > 0 and <= 256 } && float.IsFinite(row.X) && float.IsFinite(row.Y) && float.IsFinite(row.Z) && float.IsFinite(row.Yaw))
                    pending[row.Owner] = row;
        }
        catch (JsonException) { }
    }
    public void Hash(ref StateHasher hash)
    {
        HashQueues(ref hash);
        if (halves.Count == 0 && pending.Count == 0) return;
        hash.Add(0x42505258); hash.Add(halves.Count);
        foreach (var (owner, half) in halves.OrderBy(row => Rng.Key(row.Key)))
        {
            hash.Add(owner); hash.Add(half.Proxy); hash.Add(half.RestoredPlacement.HasValue);
            if (half.RestoredPlacement is { } at) { hash.Add(at.Position); hash.Add(at.Yaw); }
        }
        hash.Add(pending.Count);
        foreach (var (owner, saved) in pending.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            hash.Add(Rng.StableHash(owner)); hash.Add(saved.Carrier is not null); if (saved.Carrier is { } carrier) hash.Add(Rng.StableHash(carrier));
            hash.Add(new Vector3(saved.X, saved.Y, saved.Z)); hash.Add(saved.Yaw);
        }
    }
}
