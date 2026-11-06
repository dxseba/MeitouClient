using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meitou.Data.Fcs;
using Meitou.Simulation;

namespace Meitou.Brutality;

/// <summary>Stores head cuts and their loose pieces on the native decapitated actor's save record.</summary>
internal static class StockHeadSplitSave
{
    const string CutKey = "dxseba brutality head split", ImportKey = "dxseba brutality imported head split";
    internal const string Standalone = "dxseba brutality head corpse";
    internal const int Capacity = 8;
    const int Version = 2, MaxLength = 96_000;
    static readonly JsonSerializerOptions json = new() { IncludeFields = true, IgnoreReadOnlyProperties = true, MaxDepth = 10 };
    static readonly ConditionalWeakTable<World, State> states = new();

    internal readonly record struct Motion(Quaternion Orientation, Vector3 Linear, Vector3 Angular, Vector3 HalfExtent);
    internal readonly record struct Pose(Vector3 Position, Matrix4x4 Transform, Motion? Moving = null);
    internal sealed record Layout(int Format, HeadCutState Shape, ulong Seed, Matrix4x4[] Palette, ulong Blood, Pose First, Pose Second);
    sealed record Imported(HeadCutState Shape, ulong Seed);
    sealed record Entry(Layout Layout, bool Restoring);
    sealed class State
    {
        internal readonly ConcurrentDictionary<CharacterId, Imported> Cuts = new();
        internal readonly ConcurrentDictionary<CharacterId, Entry> Actors = new();
        internal readonly ConcurrentDictionary<CharacterId, byte> Evicted = new();
        internal long Spawns = -1;
    }

    internal static bool Has(World world) => states.TryGetValue(world, out var state) && !state.Actors.IsEmpty;
    internal static bool Exists(World world, CharacterId id) => states.TryGetValue(world, out var state) && state.Actors.ContainsKey(id);
    internal static bool HasCuts(World world) => states.TryGetValue(world, out var state) && !state.Cuts.IsEmpty;

    internal static void Remember(World world, CharacterId id, HeadCutState shape, ulong seed)
    {
        if (shape.IsValid) states.GetOrCreateValue(world).Cuts.TryAdd(id, new(shape, seed));
    }

    internal static IEnumerable<CharacterId> Owners(World world) =>
        states.TryGetValue(world, out var state) ? state.Cuts.Keys : [];

    internal static IEnumerable<(CharacterId Actor, Layout Layout)> Pending(World world) => states.TryGetValue(world, out var state)
        ? state.Actors.Where(pair => pair.Value.Restoring && pair.Value.Layout.Shape.Mode == HeadCutMode.DetachedSplit)
            .OrderBy(pair => Rng.Key(pair.Key)).Select(pair => (pair.Key, pair.Value.Layout)) : [];

    internal static Layout? Loose(World world, CharacterId id) => states.TryGetValue(world, out var state)
        && state.Actors.TryGetValue(id, out var entry) && entry.Layout.Shape.Mode is HeadCutMode.AttachedSplit or HeadCutMode.NeckDetach
        ? entry.Layout : null;

    internal static void Publish(World world, CharacterId id, HeadCutState shape, ulong seed, Matrix4x4[] palette, ulong blood, Pose first, Pose second)
    {
        var layout = new Layout(Version, shape, seed, palette, blood, first, second);
        if (!Valid(layout)) return;
        Remember(world, id, shape, seed);
        states.GetOrCreateValue(world).Actors[id] = new(layout, false);
    }

    internal static void Restored(World world, CharacterId id, Layout layout) =>
        states.GetOrCreateValue(world).Actors[id] = new(layout, false);

    internal static void Remove(World world, CharacterId id, bool evict)
    {
        if (!states.TryGetValue(world, out var state)) return;
        // The render budget can evict a mesh without removing its durable pose or owner.
        if (evict) state.Evicted.TryAdd(id, 0);
        else state.Actors.TryRemove(id, out _);
    }

    internal static void Write(World world, CharacterId id, CharacterCold actor, FcsRecord record)
    {
        if (actor.Medical is not { Dead: true, Decapitated: true }) return;
        if (!states.TryGetValue(world, out var state) || !state.Cuts.TryGetValue(id, out var cut)) return;
        if (state.Actors.TryGetValue(id, out var entry) && entry.Layout.Palette.Length != 0)
        {
            record.Strings[CutKey] = JsonSerializer.Serialize(entry.Layout, json);
            record.Strings.Remove(ImportKey);
        }
        else if (!record.Strings.ContainsKey(CutKey))
            record.Strings[ImportKey] = JsonSerializer.Serialize(cut, json);
    }

