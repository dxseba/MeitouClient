using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Frame;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Evicts retired generated looks through the stock render thread, after their native preparation has finished.</summary>
internal sealed class StockGeneratedMeshCache
{
    const int RetirePerUpdate = 8;
    static StockGeneratedMeshCache? installed;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    readonly object sync = new();
    readonly List<Cache> caches = [];
    readonly ConditionalWeakTable<object, Cache> byContent = new();
    readonly ConditionalWeakTable<object, MorphPool> morphs = new();
    readonly FieldInfo content, assets, states, stateAsset, bodyHeights, portraits, live, liveCount;
    readonly FieldInfo nativeMeshes, prepared, fetching, close, setups, meshGpuBytes, vertices, indices, knownMorphs, morphGpu;
    readonly PropertyInfo draws, rig, morphArena, meshBytes, gpuMesh, gpuParts, portraitAsset;
    readonly MethodInfo reserveMorphs, writeMorphs;
    volatile bool faulted;

    internal readonly record struct Reclaimed(string Path, long Bytes);
    sealed class PendingPath(Reclaimed mesh)
    {
        public readonly Reclaimed Mesh = mesh;
        public int References;
    }
    sealed class Cache(object owner)
    {
        public readonly WeakReference<object> Content = new(owner);
        public readonly HashSet<CharacterAppearance> Looks = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<string> Paths = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<Task> Workers = [];
        public readonly List<CharacterAppearance> RetiredLooks = [];
        public readonly List<PendingPath> RetiredPaths = [];
    }
    sealed class MorphPool
    {
        public readonly Dictionary<string, List<(string Key, Slot Slot)>> Active = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, Queue<(int Start, long Fence)>> Free = [];
    }
    readonly record struct Slot(int Start, int Capacity);

    StockGeneratedMeshCache(Type type, Type renderer)
    {
        content = Field(renderer, "_content"); assets = Field(renderer, "_assets");
        draws = Property(renderer, "Draws");
        states = Field(renderer, "_states"); stateAsset = Field(states.FieldType.GetGenericArguments()[1], "Asset");
        bodyHeights = Field(renderer, "_bodyHeights"); portraits = Field(renderer, "_portraitShown");
        portraitAsset = Property(portraits.FieldType.GetGenericArguments()[1], "Asset");
        live = Field(renderer, "_live"); liveCount = Field(renderer, "_liveCount");
        nativeMeshes = Field(type, "_meshes"); prepared = Field(type, "_prepared");
        fetching = Field(type, "_fetching"); close = Field(type, "_closeUp"); rig = Property(type, "Rigs");
        setups = Field(typeof(CharacterRigs), "_setups"); meshBytes = Property(type, "MeshBytes");
        gpuMesh = Property(nativeMeshes.FieldType.GetGenericArguments()[1], "Gpu");
        meshGpuBytes = Field(gpuMesh.PropertyType, "Bytes"); gpuParts = Property(gpuMesh.PropertyType, "Parts");
        var part = gpuParts.PropertyType.GetGenericArguments()[0];
        vertices = Field(part, "Vertices"); indices = Field(part, "Indices");
        morphArena = Property(type, "Morphs");
        var morph = morphArena.PropertyType;
        knownMorphs = Field(morph, "_known"); morphGpu = Field(morph, "_gpu");
        reserveMorphs = Method(morph, "Reserve", typeof(int));
        writeMorphs = Method(morph, "Write", typeof(int), typeof(Vector4[]));
    }

