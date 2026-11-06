using System.Runtime.CompilerServices;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality;

public sealed partial class BrutalityGameplaySystem : IModSaveData
{
    sealed class SaveIdentity(string key)
    {
        public string Key = key;
    }
    readonly ConditionalWeakTable<CharacterCold, SaveIdentity> saveIdentities = new();
    readonly Dictionary<CharacterId, string> liveSaveKeys = [];
    readonly Dictionary<string, CharacterId> resolvedSaveKeys = new(StringComparer.Ordinal);
    long boundSpawns = -1;

    public string? SaveKeyOf(CharacterCold cold) => saveIdentities.TryGetValue(cold, out var identity) ? identity.Key
        : cold.Save is { } saved ? SaveIdentityAdapter.KeyFor(saved.Source.Handle) : null;

    public CharacterId? ResolveSaveKey(World world, string key)
    {
        if (!ReferenceEquals(saveWorld, world) || boundSpawns != world.Characters.Spawns) BindWorld(world);
        return resolvedSaveKeys.TryGetValue(key, out var id) && world.Characters.TryResolveNext(id, out int slot)
            && world.Characters.Cold(slot) is { } cold && SaveKeyOf(cold) == key ? id : null;
    }

    readonly HashSet<string> legacySeen = new(StringComparer.Ordinal);
    readonly HashSet<string> legacyKeys = new(StringComparer.Ordinal);
    readonly Dictionary<Actor, BrutalityDurableState> legacyBaselines = [];
    bool sidecarAuthoritative;
    World? saveWorld;

    internal void ImportLegacySave(World world, SaveGame save)
    {
        if (save.ModData.ContainsKey(LegacyGoreSave.ModId)) sidecarAuthoritative = true;
        if (!sidecarAuthoritative)
        {
            foreach (var state in LegacyGoreSave.Read(save))
                if (legacySeen.Add(state.Key)) { unresolved.TryAdd(state.Key, state); legacyKeys.Add(state.Key); }
            foreach (var character in save.Characters)
                if (SaveIdentityAdapter.KeyFor(character.Handle) is { } key) legacySeen.Add(key);
        }
        BindWorld(world);
    }

    /// <summary>Binds before sidecar I/O and before each tick, including NPCs materialized after loading.</summary>
    public void BindWorld(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (saveWorld is not null && !ReferenceEquals(saveWorld, world))
            throw new InvalidOperationException("A Brutality system belongs to one world.");
        saveWorld = world;
        RetainSaveState(world);
        var current = resolvedSaveKeys;
        current.Clear();
        boundSpawns = world.Characters.Spawns;
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
        {
            if (!world.Characters.Next[slot].Alive || world.Characters.Cold(slot) is not { } cold) continue;
            if (!saveIdentities.TryGetValue(cold, out var identity))
            {
                if (cold.Save is not { } saved || SaveIdentityAdapter.KeyFor(saved.Source.Handle) is not { } key) continue;
                saveIdentities.Add(cold, identity = new(key));
                if (!sidecarAuthoritative && legacySeen.Add(key) && LegacyGoreSave.Character(saved.Source) is { } legacy)
                { unresolved.TryAdd(key, legacy); legacyKeys.Add(key); }
            }
            var id = new CharacterId(slot, world.Characters.Next[slot].Generation);
            liveSaveKeys[id] = identity.Key;
            current[identity.Key] = id;
        }
        if (world.Tick > 0) legacyBaselines.Clear(); // Native Read runs before the first tick; no actor references are kept after that window.
        else
            foreach (var (key, id) in current)
                if (legacyKeys.Contains(key) && unresolved.ContainsKey(key) && Find(world, id) is { } actor)
                    legacyBaselines.TryAdd(actor, Durable(key, actor));
        ResolveDurable(world, key => current.TryGetValue(key, out var id) ? id : null);
    }