    internal static void Migrate(FcsRecord record, HeadCutState shape, ulong seed, bool standalone)
    {
        if (record.Strings.ContainsKey(CutKey) || record.Strings.ContainsKey(ImportKey)) return;
        record.Strings[ImportKey] = JsonSerializer.Serialize(new Imported(shape, seed), json);
        if (standalone) record.Bools[Standalone] = true;
    }

    internal static bool IsStandalone(FcsRecord record) => record.Bools.GetValueOrDefault(Standalone) && Read(record) is not null;

    internal static void Restore(World world, CharacterId id, CharacterCold actor)
    {
        if (actor is not { Medical: { Dead: true, Decapitated: true }, Save.Source.GameState: { } record }) return;
        var state = states.GetOrCreateValue(world);
        if (state.Cuts.ContainsKey(id) || Read(record) is not { } layout) return;
        Remember(world, id, layout.Shape, layout.Seed);
        if (layout.Shape.Mode is HeadCutMode.NeckDetach or HeadCutMode.AttachedSplit)
            StockWoundRules.RecordHeadCut(world, id, layout.Shape);
        if (layout.Shape.Mode != HeadCutMode.Burst) state.Actors.TryAdd(id, new(layout, true));
    }

    internal static void Step(World world)
    {
        var state = states.GetOrCreateValue(world);
        var table = world.Characters;
        foreach (var pair in state.Actors)
            if (!table.TryResolveNext(pair.Key, out _)) state.Actors.TryRemove(pair.Key, out _);
        if (state.Spawns == table.Spawns) return;
        foreach (var pair in state.Cuts)
            if (!table.TryResolveNext(pair.Key, out _)) state.Cuts.TryRemove(pair.Key, out _);
        state.Spawns = table.Spawns;
        foreach (var pair in state.Evicted)
            if (!table.TryResolveNext(pair.Key, out _)) state.Evicted.TryRemove(pair.Key, out _);
        if (world.Saved is null) return;
        for (int i = 0; i < table.HighWater; i++)
            if (table.Next[i].Alive && table.Cold(i) is { Medical: { Dead: true, Decapitated: true }, Save: not null } actor)
                Restore(world, table.IdOf(i), actor);
    }

    static Layout? Read(FcsRecord record)
    {
        try
        {
            if (record.Strings.GetValueOrDefault(CutKey) is { Length: > 0 and <= MaxLength } text)
                return JsonSerializer.Deserialize<Layout>(text, json) is { } value && Valid(value) ? value : null;
            if (record.Strings.GetValueOrDefault(ImportKey) is { Length: > 0 and <= 512 } imported
                && JsonSerializer.Deserialize<Imported>(imported, json) is { Shape.IsValid: true } row)
                return new(Version, row.Shape, row.Seed, [], 0, default, default);
        }
        catch (JsonException) { }
        return null;
    }

    static bool Valid(Layout layout) => layout is { Format: 1 or Version, Shape: { IsValid: true, Mode: not HeadCutMode.Burst },
        Palette.Length: > 0 and <= 256 } && layout.Palette.All(MatrixValid)
        && ValidPose(layout.First) && (layout.Shape.Mode != HeadCutMode.DetachedSplit || ValidPose(layout.Second));

    static bool ValidPose(Pose pose) => MatrixValid(pose.Transform) && Matrix4x4.Invert(pose.Transform, out _)
        && float.IsFinite(pose.Position.LengthSquared()) && pose.Position.LengthSquared() < 1e14f
        && (pose.Moving is not { } motion || ValidMotion(motion));

    internal static bool ValidMotion(Motion motion) => motion.Orientation.LengthSquared() is > .9f and < 1.1f
        && motion.Linear.LengthSquared() < 1e6f && motion.Angular.LengthSquared() < 1e6f
        && motion.HalfExtent.X is >= .05f and <= 3 && motion.HalfExtent.Y is >= .05f and <= 3
        && motion.HalfExtent.Z is >= .05f and <= 3;

    static bool MatrixValid(Matrix4x4 transform) => float.IsFinite(transform.GetDeterminant())
        && float.IsFinite(transform.Translation.LengthSquared()) && transform.Translation.LengthSquared() < 1e14f;
}
