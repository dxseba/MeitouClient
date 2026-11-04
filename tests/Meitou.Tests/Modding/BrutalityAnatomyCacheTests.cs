using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed class BrutalityAnatomyCacheTests
{
    [Fact]
    public void Full_fit_storage_is_byte_bounded_and_recently_reused_faces_survive_older_faces()
    {
        var a = Mesh(0); var b = Mesh(1); var c = Mesh(2);
        long slot = AnatomyFitCache<int>.EstimateBytes(a) + 256;
        var cache = new AnatomyFitCache<int>(slot * 2);
        Assert.Same(a, cache.GetOrCreate(1, () => a)); Assert.Same(b, cache.GetOrCreate(2, () => b));
        Assert.Same(a, cache.GetOrCreate(1, () => throw new InvalidOperationException("Current face must be reused.")));
        cache.GetOrCreate(3, () => c);
        Assert.Same(a, cache.GetOrCreate(1, () => throw new InvalidOperationException("LRU eviction removed a hot face.")));
        int rebuilt = 0;
        Assert.Same(b, cache.GetOrCreate(2, () => { rebuilt++; return b; })); Assert.Equal(1, rebuilt);
        for (int i = 10; i < 110; i++)
        {
            int current = i; cache.GetOrCreate(current, () => Mesh(current));
            Assert.InRange(cache.Bytes, 1, slot * 2); Assert.InRange(cache.Count, 1, 2);
        }
    }

    [Fact]
    public void An_oversized_fit_is_returned_unchanged_without_retaining_it_or_flushing_a_useful_smaller_fit()
    {
        var small = Mesh(0); var large = Mesh(1);
        large.SubMeshes[0].VertexData!.Buffers[1] = new(1, new byte[8192]);
        long slot = AnatomyFitCache<int>.EstimateBytes(small) + 256;
        var cache = new AnatomyFitCache<int>(slot);
        cache.GetOrCreate(1, () => small);
        int fits = 0;
        for (int i = 0; i < 2; i++) Assert.Same(large, cache.GetOrCreate(2, () => { fits++; return large; }));
        Assert.Equal(2, fits); Assert.Equal(1, cache.Count); Assert.InRange(cache.Bytes, 1, slot);
        Assert.Same(small, cache.GetOrCreate(1, () => throw new InvalidOperationException("Oversized fit displaced the working set.")));
    }

    static OgreMesh Mesh(float x) => MeshCut.FromTriangles([
        V(new(x, 1, 0)), V(new(x + 1, 1, 0)), V(new(x, 2, 0))]);
    static Vertex V(Vector3 p) => new() { Position = p, Normal = Vector3.UnitZ, Tangent = new(1, 0, 0, 1),
        Colour = Vector4.One, Weights = Vector4.UnitX };
}
