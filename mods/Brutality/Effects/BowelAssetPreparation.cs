using Meitou.Game.Rendering.Frame;
using System.Runtime.CompilerServices;
using Meitou.Brutality.Anatomy;

namespace Meitou.Brutality.Effects;

/// <summary>Shared immutable CPU organ plans. Polling never imports anatomy or waits for a worker.</summary>
internal sealed class BowelAssetPreparation(Func<float, BowelAssetSet> build, long byteBudget = 64L * 1024 * 1024,
    int capacity = 4, Func<Func<BowelAssetSet>, Task<BowelAssetSet>>? start = null,
    Func<BowelAssetSet, long>? measure = null) : IDisposable
{
    sealed class Entry(Task<BowelAssetSet> work, long used)
    {
        public readonly Task<BowelAssetSet> Work = work;
        public BowelAssetSet? Ready;
        public long Bytes, Used = used;
    }
    static readonly ConditionalWeakTable<AnatomyLibrary, BowelAssetPreparation> shared = new();
    internal static BowelAssetPreparation Shared(AnatomyLibrary library) => shared.GetValue(library,
        static source => new(scale => BowelPresentation.BuildBowelAssets(source, scale)));
    readonly object gate = new();
    readonly Dictionary<float, Entry> entries = [];
    readonly Dictionary<float, WeakReference<BowelAssetSet>> published = [];
    readonly HashSet<float> failed = [];
    long sequence, bytes;
    bool disposed;
    internal int Count { get { lock (gate) { Complete(); return entries.Count; } } }
    internal long Bytes { get { lock (gate) { Complete(); return bytes; } } }

    internal bool TryGet(float scale, out BowelAssetSet? assets)
    {
        lock (gate)
        {
            assets = null;
            if (disposed || failed.Contains(scale) || !float.IsFinite(scale)) return false;
            Complete();
            if (failed.Contains(scale)) return false;
            if (entries.TryGetValue(scale, out var entry))
            { entry.Used = ++sequence; assets = entry.Ready; return assets is not null; }
            // Actual organs may still own an evicted plan; reuse it without another clipping/skinning pass.
            if (published.TryGetValue(scale, out var weak) && weak.TryGetTarget(out assets)) return true;
            if (entries.Values.Any(value => !value.Work.IsCompleted)) return false;
            while (entries.Count >= Math.Max(1, capacity)) Evict(entries.MinBy(row => row.Value.Used).Key);
            var task = (start ?? Start)(() => build(scale));
            entries.Add(scale, new(task, ++sequence));
            Complete();
            assets = entries.GetValueOrDefault(scale)?.Ready;
            return assets is not null;
        }
    }
    static Task<BowelAssetSet> Start(Func<BowelAssetSet> build) => Task.Factory.StartNew(() =>
    {
        Thread.CurrentThread.IsBackground = true; Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        return build();
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    void Complete()
    {
        foreach (var (key, entry) in entries.ToArray())
        {
            if (entry.Ready is not null || !entry.Work.IsCompleted) continue;
            if (!entry.Work.IsCompletedSuccessfully)
            {
                Console.Error.WriteLine("Brutality bowel preparation: " + entry.Work.Exception?.GetBaseException().Message);
                failed.Add(key); entries.Remove(key); continue;
            }
            entry.Ready = entry.Work.Result;
            entry.Bytes = Math.Max(0, (measure ?? Estimate)(entry.Ready)); bytes += entry.Bytes;
            published[key] = new(entry.Ready);
        }
        while (entries.Count > 1 && bytes > byteBudget) Evict(entries.MinBy(row => row.Value.Used).Key);
        if (published.Count > 128)
            foreach (var key in published.Where(row => !row.Value.TryGetTarget(out _)).Select(row => row.Key).ToArray()) published.Remove(key);
        while (published.Count > 128) published.Remove(published.Keys.First());
    }
    static long Estimate(BowelAssetSet assets)
    {
        // Source vertices, four-node skin links, managed array headers and small nine-node rigs.
        long VertexBytes(BowelAsset asset) => (long)asset.Source.Length * (Unsafe.SizeOf<Vertex>() + 64) + 1024;
        return (assets.Whole is { } whole ? VertexBytes(whole) : 0) + assets.Halves.Sum(VertexBytes);
    }
    void Evict(float key) { if (entries.Remove(key, out var entry)) bytes -= entry.Bytes; }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true;
            foreach (var entry in entries.Values)
                _ = entry.Work.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            entries.Clear(); published.Clear(); failed.Clear(); bytes = 0;
        }
    }
}
