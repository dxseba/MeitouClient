using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void A_cut_worker_reads_captured_inputs_and_publishes_under_the_original_look_only_after_completion()
    {
        using var scene = new Scene();
        var part = new CharacterPart { Record = scene.Appearance.Race, Mesh = "cloth.mesh", Field = "cloth", Mode = AttachMode.SharedSkeleton };
        scene.Appearance.Parts.Add(part);
        scene.Appearance.PoseWeights["head_size"] = .4f;
        var state = State(head: new(new(1, 0, 0, 0), HeadCutMode.DetachedSplit));
        var key = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, state, default);
        int resolved = 0;
        var input = CharacterGeometryAdapter.Capture(key, state, _ => { resolved++; return scene.Body; });
        Assert.Equal(1, resolved); Assert.Same(scene.Appearance, input.Key.Appearance);
        scene.Appearance.Parts.Clear(); scene.Appearance.PoseWeights["head_size"] = .9f;
        Assert.Same(part, Assert.Single(input.Appearance.Parts)); Assert.Equal(.4f, input.Appearance.PoseWeights["head_size"]);
        var done = new TaskCompletionSource<CharacterGeometryResult>();
        Func<CharacterGeometryResult>? work = null;
        using var queue = new CutGeometryQueue<int, CharacterGeometryAdapter.BuildKey>(start: build => { work = build; return done.Task; });
        Assert.True(queue.Request(1, key, token => scene.Adapter.BuildUnpublished(input, token)));
        Assert.True(queue.StartNext());
        Assert.False(queue.TryTake(1, key, out _));
        Assert.False(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, state, out _, default(CharacterGeometryComponents)));
        Assert.False(queue.Request(1, key, _ => throw new InvalidOperationException("A pending exact cut must not be queued twice.")));
        var completed = work!(); done.SetResult(completed);
        Assert.True(queue.TryTake(1, key, out var ready)); Assert.Same(completed, ready);
        var published = scene.Adapter.Publish(key, ready);
        Assert.True(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, state, out var cached, default(CharacterGeometryComponents)));
        Assert.Same(published, cached);
        Assert.False(scene.Adapter.TryCached(input.Appearance, Rig, scene.Body, state, out _));
        Assert.Equal(2, published.Detached.Count); Assert.Equal(1, resolved);
        Assert.All(published.Detached, piece => Assert.NotNull(piece.Geometry.BodyMesh));
    }

    [Fact]
    public void New_head_limb_and_torso_revisions_supersede_pending_work_without_publishing_the_old_topology()
    {
        using var scene = new Scene();
        var first = State(head: new(new(1, 0, 0, 0), HeadCutMode.NeckDetach));
        var second = first with { Limbs = new[] { new LimbCutState(0, .4f, Vector2.Zero) } };
        var third = second with { Torso = new TorsoBisectionState(new(0, 1, 0, -13)) };
        var key1 = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, first, default);
        var key2 = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, second, default);
        var key3 = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, third, default);
        var tasks = new List<(Func<CharacterGeometryResult> Work, TaskCompletionSource<CharacterGeometryResult> Done)>();
        using var queue = new CutGeometryQueue<int, CharacterGeometryAdapter.BuildKey>(start: build =>
        {
            var done = new TaskCompletionSource<CharacterGeometryResult>(); tasks.Add((build, done)); return done.Task;
        });
        var stale = CutResult();
        Assert.True(queue.Request(1, key1, _ => stale)); Assert.True(queue.StartNext());
        Assert.True(queue.Request(1, key2, _ => throw new InvalidOperationException("Superseded limb revision must never start.")));
        var newest = CharacterGeometryAdapter.Capture(key3, third, _ => null);
        Assert.True(queue.Request(1, key3, token => scene.Adapter.BuildUnpublished(newest, token)));
        Assert.False(queue.StartNext()); Assert.False(queue.TryTake(1, key3, out _));
        tasks[0].Done.SetResult(stale);
        Assert.False(queue.TryTake(1, key1, out _)); Assert.True(stale.Retained.IsRetired);
        Assert.True(queue.StartNext()); Assert.Equal(2, tasks.Count);
        tasks[1].Done.SetResult(tasks[1].Work());
        Assert.True(queue.TryTake(1, key3, out var ready));
        Assert.Contains(ready.Detached, piece => piece.StableKey == 1);
        Assert.Contains(ready.Detached, piece => piece.StableKey == 2);
        Assert.Contains(ready.Detached, piece => piece.StableKey == 10);
        Assert.False(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, first, out _));
        Assert.False(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, second, out _));
        scene.Adapter.Publish(key3, ready);
        Assert.True(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, third, out var final, default(CharacterGeometryComponents)));
        Assert.Same(ready, final);
    }

    [Fact]
    public void A_different_look_or_physical_island_map_cannot_consume_a_previous_cut_completion()
    {
        using var scene = new Scene();
        var state = State(limbs: new[] { new LimbCutState(0, .5f, Vector2.Zero) });
        var components = new CharacterGeometryComponents(Retained: new int[Rig.BoneCount]);
        var key = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, state, components);
        var otherLook = CharacterGeometryAdapter.RequestKey(scene.Appearance.WithParts([]), Rig, scene.Body, state, components);
        var otherIslands = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, state,
            new CharacterGeometryComponents(Retained: new int[Rig.BoneCount]));
        using var queue = new CutGeometryQueue<int, CharacterGeometryAdapter.BuildKey>(start: build => Task.FromResult(build()));
        var old = CutResult(); queue.Request(1, key, _ => old); queue.StartNext();
        Assert.False(queue.TryTake(1, otherLook, out _)); Assert.False(queue.TryTake(1, otherIslands, out _));
        Assert.True(queue.NeedsRequest(1, otherLook)); queue.Request(1, otherLook, _ => CutResult());
        Assert.True(old.Retained.IsRetired);
        Assert.False(queue.TryTake(1, key, out _));
    }

    [Fact]
    public void World_cut_queue_disposal_cancels_work_and_retires_late_results_without_waiting_for_the_worker()
    {
        var done = new TaskCompletionSource<CharacterGeometryResult>();
        Func<CharacterGeometryResult>? work = null;
        using var queue = new CutGeometryQueue<int, int>(start: build => { work = build; return done.Task; });
        CancellationToken observed = default;
        var late = CutResult();
        queue.Request(1, 1, token => { observed = token; return late; }); queue.StartNext();
        Assert.Same(late, work!()); Assert.False(observed.IsCancellationRequested);
        queue.Dispose(); Assert.True(observed.IsCancellationRequested);
        Assert.False(done.Task.IsCompleted); Assert.False(queue.NeedsRequest(2, 2));
        Assert.False(queue.StartNext()); Assert.False(queue.TryTake(1, 1, out _));
        done.SetResult(late);
        Assert.True(late.Retained.IsRetired); Assert.All(late.Detached, piece => Assert.True(piece.Geometry.IsRetired));
        queue.Dispose();
    }

    [Fact]
    public void Cut_queue_bounds_pending_owners_and_unseen_results_and_does_not_retry_a_failed_key_every_frame()
    {
        var tasks = new List<TaskCompletionSource<CharacterGeometryResult>>();
        using var queue = new CutGeometryQueue<int, int>(capacity: 2, start: _ =>
        { var done = new TaskCompletionSource<CharacterGeometryResult>(); tasks.Add(done); return done.Task; });
        queue.Request(1, 1, _ => CutResult()); queue.Request(2, 2, _ => CutResult()); queue.Request(3, 3, _ => CutResult());
        Assert.True(queue.NeedsRequest(1, 1)); Assert.False(queue.NeedsRequest(2, 2));
        queue.StartNext(); var first = CutResult(); tasks[0].SetResult(first);
        queue.StartNext(); var second = CutResult(); tasks[1].SetResult(second);
        Assert.True(queue.TryTake(3, 3, out var newest)); Assert.Same(second, newest); Assert.True(first.Retained.IsRetired);
        queue.Request(4, 4, _ => CutResult()); queue.StartNext(); tasks[2].SetException(new InvalidDataException("synthetic cut failure"));
        Assert.False(queue.TryTake(4, 4, out _)); Assert.False(queue.NeedsRequest(4, 4));
        Assert.True(queue.NeedsRequest(4, 5));
        queue.Retain(new HashSet<int>()); Assert.True(queue.NeedsRequest(4, 4));
    }

    [Fact]
    public void Native_head_fallback_is_replaced_only_by_the_exact_live_published_head_topology()
    {
        using var scene = new Scene();
        var state = State(head: new(new(1, 0, 0, 0), HeadCutMode.NeckDetach));
        var key = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, state, default);
        var result = CutResult();
        var frame = new GoreCharacterFrame(default, scene.Appearance, state, null, []);
        Assert.True(RenderBridge.HasPublishedHead(key, result, frame));
        Assert.True(RenderBridge.HasPublishedHead(key, result, frame with { State = state with { Experience = 40, Progress = .4f } }));
        Assert.False(RenderBridge.HasPublishedHead(key, result, frame with { Appearance = scene.Appearance.WithParts([]) }));
        Assert.False(RenderBridge.HasPublishedHead(key, result, frame with { State = state with { Head = new HeadCutState(new(0, 1, 0, .2f), HeadCutMode.DetachedSplit) } }));
        Assert.False(RenderBridge.HasPublishedHead(key, result, frame with { State = state with { Limbs = new[] { new LimbCutState(0, .5f, Vector2.Zero) } } }));
        var newIslands = PhysicalPose(); newIslands.Components = new int[Rig.BoneCount];
        Assert.False(RenderBridge.HasPublishedHead(key, result, frame with { Retained = newIslands }));
        Assert.False(RenderBridge.HasPublishedHead(key, result with { Detached = [] }, frame));
        result.Detached[0].Geometry.Retire(); Assert.False(RenderBridge.HasPublishedHead(key, result, frame));
    }

    [Fact]
    public void Severe_and_deep_abdomen_pending_work_cannot_be_bypassed_by_the_ordinary_wound_bridge()
    {
        using var scene = new Scene();
        var severe = new SevereHeadWoundState(4, SevereHeadWoundKind.Scalp, 3, new(new(0, 18.65f, 0), Vector3.UnitZ, Vector3.UnitX));
        var state = State() with { SevereHead = severe };
        var key = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, state, default);
        var done = new TaskCompletionSource<CharacterGeometryResult>();
        using var queue = new CutGeometryQueue<int, CharacterGeometryAdapter.BuildKey>(start: _ => done.Task);
        queue.Request(1, key, _ => CutResult()); queue.StartNext();
        Assert.True(queue.TryPendingKey(1, out var pending));
        Assert.True(RenderBridge.MatchesPendingCut(pending, scene.Appearance, state));
        Assert.True(RenderBridge.MatchesPendingCut(pending, scene.Appearance, state with { SevereHead = severe with { HeavyHits = 8 } }));
        var deep = state with { Wounds = new([], 10) };
        Assert.False(RenderBridge.MatchesPendingCut(pending, scene.Appearance, deep));
        var previous = new RuntimeCharacterGeometry();
        Assert.Same(previous, BodyWoundRenderBridge.CompatiblePendingGeometry(scene.Appearance, scene.Appearance, previous,
            severe with { HeavyHits = 0 }, severe with { HeavyHits = 0 }, false, false));
        Assert.Null(BodyWoundRenderBridge.CompatiblePendingGeometry(scene.Appearance, scene.Appearance, previous,
            default(SevereHeadWoundState), severe with { HeavyHits = 0 }, false, false));
        Assert.Null(BodyWoundRenderBridge.CompatiblePendingGeometry(scene.Appearance, scene.Appearance, previous,
            severe with { HeavyHits = 0 }, severe with { HeavyHits = 0 }, false, true));
        done.SetResult(CutResult()); Assert.True(queue.TryPendingKey(1, out pending));
        Assert.True(queue.TryTake(1, key, out _)); Assert.False(queue.TryPendingKey(1, out _));
        var deepKey = CharacterGeometryAdapter.RequestKey(scene.Appearance, Rig, scene.Body, deep, default);
        queue.Request(1, deepKey, _ => CutResult());
        Assert.True(queue.TryPendingKey(1, out pending)); Assert.True(RenderBridge.MatchesPendingCut(pending, scene.Appearance, deep));
    }

    [Fact]
    public void Idle_cut_queue_frames_are_safe_before_work_after_consumption_and_after_owner_removal()
    {
        int started = 0;
        using var queue = new CutGeometryQueue<int, int>(start: build => { started++; return Task.FromResult(build()); });
        for (int frame = 0; frame < 8; frame++)
        {
            queue.Retain(new HashSet<int>());
            Assert.False(queue.StartNext()); Assert.False(queue.TryTake(1, 1, out _));
        }
        var first = CutResult();
        Assert.True(queue.Request(1, 1, _ => first)); Assert.True(queue.StartNext());
        Assert.True(queue.TryTake(1, 1, out var completed)); Assert.Same(first, completed);
        for (int frame = 0; frame < 8; frame++)
        {
            queue.Retain(new HashSet<int> { 1 });
            Assert.False(queue.StartNext()); Assert.False(queue.TryPendingKey(1, out _));
        }
        queue.Remove(1);
        for (int frame = 0; frame < 8; frame++) Assert.False(queue.StartNext());
        Assert.True(queue.Request(2, 2, _ => CutResult())); Assert.True(queue.StartNext());
        queue.Retain(new HashSet<int>());
        for (int frame = 0; frame < 8; frame++) Assert.False(queue.StartNext());
        Assert.Equal(2, started);
    }

    static CharacterGeometryResult CutResult() => new(new RuntimeCharacterGeometry(),
        new[] { new GeometryFragment(GeometryFragmentKind.HeadPositive, 2, -1, new RuntimeCharacterGeometry(), Vector3.Zero, Vector3.One, default) },
        Vector3.Zero, Vector3.One, GeometryFidelityGap.None);
}
