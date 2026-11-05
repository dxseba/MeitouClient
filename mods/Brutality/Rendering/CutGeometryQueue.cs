using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Meitou.Brutality.Rendering;

/// <summary>One CPU cut per world, with a globally bounded worker and latest-request wins per owner. Rendering only polls completed tasks.</summary>
internal sealed class CutGeometryQueue<TOwner, TKey>(int capacity = 128,
    Func<Func<CharacterGeometryResult>, Task<CharacterGeometryResult>>? start = null) : IDisposable
    where TOwner : notnull where TKey : notnull
{
    sealed class Entry(TKey key, Func<CancellationToken, CharacterGeometryResult> work, long order)
    {
        public readonly TKey Key = key;
        public Func<CancellationToken, CharacterGeometryResult>? Work = work;
        public readonly long Order = order;
        public CharacterGeometryResult? Ready;
        public bool Failed;
    }
    sealed record Running(TOwner Owner, Entry Entry, Task<CharacterGeometryResult> Task, CancellationTokenSource Cancellation);
    readonly object gate = new();
    readonly Dictionary<TOwner, Entry> entries = [];
    readonly CancellationTokenSource lifetime = new();
    Running? running;
    long order;
    bool disposed;

    internal bool NeedsRequest(TOwner owner, TKey key)
    {
        lock (gate)
        {
            Complete();
            return !disposed && (!entries.TryGetValue(owner, out var entry) || !EqualityComparer<TKey>.Default.Equals(entry.Key, key)
                || entry.Ready is null && entry.Work is null && !entry.Failed && !ReferenceEquals(running?.Entry, entry));
        }
    }

    internal bool Request(TOwner owner, TKey key, Func<CancellationToken, CharacterGeometryResult> work)
    {
        lock (gate)
        {
            if (!NeedsRequest(owner, key)) return false;
            Remove(owner);
            while (entries.Count >= Math.Max(1, capacity)) Remove(entries.MinBy(pair => pair.Value.Order).Key);
            entries[owner] = new(key, work, order++);
            return true;
        }
    }

    internal bool TryPendingKey(TOwner owner, out TKey key)
    {
        lock (gate)
        {
            Complete(); key = default!;
            if (disposed || !entries.TryGetValue(owner, out var entry) || entry.Failed
                || entry.Work is null && entry.Ready is null && !ReferenceEquals(running?.Entry, entry)) return false;
            key = entry.Key;
            return true;
        }
    }

    internal bool TryTake(TOwner owner, TKey key, [NotNullWhen(true)] out CharacterGeometryResult? result)
    {
        lock (gate)
        {
            Complete(); result = null;
            if (disposed || !entries.TryGetValue(owner, out var entry) || !EqualityComparer<TKey>.Default.Equals(entry.Key, key)
                || entry.Ready is not { } ready) return false;
            result = ready; entry.Ready = null;
            return true;
        }
    }

    internal void Retain(IReadOnlySet<TOwner> owners)
    {
        lock (gate)
        {
            foreach (var owner in entries.Keys.Where(owner => !owners.Contains(owner)).ToArray()) Remove(owner);
            Complete();
        }
    }

    internal void Remove(TOwner owner)
    {
        lock (gate)
        {
            if (!entries.Remove(owner, out var entry)) return;
            if (entry.Ready is { } ready) CharacterGeometryAdapter.Retire(ready);
            if (ReferenceEquals(running?.Entry, entry)) running.Cancellation.Cancel();
        }
    }

    /// <summary>Start after the full draw list has supplied its latest keys, so an old queued revision cannot win that frame.</summary>
    internal bool StartNext()
    {
        lock (gate)
        {
            Complete();
            if (disposed || running is not null) return false;
            Entry? entry = null;
            TOwner owner = default!;
            foreach (var pair in entries)
                if (pair.Value.Work is not null && (entry is null || pair.Value.Order < entry.Order))
                { owner = pair.Key; entry = pair.Value; }
            if (entry?.Work is not { } work) return false;
            entry.Work = null;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            Task<CharacterGeometryResult> task;
            try { task = (start ?? CutGeometryWorker.Start)(() => work(cancellation.Token)); }
            catch (Exception error) { task = Task.FromException<CharacterGeometryResult>(error); }
            running = new(owner, entry, task, cancellation);
            return true;
        }
    }

    void Complete()
    {
        if (running is not { } active || !active.Task.IsCompleted) return;
        running = null; active.Cancellation.Dispose();
        bool current = !disposed && entries.TryGetValue(active.Owner, out var latest) && ReferenceEquals(latest, active.Entry);
        if (active.Task.IsCompletedSuccessfully)
        {
            if (!current) CharacterGeometryAdapter.Retire(active.Task.Result);
            else
            {
                // Only one unseen completion is retained: leaving the camera cannot accumulate full cut meshes in this queue.
                foreach (var entry in entries.Values)
                    if (entry.Ready is { } old) { CharacterGeometryAdapter.Retire(old); entry.Ready = null; }
                active.Entry.Ready = active.Task.Result;
            }
        }
        else
        {
            if (current) active.Entry.Failed = true;
            if (active.Task.Exception is { } error && error.GetBaseException() is not OperationCanceledException)
                Console.Error.WriteLine("Brutality cut geometry: " + error.GetBaseException().Message);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel();
            foreach (var entry in entries.Values) if (entry.Ready is { } ready) CharacterGeometryAdapter.Retire(ready);
            entries.Clear();
            if (running is { } active)
            {
                running = null;
                _ = active.Task.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully) CharacterGeometryAdapter.Retire(task.Result);
                    else _ = task.Exception;
                    active.Cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            lifetime.Dispose();
        }
    }
}

internal static class CutGeometryWorker
{
    sealed record Job(Func<CharacterGeometryResult> Work, TaskCompletionSource<CharacterGeometryResult> Result);
    static readonly ConcurrentQueue<Job> jobs = new();
    static int running;

    internal static Task<CharacterGeometryResult> Start(Func<CharacterGeometryResult> work)
    {
        var result = new TaskCompletionSource<CharacterGeometryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Enqueue(new(work, result));
        StartDrain();
        return result.Task;
    }

    static void StartDrain()
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return;
        _ = Task.Factory.StartNew(Drain, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    static void Drain()
    {
        Thread.CurrentThread.IsBackground = true;
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        do
        {
            while (jobs.TryDequeue(out var job))
            {
                try { job.Result.TrySetResult(job.Work()); }
                catch (OperationCanceledException error) { job.Result.TrySetCanceled(error.CancellationToken); }
                catch (Exception error) { job.Result.TrySetException(error); }
            }
            Volatile.Write(ref running, 0);
            // A producer racing the empty queue either starts another drain or hands its job to this one.
        } while (!jobs.IsEmpty && Interlocked.CompareExchange(ref running, 1, 0) == 0);
    }
}