    internal static void Install(ModContext context, Type content)
    {
        var applied = new List<(MethodInfo Target, MethodInfo Callback)>();
        string step = "resolve stock cache members";
        try
        {
            var renderer = content.Assembly.GetType("Meitou.Game.Rendering.Characters.CharacterRenderer", true)!;
            var hook = new StockGeneratedMeshCache(content, renderer);
            var start = Method(content, "Start", typeof(CharacterAppearance), typeof(bool), typeof(bool));
            // Build has a filtered catch whose IL the pinned Harmony version cannot rewrite. AssetFor also covers synchronous stills.
            var asset = Method(renderer, "AssetFor", typeof(CharacterInstance).MakeByRefType(), typeof(Vector3), typeof(float),
                typeof(int).MakeByRefType(), typeof(double).MakeByRefType());
            var morph = Method(hook.morphArena.PropertyType, "Add", typeof(string), typeof(Vector4[]));
            var update = Method(renderer, "Update", typeof(Vector3));
            var dispose = Method(content, nameof(IDisposable.Dispose));
            Apply(start, nameof(Started));
            Apply(asset, nameof(AssetRequested), prefix: true);
            Apply(morph, nameof(Morph), prefix: true);
            Apply(update, nameof(Updated));
            Apply(dispose, nameof(Disposed));
            installed = hook;
            context.Log("Brutality stock generated mesh recycling initialized");
        }
        catch (Exception error)
        {
            installed = null;
            for (int index = applied.Count - 1; index >= 0; index--)
            {
                var (target, callback) = applied[index];
                try
                {
                    if (Harmony.GetPatchInfo(target) is { } info
                        && info.Prefixes.Concat(info.Postfixes).Any(patch => patch.PatchMethod == callback))
                        context.Harmony.Unpatch(target, callback);
                }
                catch (Exception rollback)
                {
                    context.Log($"Brutality stock recycler could not roll back {target.Name}: {rollback.GetBaseException().Message}");
                }
            }
            context.Log($"Brutality stock generated mesh recycling unavailable at {step}: {error.GetBaseException().Message}");
        }

        void Apply(MethodInfo target, string name, bool prefix = false)
        {
            step = $"{target.DeclaringType?.Name}.{target.Name}";
            var callback = typeof(StockGeneratedMeshCache).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(StockGeneratedMeshCache).Name, name);
            applied.Add((target, callback));
            var patch = new HarmonyMethod(callback);
            context.Harmony.Patch(target, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }
    }

