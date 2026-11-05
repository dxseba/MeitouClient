using Meitou.Brutality.Effects;

namespace Meitou.Tests.Modding;

public sealed class BrutalityBowelPreparationTests
{
    static BowelAssetSet Empty() => new(null, []);

    [Fact]
    public void First_and_duplicate_requests_never_wait_and_only_one_scale_build_is_in_flight()
    {
        var work = new List<TaskCompletionSource<BowelAssetSet>>();
        using var cache = new BowelAssetPreparation(_ => throw new InvalidOperationException("worker controlled by test"), start: _ =>
        {
            var task = new TaskCompletionSource<BowelAssetSet>(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Add(task); return task.Task;
        });
        Assert.False(cache.TryGet(1, out _)); Assert.False(cache.TryGet(1, out _)); Assert.False(cache.TryGet(2, out _));
        Assert.Single(work);
        var first = Empty(); work[0].SetResult(first);
        Assert.True(cache.TryGet(1, out var ready)); Assert.Same(first, ready);
        Assert.False(cache.TryGet(2, out _)); Assert.Equal(2, work.Count);
        work[1].SetResult(Empty()); Assert.True(cache.TryGet(2, out _));
    }

    [Fact]
    public void Ready_residency_is_byte_bounded_but_live_evicted_organs_reuse_the_same_immutable_plan()
    {
        int builds = 0;
        using var cache = new BowelAssetPreparation(_ => { builds++; return Empty(); }, byteBudget: 40, capacity: 2,
            start: work => Task.FromResult(work()), measure: _ => 32);
        Assert.True(cache.TryGet(1, out var first)); Assert.True(cache.TryGet(2, out _));
        Assert.Equal(1, cache.Count); Assert.Equal(32, cache.Bytes);
        Assert.True(cache.TryGet(1, out var reused)); Assert.Same(first, reused); Assert.Equal(2, builds);
        GC.KeepAlive(first);
    }

    [Fact]
    public void Failure_and_disposal_do_not_retry_or_publish_a_late_worker_result()
    {
        int starts = 0;
        using var failed = new BowelAssetPreparation(_ => Empty(), start: _ =>
        { starts++; return Task.FromException<BowelAssetSet>(new InvalidDataException("broken local anatomy")); });
        Assert.False(failed.TryGet(1, out _)); Assert.False(failed.TryGet(1, out _)); Assert.Equal(1, starts);
        var pending = new TaskCompletionSource<BowelAssetSet>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var disposed = new BowelAssetPreparation(_ => Empty(), start: _ => pending.Task);
        Assert.False(disposed.TryGet(1, out _)); disposed.Dispose(); pending.SetResult(Empty());
        Assert.False(disposed.TryGet(1, out _)); Assert.Equal(0, disposed.Count); Assert.Equal(0, disposed.Bytes);
    }
}
