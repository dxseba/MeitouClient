using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public async Task Warm_and_actual_native_geometry_share_one_fitted_mesh_without_cloning_the_body()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var scene = new Scene(withAnatomy: true);
        using var start = new ManualResetEventSlim();
        var library = scene.Adapter.Anatomy;
        var warm = Task.Run(() =>
        {
            start.Wait(cancellation);
            return GeometryPreparations.Prepare(library, Rig, scene.Body, scene.Appearance.Race, [], [], false, 0x800000, false);
        }, cancellation);
        var cut = Task.Run(() =>
        {
            start.Wait(cancellation);
            return scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, State());
        }, cancellation);
        start.Set();
        var prepared = await warm.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        var actual = await cut.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        Assert.Null(prepared.Geometry.BodyMesh);
        Assert.DoesNotContain(prepared.Geometry.Parts!, part => part.Part.Field == "anatomy");
        var actualPart = Assert.Single(actual.Retained.Parts!, part => part.Part.Field == "anatomy");
        Assert.Same(prepared.SourceAnatomy, actualPart.Mesh);
        Assert.NotNull(prepared.Geometry.Shaders);
        prepared.Geometry.Retire();
    }

    [Fact]
    public void Two_active_gender_fits_survive_warm_eviction_without_duplicate_upload_geometry_or_refits()
    {
        using var scene = new Scene(withAnatomy: true);
        var library = scene.Adapter.Anatomy;
        // Both real fits exceed this deliberately tiny byte budget: only actual published geometry may keep their source alive.
        NativeGeometry.ConfigureFitBudget(library, 1);
        var female = scene.Appearance.WithParts([]);
        typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.Female))!.SetValue(female, true);
        using var cache = new GeometryPreparationCache<bool>(1, 16, work => Task.FromResult(work()));
        var cuts = new List<RuntimeCharacterGeometry>();
        var expected = new List<WeakReference<OgreMesh>>();
        long largest = 0;
        foreach (var look in new[] { scene.Appearance, female })
        {
            Assert.True(cache.Request(look.Female, new(1, look, Vector3.Zero, 0, []), look.Female ? 2 : 1,
                () => GeometryPreparations.Prepare(library, Rig, scene.Body, look.Race, [], [], look.Female, 0x800000, false)));
            var pending = new List<CharacterInstance>(); cache.Publish(pending);
            Assert.DoesNotContain(Assert.Single(pending).Geometry!.Parts!, part => part.Part.Field == "anatomy");
            var cut = scene.Adapter.Build(look, Rig, scene.Body, _ => null,
                State(head: new(new(1, 0, 0, 0), HeadCutMode.DetachedSplit)));
            cuts.Add(cut.Retained);
            var source = Assert.IsType<OgreMesh>(cut.Surface!.FittedAnatomy);
            expected.Add(new(source)); largest = Math.Max(largest, AnatomyFitCache<int>.EstimateBytes(source));
            Assert.InRange(cache.Bytes, 1, largest); Assert.Equal(0, NativeGeometry.CachedFitBytes(library));
        }
        cache.Dispose();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        for (int i = 0; i < 8; i++)
        {
            int index = i % 2; var look = index == 0 ? scene.Appearance : female;
            var rebuilt = NativeGeometry.Build(library, look, Rig, scene.Body, default);
            Assert.True(expected[index].TryGetTarget(out var original));
            Assert.Same(original, rebuilt.Anatomy);
            Assert.Same(original, CharacterGeometryAdapter.SurfaceFor(cuts[index])!.FittedAnatomy);
            Assert.Equal(0, NativeGeometry.CachedFitBytes(library));
        }
        foreach (var geometry in cuts) geometry.Retire();
    }

    [Fact]
    public async Task A_warming_key_is_single_flight_without_blocking_a_different_cached_fit()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var mesh = MeshCut.FromTriangles(Box(new(-1), new(1), 1));
        var cache = new AnatomyFitCache<int>();
        cache.GetOrCreate(2, () => mesh);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int count = 0;
        var worker = Task.Run(() => cache.GetOrCreate(1, () =>
        {
            Interlocked.Increment(ref count); entered.Set(); release.Wait(cancellation); return mesh;
        }), cancellation);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), cancellation));
            var actual = Task.Run(() => cache.GetOrCreate(1, () => throw new InvalidOperationException("Duplicate live fit.")), cancellation);
            var cached = Task.Run(() => cache.GetOrCreate(2, () => throw new InvalidOperationException("Cached fit lost.")), cancellation);
            Assert.Same(mesh, await cached.WaitAsync(TimeSpan.FromSeconds(5), cancellation));
            release.Set();
            Assert.Same(mesh, await actual.WaitAsync(TimeSpan.FromSeconds(5), cancellation));
            Assert.Same(mesh, await worker.WaitAsync(TimeSpan.FromSeconds(5), cancellation));
            Assert.Equal(1, count);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Published_warm_geometry_can_share_its_fit_after_byte_lru_eviction()
    {
        var first = MeshCut.FromTriangles(Box(new(-1), new(1), 1));
        var second = MeshCut.FromTriangles(Box(new(-2), new(2), 1));
        long slot = AnatomyFitCache<int>.EstimateBytes(first) + 256;
        var cache = new AnatomyFitCache<int>(slot);
        cache.GetOrCreate(1, () => first); cache.SharePublished(1, first);
        cache.GetOrCreate(2, () => second);
        Assert.Same(first, cache.GetOrCreate(1, () => throw new InvalidOperationException("Published GPU source was refitted.")));
        Assert.InRange(cache.Bytes, 1, slot);
    }

    [Fact]
    public void Default_warm_residency_keeps_the_two_measured_common_fits_but_evicts_the_oldest_on_a_third()
    {
        // Encoded male/female fits measured in the six-human native preview; no game data is needed to verify residency policy.
        const long maleBytes = 76_138_316, femaleBytes = 75_794_528;
        using var cache = new GeometryPreparationCache<int>(start: work => Task.FromResult(work()));
        var instance = new CharacterInstance(1, null!, Vector3.Zero, 0, []);
        var male = new RuntimeCharacterGeometry(null, []); var female = new RuntimeCharacterGeometry(null, []);
        var third = new RuntimeCharacterGeometry(null, []);
        var output = new List<CharacterInstance>();
        Assert.True(cache.Request(1, instance, 1, () => new(male, maleBytes))); cache.Publish(output);
        Assert.True(cache.Request(2, instance, 2, () => new(female, femaleBytes))); output.Clear(); cache.Publish(output);
        Assert.Equal(2, output.Count); Assert.Equal(maleBytes + femaleBytes, cache.Bytes);
        Assert.False(male.IsRetired); Assert.False(female.IsRetired);
        Assert.True(cache.Request(3, instance, 3, () => new(third, maleBytes))); output.Clear(); cache.Publish(output);
        Assert.Equal(2, output.Count); Assert.InRange(cache.Bytes, 1, 192L * 1024 * 1024);
        Assert.True(male.IsRetired); Assert.False(female.IsRetired); Assert.False(third.IsRetired);
        Assert.Contains(output, item => ReferenceEquals(item.Geometry, female));
        Assert.Contains(output, item => ReferenceEquals(item.Geometry, third));
    }

    [Fact]
    public async Task Preparation_is_single_worker_per_frame_byte_bounded_resident_and_retires_pending_work_on_disposal()
    {
        var jobs = new List<(Func<PreparedGeometry> Work, TaskCompletionSource<PreparedGeometry> Done)>();
        Task<PreparedGeometry> Start(Func<PreparedGeometry> work)
        {
            var done = new TaskCompletionSource<PreparedGeometry>();
            jobs.Add((work, done)); return done.Task;
        }
        using var cache = new GeometryPreparationCache<int>(100, 16, Start);
        var instance = new CharacterInstance(1, null!, Vector3.Zero, 0, []);
        var first = new RuntimeCharacterGeometry(null, []); var second = new RuntimeCharacterGeometry(null, []);
        Assert.True(cache.Request(1, instance, 10, () => new(first, 80)));
        Assert.False(cache.Request(2, instance, 10, () => new(second, 80)));
        Assert.False(cache.Request(2, instance, 11, () => new(second, 80)));
        jobs[0].Done.SetResult(jobs[0].Work());
        var output = new List<CharacterInstance>(); cache.Publish(output);
        Assert.Same(first, Assert.Single(output).Geometry);
        output.Clear(); cache.Publish(output); Assert.Same(first, Assert.Single(output).Geometry);
        Assert.False(cache.Request(2, instance, 10, () => new(second, 80)));
        Assert.True(cache.Request(2, instance, 11, () => new(second, 80)));
        jobs[1].Done.SetResult(jobs[1].Work()); output.Clear(); cache.Publish(output);
        Assert.Same(second, Assert.Single(output).Geometry); Assert.True(first.IsRetired);
        Assert.Equal(1, cache.Count); Assert.Equal(80, cache.Bytes);
        Assert.False(cache.Request(1, instance, 12, () => throw new InvalidOperationException("Evicted fit restarted every frame.")));
        var last = new RuntimeCharacterGeometry(null, []);
        Assert.True(cache.Request(3, instance, 12, () => new(last, 200)));
        cache.Dispose(); jobs[2].Done.SetResult(jobs[2].Work());
        await jobs[2].Done.Task;
        Assert.True(last.IsRetired); Assert.True(second.IsRetired);
    }
}
