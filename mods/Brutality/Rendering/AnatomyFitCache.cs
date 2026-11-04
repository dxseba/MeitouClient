using System.Runtime.CompilerServices;
using Meitou.Data.Ogre;

namespace Meitou.Brutality.Rendering;

/// <summary>Full fitted anatomy is temporary input to exposure/cutting. Keep only a bounded working set, independently of live appearance lifetimes.</summary>
internal sealed class AnatomyFitCache<TKey> where TKey : notnull
{
    public const long DefaultBudgetBytes = 96L * 1024 * 1024;
    sealed record Entry(TKey Key, OgreMesh? Mesh, long Bytes);
    readonly object gate = new();
    readonly long budget;
    readonly int capacity;
    readonly Dictionary<TKey, LinkedListNode<Entry>> entries = [];
    readonly LinkedList<Entry> order = [];
    readonly Dictionary<TKey, Lazy<OgreMesh?>> pending = [];
    readonly Dictionary<TKey, WeakReference<OgreMesh>> published = [];
    long bytes;
    public AnatomyFitCache(long byteBudget = DefaultBudgetBytes, int entryLimit = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryLimit);
        budget = byteBudget; capacity = entryLimit;
    }
    public long Bytes { get { lock (gate) return bytes; } }
    public int Count { get { lock (gate) return entries.Count; } }

    public OgreMesh? GetOrCreate(TKey key, Func<OgreMesh?> create, long keyBytes = 0)
    {
        Lazy<OgreMesh?> work;
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existing))
            { order.Remove(existing); order.AddFirst(existing); return existing.Value.Mesh; }
            if (published.TryGetValue(key, out var weak) && weak.TryGetTarget(out var shared))
            { Store(key, shared, keyBytes); return shared; }
            if (!pending.TryGetValue(key, out work!)) pending.Add(key, work = new(create, LazyThreadSafetyMode.ExecutionAndPublication));
        }
        OgreMesh? mesh;
        try { mesh = work.Value; }
        catch
        {
            lock (gate) if (pending.TryGetValue(key, out var failed) && ReferenceEquals(failed, work)) pending.Remove(key);
            throw;
        }
        lock (gate)
        {
            if (pending.TryGetValue(key, out var completed) && ReferenceEquals(completed, work)) pending.Remove(key);
            if (!entries.ContainsKey(key)) Store(key, mesh, keyBytes);
        }
        return mesh;
    }

    /// <summary>Published preparation geometry owns this fit. A weak lookup permits reuse after LRU eviction without retaining extra full meshes.</summary>
    public void SharePublished(TKey key, OgreMesh? mesh)
    {
        if (mesh is null) return;
        lock (gate)
        {
            if (published.Count >= 32 && !published.ContainsKey(key))
            {
                foreach (var dead in published.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) published.Remove(dead);
                if (published.Count >= 32) published.Remove(published.Keys.First());
            }
            published[key] = new(mesh);
        }
    }

    void Store(TKey key, OgreMesh? mesh, long keyBytes)
    {
        long size = EstimateBytes(mesh) + Math.Max(0, keyBytes) + 256;
        if (size > budget) return;
        while (entries.Count >= capacity || bytes > budget - size)
        {
            var oldest = order.Last!; order.RemoveLast(); entries.Remove(oldest.Value.Key); bytes -= oldest.Value.Bytes;
        }
        var node = order.AddFirst(new Entry(key, mesh, size)); entries.Add(key, node); bytes += size;
    }

    /// <summary>Conservative retained footprint: actual encoded buffers and list capacities, counting shared buffers only once.</summary>
    public static long EstimateBytes(OgreMesh? mesh)
    {
        if (mesh is null) return 0;
        long size = 512 + mesh.BoneAssignments.Capacity * (long)Unsafe.SizeOf<OgreBoneAssignment>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Vertices(OgreVertexData? data)
        {
            if (data is null || !seen.Add(data)) return;
            size += 128 + data.Elements.Capacity * (long)IntPtr.Size;
            foreach (var buffer in data.Buffers.Values)
                if (seen.Add(buffer.Data)) size += buffer.Data.LongLength + 64;
        }
        void Indices(OgreIndexBuffer? indices)
        {
            if (indices is not null && seen.Add(indices.Indices)) size += indices.Indices.LongLength * sizeof(uint) + 64;
        }
        Vertices(mesh.SharedVertexData);
        foreach (var sub in mesh.SubMeshes)
        {
            size += 256 + sub.BoneAssignments.Capacity * (long)Unsafe.SizeOf<OgreBoneAssignment>();
            Vertices(sub.VertexData); Indices(sub.Indices);
        }
        foreach (var pose in mesh.Poses) size += 128 + pose.Vertices.Capacity * (long)Unsafe.SizeOf<OgrePoseVertex>();
        if (mesh.Lod is { } lod)
            foreach (var level in lod.Levels)
            {
                size += 128;
                foreach (var face in level.Faces) Indices(face.Buffer);
            }
        return size;
    }
}