    static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
    static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name) ?? throw new MissingMemberException(type.Name, name);
    static MethodInfo Method(Type type, string name, params Type[] args) => AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.Name, name);
    static void Started(object __instance, CharacterAppearance c, Task? __result) => installed?.Track(__instance, c, __result);
    static void AssetRequested(object __instance, in CharacterInstance inst)
    {
        if (installed is not { faulted: false } hook || !StockHeadMeshes.HasGeneratedPath(inst.Appearance)
            || hook.content.GetValue(__instance) is not { } owner) return;
        hook.Track(owner, inst.Appearance, null);
    }
    static void Updated(object __instance)
    {
        if (installed is not { faulted: false } hook) return;
        try { hook.Clean(__instance); }
        catch (Exception error)
        {
            hook.faulted = true;
            Console.Error.WriteLine($"Brutality stopped recycling stock mesh caches: {error.GetBaseException().Message}");
        }
    }
    static void Disposed(object __instance) => installed?.Forget(__instance);

    void Track(object owner, CharacterAppearance look, Task? worker)
    {
        if (faulted || !StockHeadMeshes.HasGeneratedPath(look)) return;
        lock (sync)
        {
            if (!byContent.TryGetValue(owner, out var cache))
            {
                cache = new(owner);
                byContent.Add(owner, cache);
                caches.Add(cache);
            }
            if (cache.Looks.Add(look)) cache.Paths.UnionWith(StockHeadMeshes.GeneratedPaths(look));
            if (worker is { IsCompleted: false })
            {
                cache.Workers.RemoveAll(task => task.IsCompleted);
                cache.Workers.Add(worker);
            }
        }
    }

    internal static void Retire(CharacterAppearance look, IReadOnlyList<Reclaimed> paths)
    {
        if (installed is not { faulted: false } hook) return;   // Preserve the conservative process ceiling without the native seam.
        lock (hook.sync)
        {
            for (int i = hook.caches.Count - 1; i >= 0; i--)
            {
                var cache = hook.caches[i];
                if (!cache.Content.TryGetTarget(out _)) { hook.Release(cache); continue; }
                if (cache.Looks.Remove(look)) cache.RetiredLooks.Add(look);
            }
            foreach (var path in paths)
            {
                var pending = new PendingPath(path);
                foreach (var cache in hook.caches)
                    if (cache.Paths.Remove(path.Path))
                    {
                        pending.References++;
                        cache.RetiredPaths.Add(pending);
                    }
                if (pending.References == 0) StockHeadMeshes.Recycled(path);
            }
        }
    }

    void Clean(object renderer)
    {
        if (content.GetValue(renderer) is not { } owner || !byContent.TryGetValue(owner, out var cache)
            || draws.GetValue(renderer) is not CharacterDrawList list) return;
        lock (sync)
        {
            if (cache.RetiredLooks.Count == 0 && cache.RetiredPaths.Count == 0) return;
            cache.Workers.RemoveAll(task => task.IsCompleted);
            if (cache.Workers.Count != 0) return;
            // Update has rebuilt the list for this frame. Do not dispose anything it can still draw or blend.
            var currentLooks = new HashSet<CharacterAppearance>(ReferenceEqualityComparer.Instance);
            foreach (var item in list.Items.Concat(list.Distant))
            {
                if (StockHeadMeshes.HasGeneratedPath(item.Appearance)) currentLooks.Add(item.Appearance);
                if (item.BlendFrom is { } from && StockHeadMeshes.HasGeneratedPath(from)) currentLooks.Add(from);
            }
            var looks = new List<CharacterAppearance>();
            for (int i = cache.RetiredLooks.Count - 1; i >= 0 && looks.Count < RetirePerUpdate; i--)
            {
                var look = cache.RetiredLooks[i];
                if (currentLooks.Contains(look)) continue;
                looks.Add(look);
                cache.RetiredLooks.RemoveAt(i);
            }
            if (looks.Count > 0) ClearLooks(renderer, owner, looks);
            // The earlier frame's cached assets may retain these buffers until their looks have also been retired.
            if (cache.RetiredLooks.Count != 0) return;
            var currentPaths = currentLooks.SelectMany(StockHeadMeshes.GeneratedPaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var paths = new List<PendingPath>();
            for (int i = cache.RetiredPaths.Count - 1; i >= 0 && paths.Count < RetirePerUpdate; i--)
            {
                var pending = cache.RetiredPaths[i];
                if (currentPaths.Contains(pending.Mesh.Path)) continue;
                paths.Add(pending);
                cache.RetiredPaths.RemoveAt(i);
            }
            if (paths.Count == 0) return;
            ClearPaths(renderer, owner, paths);
            foreach (var path in paths) Complete(path);
            if (trace) Console.WriteLine($"Brutality stock recycled generated meshes={paths.Count}");
        }
    }

    void ClearLooks(object renderer, object owner, IReadOnlyList<CharacterAppearance> looks)
    {
        var loaded = (IDictionary)assets.GetValue(renderer)!;
        var jobs = (IDictionary)fetching.GetValue(owner)!;
        var closeUps = (HashSet<CharacterAppearance>)close.GetValue(owner)!;
        var shapes = (IDictionary)setups.GetValue(rig.GetValue(owner))!;
        var removed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var look in looks)
        {
            if (loaded[look] is { } asset) removed.Add(asset);
            loaded.Remove(look);
            jobs.Remove(look);
            closeUps.Remove(look);
            shapes.Remove(look);
        }
        if (removed.Count == 0) return;
        foreach (object state in ((IDictionary)states.GetValue(renderer)!).Values)
            if (stateAsset.GetValue(state) is { } asset && removed.Contains(asset)) stateAsset.SetValue(state, null);
        var shown = (IDictionary)portraits.GetValue(renderer)!;
        foreach (object key in shown.Keys.Cast<object>().ToArray())
            if (portraitAsset.GetValue(shown[key]) is { } asset && removed.Contains(asset)) shown.Remove(key);
        var previous = (Array)live.GetValue(renderer)!;
        int count = (int)liveCount.GetValue(renderer)!;
        Array.Clear(previous, count, previous.Length - count);
    }

    void ClearPaths(object renderer, object owner, IReadOnlyList<PendingPath> paths)
    {
        var selected = paths.Select(path => path.Mesh.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaded = (IDictionary)nativeMeshes.GetValue(owner)!;
        var pending = (IDictionary)prepared.GetValue(owner)!;
        var heights = (IDictionary)bodyHeights.GetValue(renderer)!;
        foreach (object key in pending.Keys.Cast<object>().Where(Matches).ToArray()) pending.Remove(key);
        long bytes = 0;
        foreach (object key in loaded.Keys.Cast<object>().Where(Matches).ToArray())
        {
            object? mesh = loaded[key];
            loaded.Remove(key);
            if (mesh is null || gpuMesh.GetValue(mesh) is not { } gpu) continue;
            bytes += (long)meshGpuBytes.GetValue(gpu)!;
            foreach (object part in (IEnumerable)gpuParts.GetValue(gpu)!)
            {
                ((DeviceBuffer)vertices.GetValue(part)!).Dispose();
                ((DeviceBuffer)indices.GetValue(part)!).Dispose();
            }
        }
        meshBytes.SetValue(owner, Math.Max(0, (long)meshBytes.GetValue(owner)! - bytes));
        foreach (string path in selected) heights.Remove(path);
        if (morphArena.GetValue(owner) is not { } arena || !morphs.TryGetValue(arena, out var pool)) return;
        var known = (Dictionary<string, uint>)knownMorphs.GetValue(arena)!;
        long fence = ((IGpuContext)morphGpu.GetValue(arena)!).Frame.Number;
        foreach (string path in selected)
            if (pool.Active.Remove(path, out var retired))
                foreach (var (key, slot) in retired)
                {
                    known.Remove(key);
                    if (!pool.Free.TryGetValue(slot.Capacity, out var free)) pool.Free.Add(slot.Capacity, free = new());
                    free.Enqueue((slot.Start, fence));
                }

        bool Matches(object key) => key is ITuple tuple && tuple[0] is string path && selected.Contains(path);
    }

    static bool Morph(object __instance, string key, Vector4[] deltas, ref uint __result)
    {
        if (installed is not { faulted: false } hook || deltas.Length == 0 || StockHeadMeshes.MorphPath(key) is not { } path) return true;
        __result = hook.AddMorph(__instance, key, path, deltas);
        return false;
    }

    uint AddMorph(object arena, string key, string path, Vector4[] deltas)
    {
        var known = (Dictionary<string, uint>)knownMorphs.GetValue(arena)!;
        if (known.TryGetValue(key, out uint value)) return value;
        var pool = morphs.GetOrCreateValue(arena);
        var gpu = (IGpuContext)morphGpu.GetValue(arena)!;
        int capacity = checked((int)BitOperations.RoundUpToPowerOf2((uint)deltas.Length));
        int start = pool.Free.TryGetValue(capacity, out var free) && free.TryPeek(out var candidate) && gpu.FrameCompleted(candidate.Fence)
            ? free.Dequeue().Start : (int)reserveMorphs.Invoke(arena, [capacity])!;
        writeMorphs.Invoke(arena, [start, deltas]);
        known.Add(key, value = (uint)(start - 1));
        if (!pool.Active.TryGetValue(path, out var active)) pool.Active.Add(path, active = []);
        active.Add((key, new(start, capacity)));
        return value;
    }

    void Forget(object owner)
    {
        lock (sync)
            if (byContent.TryGetValue(owner, out var cache))
            {
                byContent.Remove(owner);
                Release(cache);
            }
    }

    void Release(Cache cache)
    {
        foreach (var path in cache.RetiredPaths) Complete(path);
        caches.Remove(cache);
    }

    static void Complete(PendingPath path)
    {
        if (--path.References == 0) StockHeadMeshes.Recycled(path.Mesh);
    }
}
