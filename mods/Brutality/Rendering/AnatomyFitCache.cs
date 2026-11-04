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
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existing))
            {
                order.Remove(existing); order.AddFirst(existing); return existing.Value.Mesh;
            }
            var mesh = create();
            long size = EstimateBytes(mesh) + Math.Max(0, keyBytes) + 256;
            // A valid oversized fit still renders at full quality; it is released after this CPU cut instead of displacing the whole working set.
            if (size > budget) return mesh;
            while (entries.Count >= capacity || bytes > budget - size)
            {
                var oldest = order.Last!; order.RemoveLast(); entries.Remove(oldest.Value.Key); bytes -= oldest.Value.Bytes;
            }
            var node = order.AddFirst(new Entry(key, mesh, size)); entries.Add(key, node); bytes += size;
            return mesh;
        }
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
