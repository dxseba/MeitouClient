using System.Runtime.CompilerServices;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Engine;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Brutality.Rendering;

/// <summary>Prepares immutable healthy fits before a first wound; GPU uploads remain on the renderer's bounded preparation path.</summary>
internal static class GeometryPreparations
{
    readonly record struct Key(OgreMesh Body, SkeletonRig Rig, bool Female, uint Blood, bool Mechanical);
    sealed record Resolved(Key? Value, string? BodyPath);
    sealed class WorldState
    {
        public readonly GeometryPreparationCache<Key> Cache = new();
        public readonly ConditionalWeakTable<CharacterAppearance, Resolved> Looks = new();
        public long ResolvedFrame = long.MinValue;
    }
    static readonly ConditionalWeakTable<World, WorldState> worlds = new();

    internal static void ReleaseWorld(World world)
    {
        if (worlds.TryGetValue(world, out var state)) { state.Cache.Dispose(); worlds.Remove(world); }
    }

    internal static bool CanPrepare(BrutalityActorView state) => state.Torso is null && state.Head is null && state.Limbs.Count == 0;

    internal static bool ShouldDefer(World world, CharacterAppearance look, SkeletonRig rig, OgreMesh body)
    {
        if (look.Animal is not null || look.Headless || look.HeadOnly || !worlds.TryGetValue(world, out var state)) return false;
        var key = new Key(body, rig, look.Female, unchecked((uint)look.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF,
            look.Race.GetBool("is robot"));
        return state.Cache.ShouldDefer(key);
    }

    internal static void Capture(object host, CharacterDrawList list, WorldSession session, RenderResources resources)
    {
        if (list.Context is not { } gpu) return;
        var worldState = worlds.GetValue(session.World, static _ => new());
        var cache = worldState.Cache;
        long frame = gpu.Frame.Number;
        var wounded = BrutalityRuntime.For(session.World)?.WoundFrameAt(session.ShownTick)?.Characters.Values
            .Where(state => !CanPrepare(state))
            .Select(state => RenderBridge.NativeDrawKey(host, state.Character)).ToHashSet();
        foreach (var instance in list.Items)
        {
            var look = instance.Appearance;
            if (instance.Hidden || look.Animal is not null || look.Headless || look.HeadOnly || wounded?.Contains(instance.Key) == true) continue;
            if (!worldState.Looks.TryGetValue(look, out var resolved))
            {
                // Import at most one previously unseen look per GPU frame; stable looks do no file/rig lookups thereafter.
                if (worldState.ResolvedFrame == frame || !cache.CanStartAt(frame)) continue;
                worldState.ResolvedFrame = frame;
                Key? value = resources.Rigs.For(look) is { } setup && resources.Mesh(look.BodyMesh) is { } body
                    ? new(body, setup.Rig, look.Female, unchecked((uint)look.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF,
                        look.Race.GetBool("is robot")) : null;
                resolved = new(value, value is null ? null : resources.Rigs.FindMesh(look.BodyMesh)); worldState.Looks.Add(look, resolved);
            }
            if (resolved is not { Value: { } key, BodyPath: { } bodyPath }) continue;
            cache.Touch(key, frame);
            if (!cache.CanRequest(key, frame)) continue;
            // Resolve host dictionaries and snapshot records on this render thread. The worker sees immutable meshes/rig/values only.
            var parts = look.Parts.ToArray();
            var exteriors = parts.Where(part => part.Mode == AttachMode.SharedSkeleton)
                .Select(part => (Mesh: resources.Mesh(part.Mesh), Scale: part.Record.Type == Meitou.Data.Fcs.FcsRecordType.ATTACHMENT ? 4f : 1f)).ToArray();
            var library = resources.Geometry.Anatomy;
            var race = look.Race;
            var fragmentAssets = FragmentAssets.For(resources.Rigs);
            var indexed = resources.Assets.Configured;
            var installRoot = resources.InstallRoot;
            cache.Request(key, instance, frame, () =>
            {
                fragmentAssets.Prepare(bodyPath, key.Body, race, key.Female, key.Rig, indexed, installRoot);
                return Prepare(library, key.Rig, key.Body, race, parts, exteriors, key.Female, key.Blood, key.Mechanical);
            });
        }
        cache.Publish(list.Preparations);
    }

    internal static PreparedGeometry Prepare(Anatomy.AnatomyLibrary library, SkeletonRig rig, OgreMesh body, GameRecord race,
        CharacterPart[] sourceParts, (OgreMesh? Mesh, float Scale)[] exteriors, bool female, uint blood, bool mechanical)
    {
        var fit = NativeGeometry.Prepare(library, rig, body, female, blood, mechanical);
        if (!mechanical) Meitou.Brutality.Effects.BowelPresentation.Prepare(library, rig);
        GeneratedExteriorLod.LevelsFor(body, rig);
        foreach (var (mesh, scale) in exteriors) if (mesh is not null) GeneratedExteriorLod.LevelsFor(mesh, rig, scale);
        var parts = sourceParts.Select(part => new RuntimeCharacterPart(part)
        {
            DrawFlags = part.Mode == AttachMode.SharedSkeleton
                ? (part.Record.GetInt("material type") == 3 ? NativeSurfaceShaders.PlateFlag : NativeSurfaceShaders.TearFlag)
                    | (part.Slot is AttachSlot.Hair or AttachSlot.Beard ? NativeSurfaceShaders.HairWoundFlag : 0)
                : 0,
        }).ToList();
        // Compile the same programs and native exterior materials without uploading a healthy actor's unused full anatomy.
        // The encoded fit stays CPU-owned here and is uploaded only by real wound/cut geometry.
        var geometry = new RuntimeCharacterGeometry(null, parts, NativeSurfaceShaders.CreateSet(),
            bodyFlags: NativeSurfaceShaders.WoundFlag, bodyMaterialFlags: mechanical ? NativeSurfaceShaders.Mechanical : 0, bodyDoubleSided: true);
        return new(geometry, AnatomyFitCache<int>.EstimateBytes(fit.Anatomy), fit.Anatomy);
    }
}

internal readonly record struct PreparedGeometry(RuntimeCharacterGeometry Geometry, long Bytes, OgreMesh? SourceAnatomy = null);

/// <summary>One background fit at a time, one start per GPU frame, byte-bounded residency, and no repeated work for evicted keys.</summary>
internal sealed class GeometryPreparationCache<TKey>(long byteBudget = 192L * 1024 * 1024, int capacity = 16,
    Func<Func<PreparedGeometry>, Task<PreparedGeometry>>? start = null) : IDisposable where TKey : notnull
{
    sealed class Entry(CharacterInstance instance, Task<PreparedGeometry> task, long touched)
    {
        public readonly CharacterInstance Instance = instance;
        public readonly Task<PreparedGeometry> Task = task;
        public PreparedGeometry? Ready;
        public long Touched = touched;
    }
    readonly Dictionary<TKey, Entry> entries = [];
    readonly HashSet<TKey> attempted = [];
    long startedFrame = long.MinValue, bytes;
    bool disposed;
    internal int Count => entries.Count;
    internal long Bytes => bytes;
    internal void Touch(TKey key, long frame) { if (entries.TryGetValue(key, out var entry)) entry.Touched = frame; }
    // A successful task becomes visible only when the next capture publishes its result; render fills never wait for it.
    internal bool IsPending(TKey key) => !disposed && entries.TryGetValue(key, out var entry) && entry.Ready is null
        && !entry.Task.IsFaulted && !entry.Task.IsCanceled;
    internal bool ShouldDefer(TKey key) => !disposed && (IsPending(key)
        || !attempted.Contains(key) && attempted.Count < 128);
    internal bool CanStartAt(long frame) => !disposed && startedFrame != frame && attempted.Count < 128
        && entries.Values.All(entry => entry.Task.IsCompleted);
    internal bool CanRequest(TKey key, long frame) => !attempted.Contains(key) && CanStartAt(frame);

    internal bool Request(TKey key, CharacterInstance instance, long frame, Func<PreparedGeometry> prepare)
    {
        if (!CanRequest(key, frame)) return false;
        Complete();
        while (entries.Count >= capacity) Evict(entries.MinBy(pair => pair.Value.Touched).Key);
        attempted.Add(key); startedFrame = frame;
        entries.Add(key, new(instance, (start ?? Start)(prepare), frame));
        return true;
    }
    static Task<PreparedGeometry> Start(Func<PreparedGeometry> prepare) => Task.Factory.StartNew(() =>
    {
        Thread.CurrentThread.IsBackground = true;
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        return prepare();
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    void Complete()
    {
        foreach (var pair in entries.ToArray())
        {
            var entry = pair.Value;
            if (entry.Ready is not null || !entry.Task.IsCompleted) continue;
            if (!entry.Task.IsCompletedSuccessfully)
            {
                Console.Error.WriteLine("Brutality preparation: " + entry.Task.Exception?.GetBaseException().Message);
                entries.Remove(pair.Key); continue;
            }
            entry.Ready = entry.Task.Result; bytes += Math.Max(0, entry.Ready.Value.Bytes);
        }
        // One individually oversized fit may remain; never retain several oversized fits or duplicate their encoded buffers.
        while (entries.Count > 1 && bytes > byteBudget)
        {
            var oldest = entries.Where(pair => pair.Value.Ready is not null).MinBy(pair => pair.Value.Touched);
            Evict(oldest.Key);
        }
    }
    internal void Publish(List<CharacterInstance> output)
    {
        if (disposed) return;
        Complete();
        foreach (var entry in entries.Values)
            if (entry.Ready is { } ready) output.Add(entry.Instance with { Geometry = ready.Geometry, Bones = null, RuntimeShaderData = null });
    }
    void Evict(TKey key)
    {
        var entry = entries[key]; entries.Remove(key);
        if (entry.Ready is { } ready) { bytes -= Math.Max(0, ready.Bytes); ready.Geometry.Retire(); }
        else _ = entry.Task.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully) task.Result.Geometry.Retire();
            else _ = task.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public void Dispose()
    {
        disposed = true;
        foreach (var key in entries.Keys.ToArray()) Evict(key);
        attempted.Clear();
    }
}
