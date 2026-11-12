using System.Reflection;
using System.Numerics;
using System.Text.Json;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Imports an earlier Brutality save once, before the stock native loader restores characters and mod data.</summary>
internal static class StockSaveMigration
{
    const string StockId = LegacyGoreSave.ModId + ".stock";
    const string WeaponBlood = "weapon-blood", OldTorso = "meitou torso corpse", OldHead = "meitou head corpse";
    const string Decapitated = "meitou decapitated";
    const string CarrySide = "meitou carry torso side";
    const int MaxSidecar = 32 * 1024 * 1024, MaxActors = 100_000;
    internal const string WoundCorpse = "dxseba brutality wound corpse";
    static readonly Type envelope = typeof(ModHost).Assembly.GetType("Meitou.Modding.ModSaveFile", throwOnError: true)!;
    static readonly Func<byte[], Dictionary<string, byte[]>> read = AccessTools.Method(envelope, "Read").CreateDelegate<Func<byte[], Dictionary<string, byte[]>>>();
    static readonly Func<IReadOnlyList<(string Name, byte[] Data)>, byte[]> write =
        AccessTools.Method(envelope, "Write").CreateDelegate<Func<IReadOnlyList<(string, byte[])>, byte[]>>();
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    sealed record OldHalf(string Owner, int StableKey, string? Carrier, float X, float Y, float Z, float Yaw);
    sealed record OldProxies(int SchemaVersion, OldHalf[] Halves);

    internal static void Install(ModContext context)
    {
        context.Harmony.Patch(AccessTools.Method(typeof(SaveGame), nameof(SaveGame.Load), [typeof(SaveFolder), typeof(Action<SaveCamera>)]),
            postfix: Hook(nameof(LoadedFiles)));
        context.Harmony.Patch(AccessTools.Method(typeof(SaveGame), nameof(SaveGame.Clone)), postfix: Hook(nameof(CopiedFiles)));
        context.Harmony.Patch(AccessTools.Method(typeof(SaveLoader), nameof(SaveLoader.Load)), prefix: Hook(nameof(BeforeLoad)));
    }

    static HarmonyMethod Hook(string name) => new(typeof(StockSaveMigration), name);
    static void LoadedFiles(SaveFolder folder, SaveGame __result) => LegacyGoreSave.Loaded(folder, __result);
    static void CopiedFiles(SaveGame __instance, SaveGame __result) => LegacyGoreSave.Copied(__instance, __result);

    static void BeforeLoad(SaveGame save)
    {
        if (save.ModData.ContainsKey(StockId)) return;
        try { Import(save); }
        catch (Exception error) when (error is InvalidDataException or IOException or JsonException or FormatException)
        {
            Console.Error.WriteLine($"Brutality older save metadata was skipped: {error.GetBaseException().Message}");
        }
    }

