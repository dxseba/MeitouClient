using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Meitou.Brutality.Effects;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Stores each organ on its owner's native save record, independent of transient character slots.</summary>
internal static class StockBowelSave
{
    const string Key = "dxseba brutality bowels";
    const int Version = 1, MaximumLength = 32_768;
    static readonly ConditionalWeakTable<World, State> states = new();
    static readonly JsonSerializerOptions json = new() { IncludeFields = true, IgnoreReadOnlyProperties = true, MaxDepth = 12 };
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed record Record(int Version, SavedBowelOwner Owner);
    sealed record Capture(World World, IReadOnlyDictionary<CharacterId, SavedBowelOwner> Live);
    [ThreadStatic] static Capture? capturing;

    sealed class State
    {
        public readonly ConcurrentDictionary<CharacterId, SavedBowelOwner> Pending = new();
        public readonly HashSet<CharacterId> Observed = [];
        public long Spawns = -1;
        public int Count;
    }

    internal static void Install(ModContext context)
    {
        var writer = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "WriteCharacter" && method.ReturnType == typeof(SaveCharacter)
                && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
        context.Harmony.Patch(AccessTools.Method(typeof(SaveCapture), nameof(SaveCapture.Capture)),
            prefix: Hook(nameof(BeforeCapture)), finalizer: Hook(nameof(CaptureFinished)));
        context.Harmony.Patch(writer, postfix: Hook(nameof(Written)));
        var loaded = Hook(nameof(Loaded));
        loaded.priority = Priority.Last;
        context.Harmony.Patch(AccessTools.Method(typeof(SaveLoader), nameof(SaveLoader.Load)), postfix: loaded);

        static HarmonyMethod Hook(string name) => new(typeof(StockBowelSave), name);
    }

    static void BeforeCapture(World world, out Capture? __state)
    {
        __state = capturing;
        capturing = MasterImpactFeed.CaptureBowels(world) is { } rows ? new(world, rows) : null;
    }

    static Exception? CaptureFinished(Exception? __exception, Capture? __state)
    {
        capturing = __state;
        return __exception;
    }

    static void Written(World world, CharacterId id, SaveCharacter __result)
    {
        if (__result.GameState is not { } state) return;
        var live = capturing is { } capture && ReferenceEquals(capture.World, world) ? capture.Live.GetValueOrDefault(id) : null;
        if ((live ?? Pending(world, id)) is not { IsValid: true } row) return;
        var text = JsonSerializer.Serialize(new Record(Version, row), json);
        if (text.Length <= MaximumLength) state.Strings[Key] = text;
        if (trace && row.Pieces.Length > 0) Console.WriteLine($"Brutality stock bowel saved owner={id} {Describe(row)}");
    }

    static void Loaded(World world) => ObserveSpawns(world, force: true);

    internal static void ObserveSpawns(World world, bool force = false)
    {
        if (world.Saved is null) return;
        var state = states.GetOrCreateValue(world);
        var table = world.Characters;
        if (!force && state.Spawns == table.Spawns) return;
        state.Spawns = table.Spawns;
        foreach (var id in state.Observed.Where(id => !table.TryResolveNext(id, out _)).ToArray())
        {
            state.Observed.Remove(id);
            Consume(world, id);
        }
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot)?.Save?.Source.GameState is not { } game
                || game.Strings.GetValueOrDefault(Key) is not { Length: > 0 and <= MaximumLength } text) continue;
            var id = table.IdOf(slot);
            if (!state.Observed.Add(id)) continue;
            try
            {
                if (JsonSerializer.Deserialize<Record>(text, json) is { Version: Version, Owner.IsValid: true } row
                    && state.Pending.TryAdd(id, row.Owner))
                    Interlocked.Increment(ref state.Count);
            }
            catch (JsonException) { }
        }
    }

    internal static bool HasPending(World world) =>
        states.TryGetValue(world, out var state) && Volatile.Read(ref state.Count) > 0;

    internal static SavedBowelOwner? Pending(World world, CharacterId id) =>
        states.TryGetValue(world, out var state) ? state.Pending.GetValueOrDefault(id) : null;

    internal static void Consume(World world, CharacterId id)
    {
        if (states.TryGetValue(world, out var state) && state.Pending.TryRemove(id, out _))
            Interlocked.Decrement(ref state.Count);
    }

    internal static string Describe(SavedBowelOwner owner)
    {
        var positions = string.Join(";", owner.Pieces.OrderBy(row => row.Half).Select(row =>
        {
            var point = row.Rig.Nodes[0];
            return FormattableString.Invariant($"{row.Half}:{point.Position.X:F3},{point.Position.Y:F3},{point.Position.Z:F3}/{point.Linear.Length():F3}");
        }));
        return $"pieces={owner.Pieces.Length} attached={owner.Pieces.Count(row => row.Rig.PinNode >= 0)} frozen={owner.Pieces.Count(row => row.Rig.Frozen)} nodes={positions}";
    }
}