    /// <summary>Called after pending cuts are applied and before actor cleanup loses an offscreen NPC's runtime entry.</summary>
    public void RetainSaveState(World world)
    {
        foreach (var (id, key) in liveSaveKeys.ToArray())
        {
            if (world.Characters.TryResolveNext(id, out _)) continue;
            if (actors.TryGetValue(id, out var actor))
                unresolved[key] = Durable(key, actor);
            liveSaveKeys.Remove(id);
        }
    }

    internal void CaptureSaveIdentity(World world, CharacterCold cold, CharacterId id, Hand handle)
    {
        if (SaveIdentityAdapter.KeyFor(handle) is not { } key) return;
        if (saveWorld is null) BindWorld(world);
        else if (!ReferenceEquals(saveWorld, world)) throw new InvalidOperationException("A Brutality system belongs to one world.");
        saveIdentities.TryGetValue(cold, out var previous);
        string? oldKey = previous?.Key ?? (cold.Save is { } source ? SaveIdentityAdapter.KeyFor(source.Source.Handle) : null);
        BrutalityDurableState? remembered = oldKey is not null ? unresolved.GetValueOrDefault(oldKey) : null;
        if (oldKey is not null && oldKey != key) unresolved.Remove(oldKey);
        if (previous is not null) previous.Key = key;
        else saveIdentities.Add(cold, new(key));
        if (!id.IsNone && world.Characters.TryResolveNext(id, out int slot) && ReferenceEquals(world.Characters.Cold(slot), cold))
        {
            liveSaveKeys[id] = key;
            if (oldKey is not null && oldKey != key) resolvedSaveKeys.Remove(oldKey);
            resolvedSaveKeys[key] = id;
        }
        else
        {
            var stored = DurableFor(cold, key) ?? (remembered is not null ? remembered with { Key = key } : null)
                ?? (!sidecarAuthoritative && cold.Save is { } saved ? LegacyGoreSave.Character(saved.Source) is { } legacy ? legacy with { Key = key } : null : null);
            if (stored is not null) unresolved[key] = stored;
        }
    }

    public void Write(Stream stream)
    {
        if (saveWorld is not { } world) throw new InvalidOperationException("Bind the Brutality system to its world before saving.");
        BindWorld(world);
        RetainSaveState(world);
        if (!sidecarAuthoritative && world.Saved is { } population)
            foreach (var member in population.Platoons.SelectMany(platoon => platoon.Members))
                if (!member.Gone && SaveIdentityAdapter.KeyFor(member.Source.Handle) is { } key && legacySeen.Add(key)
                    && LegacyGoreSave.Character(member.Source) is { } legacy) unresolved.TryAdd(key, legacy);
        BrutalityStateCodec.Write(stream, ExportDurable(id => liveSaveKeys.GetValueOrDefault(id)));
        // All known legacy records are now represented. The continuing world and a reload use the same authority rule.
        sidecarAuthoritative = true;
        legacyBaselines.Clear(); legacyKeys.Clear(); legacySeen.Clear();
    }

    public void Read(Stream stream)
    {
        var saved = BrutalityStateCodec.Read(stream);
        sidecarAuthoritative = true;
        foreach (var (actor, baseline) in legacyBaselines)
        {
            actor.Progress = new(baseline.Experience); actor.Severe = baseline.SevereHead; actor.Head = baseline.Head; actor.Torso = baseline.Torso;
            actor.Limbs = Array.AsReadOnly(baseline.Limbs.ToArray()); actor.Wounds = WoundBody.From(baseline.Wounds);
            actor.ChestHole = baseline.ChestHole;
        }
        legacyBaselines.Clear(); legacyKeys.Clear(); legacySeen.Clear();
        unresolved.Clear();
        foreach (var state in saved) unresolved[state.Key] = state;
        if (saveWorld is { } world) BindWorld(world);
        changed = true;
    }
    internal void HashSave(ref StateHasher hash)
    {
        if (!sidecarAuthoritative) return;
        hash.Add(0x47534156); hash.Add(true);
    }
}