    static void Import(SaveGame save)
    {
        Dictionary<string, byte[]> older = [];
        if (save.ModData.TryGetValue(LegacyGoreSave.ModId, out byte[]? sidecar))
        {
            if (sidecar.Length > MaxSidecar) throw new InvalidDataException("Older Brutality data exceeds the migration limit.");
            older = read(sidecar);
        }
        IReadOnlyList<BrutalityDurableState> rows = older.TryGetValue("gore", out byte[]? actorBytes)
            ? BrutalityStateCodec.Read(new MemoryStream(actorBytes, writable: false)) : LegacyGoreSave.Read(save);
        if (rows.Count > MaxActors) throw new InvalidDataException("Too many older Brutality characters to migrate.");

        var actors = save.Characters.Where(actor => actor.Handle.RecordType == FcsRecordType.Character)
            .GroupBy(actor => SaveIdentityAdapter.KeyFor(actor.Handle)!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var accepted = rows.Where(row => actors.ContainsKey(row.Key)).GroupBy(row => row.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => (Actor: actors[group.Key], State: group.Single())).ToArray();
        var entries = new List<(string, byte[])>();
        if (accepted.Length > 0)
        {
            entries.Add(("progression", StockProgression.MigrationBytes(accepted.Select(row => (row.Actor.Handle, row.State.Experience)))));
            entries.Add(("wounds", StockWoundRules.MigrationBytes(accepted.Select(row => (row.Actor.Handle, row.State)))));
        }
        if (older.TryGetValue(WeaponBlood, out byte[]? stains) && ValidWeaponBlood(stains)) entries.Add((WeaponBlood, stains));
        if (entries.Count == 0) return;

        // Encode the stock envelope first. The old records stay in the save and this conversion only changes the in-memory load.
        byte[] stock = write(entries);
        foreach (var (actor, state) in accepted) ApplyCuts(actor, state);
        if (StockTorsoRules.Enabled) ImportTorsoCarries(older, actors, accepted, sidecar is null);
        save.ModData[StockId] = stock;
        if (trace) Console.WriteLine($"Brutality stock imported older save actors={accepted.Length} weaponSidecar={entries.Any(entry => entry.Item1 == WeaponBlood)}");
    }

    static void ImportTorsoCarries(IReadOnlyDictionary<string, byte[]> older, IReadOnlyDictionary<string, SaveCharacter> actors,
        IReadOnlyList<(SaveCharacter Actor, BrutalityDurableState State)> accepted, bool integratedSave)
    {
        var owners = accepted.Where(row => row.State.Torso is not null && row.Actor.Medical?.Bools.GetValueOrDefault("dead") == true)
            .ToDictionary(row => row.State.Key, row => row.Actor, StringComparer.Ordinal);
        if (owners.Count == 0) return;
        if (older.TryGetValue("body-fragments", out var bytes))
        {
            OldProxies? document;
            try { document = JsonSerializer.Deserialize<OldProxies>(bytes); }
            catch (JsonException) { return; }
            if (document is not { SchemaVersion: 1, Halves.Length: <= MaxActors }) return;
            var rows = document.Halves.Where(row => row is { StableKey: 1, Owner.Length: > 0 and <= 256 }
                    && owners.ContainsKey(row.Owner) && ValidPosition(new(row.X, row.Y, row.Z)) && float.IsFinite(row.Yaw))
                .GroupBy(row => row.Owner, StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
            var uniqueCarriers = rows.Where(row => row.Carrier is not null).GroupBy(row => row.Carrier!, StringComparer.Ordinal)
                .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var owner = owners[row.Owner];
                StockTorsoProxySave.MigratePosition(owner, new(row.X, row.Y, row.Z));
                if (row.Carrier is { } key && uniqueCarriers.Contains(key) && actors.TryGetValue(key, out var carrier))
                    MigrateCarry(owner, carrier);
            }
            return;
        }
        if (!integratedSave) return;
        // Integrated builds wrote both halves as native character carries. Only the lower half needs a new proxy link.
        var integrated = actors.Values.Where(actor => actor.GameState is { } state && state.Bools.GetValueOrDefault("carrying")
                && Hand.IsComplete(state, "carrying") && actor.Medical?.Bools.GetValueOrDefault("dead") != true)
            .Select(actor => (Carrier: actor, Target: SaveIdentityAdapter.KeyFor(Hand.Read(actor.GameState!, "carrying")),
                Side: actor.GameState!.Ints.GetValueOrDefault(CarrySide)))
            .Where(row => row.Side is 0 or 1 && row.Target is not null && owners.TryGetValue(row.Target, out var owner)
                && !ReferenceEquals(owner, row.Carrier))
            .GroupBy(row => (row.Target, row.Side)).Where(group => group.Count() == 1).Select(group => group.Single());
        foreach (var (carrier, target, side) in integrated)
        {
            var owner = owners[target!];
            if (side == 1)
            {
                // The native link can restore the upper half once its owner is spawned, even if its saved platoon is remote.
                if (owner.GameState is { } body) StockTorsoSave.RequireMigratedOwner(body);
                continue;
            }

            if (ValidPosition(owner.Position)) StockTorsoProxySave.MigratePosition(owner, owner.Position);
            MigrateCarry(owner, carrier);
        }
    }

    static bool ValidPosition(Vector3 position) => float.IsFinite(position.LengthSquared()) && position.LengthSquared() < 1e14f;

    static void MigrateCarry(SaveCharacter owner, SaveCharacter carrier)
    {
        if (ReferenceEquals(owner, carrier) || owner.GameState is not { } body || carrier.GameState is not { } holder
            || carrier.Medical?.Bools.GetValueOrDefault("dead") == true) return;
        var holding = Hand.Read(holder, "carrying");
        var heldBy = Hand.Read(body, "carrying");
        if (!holding.IsNull && holding != owner.Handle || !heldBy.IsNull && heldBy != carrier.Handle
            || !StockTorsoProxySave.MigrateCarrier(carrier, owner.Handle)) return;
        if (holding == owner.Handle) Hand.Null.Write(holder, "carrying");
        if (heldBy == carrier.Handle) Hand.Null.Write(body, "carrying");
        StockTorsoSave.RequireMigratedOwner(body);
    }

    static void ApplyCuts(SaveCharacter actor, BrutalityDurableState state)
    {
        if (actor.GameState is not { } record) return;
        ulong seed = Rng.StableHash(state.Key);
        if (StockLimbRules.Enabled && state.Limbs.Length > 0) StockLimbSave.Migrate(record, state.Limbs, seed);
        if (actor.Medical is not { } medical || !medical.Bools.GetValueOrDefault("dead")) return;
        if (StockTorsoRules.Enabled && state.Torso is { } torso)
            StockTorsoSave.Migrate(record, torso, seed, Marked(actor, OldTorso));
        if (!StockHeadRules.Enabled || state.Head is not { } head) return;

        medical.Bools[Decapitated] = true;
        StockHeadSplitSave.Migrate(record, head, seed, Marked(actor, OldHead));
        if (head.Mode != HeadCutMode.DetachedSplit && Marked(actor, OldHead)) record.Bools[WoundCorpse] = true;
    }

    static bool Marked(SaveCharacter actor, string name) => actor.GameState?.Bools.GetValueOrDefault(name) == true
        || actor.Platoon.Record.Bools.GetValueOrDefault(name);

    static bool ValidWeaponBlood(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("SchemaVersion", out var version)
                && version.TryGetInt32(out int number) && number == 1;
        }
        catch (JsonException) { return false; }
    }
}
