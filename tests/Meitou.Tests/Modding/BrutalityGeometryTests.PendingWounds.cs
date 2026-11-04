using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Rendering;
using Meitou.Data.Characters;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void An_ordinary_wound_can_queue_and_stays_native_until_the_pending_fit_is_published_without_losing_newer_marks()
    {
        using var scene = new Scene();
        var done = new TaskCompletionSource<PreparedGeometry>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = new GeometryPreparationCache<int>(start: _ => done.Task);
        var mark = new Wound(9, .5f, Vector3.UnitZ, new(1, 0, .3f, .5f, WoundKind.Slash), 1, 1, 1);
        var state = State() with { Wounds = new([new BodyWoundEntry(-1, mark)]) };
        Assert.True(GeometryPreparations.CanPrepare(state));
        Assert.True(cache.ShouldDefer(1));
        var instance = new CharacterInstance(1, scene.Appearance, Vector3.Zero, 0, []);
        Assert.True(cache.Request(1, instance, 10, () => throw new InvalidOperationException("Controlled task owns this work.")));
        Assert.True(cache.IsPending(1));
        Assert.Null(BodyWoundRenderBridge.PendingGeometry(scene.Appearance, null, null));
        var latest = state with { Wounds = new([new BodyWoundEntry(-1, mark with { Dose = 2, Hits = 2, Age = 2 })]) };
        var prepared = new RuntimeCharacterGeometry(null, []);
        done.SetResult(new(prepared, 1));
        Assert.True(cache.IsPending(1)); Assert.True(cache.ShouldDefer(1));
        var output = new List<CharacterInstance>(); cache.Publish(output);
        Assert.Same(prepared, Assert.Single(output).Geometry);
        Assert.False(cache.IsPending(1)); Assert.False(cache.ShouldDefer(1));
        var generated = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, latest);
        Assert.NotNull(CharacterGeometryAdapter.SurfaceFor(generated.Retained));
        Assert.NotEqual(0u, generated.Retained.BodyFlags & NativeSurfaceShaders.WoundFlag);
        Assert.Equal(2, Assert.Single(latest.Wounds.Entries).Mark.Hits);
        Assert.True(GeometryPreparations.CanPrepare(latest));
    }

    [Fact]
    public void Pending_wound_geometry_reuses_only_the_same_nonretired_look_and_failed_jobs_do_not_hide_wounds_forever()
    {
        using var scene = new Scene();
        var geometry = new RuntimeCharacterGeometry();
        Assert.Same(geometry, BodyWoundRenderBridge.PendingGeometry(scene.Appearance, scene.Appearance, geometry));
        var changedLook = scene.Appearance.WithParts(scene.Appearance.Parts);
        Assert.Null(BodyWoundRenderBridge.PendingGeometry(changedLook, scene.Appearance, geometry));
        geometry.Retire();
        Assert.Null(BodyWoundRenderBridge.PendingGeometry(scene.Appearance, scene.Appearance, geometry));
        var failed = new TaskCompletionSource<PreparedGeometry>();
        using var cache = new GeometryPreparationCache<int>(start: _ => failed.Task);
        Assert.True(cache.Request(1, new(1, scene.Appearance, Vector3.Zero, 0, []), 1, () => default));
        failed.SetException(new InvalidDataException("synthetic fit failure"));
        Assert.False(cache.IsPending(1)); Assert.False(cache.ShouldDefer(1));
        var cut = State(head: new(new(1, 0, 0, 0), HeadCutMode.NeckDetach));
        Assert.False(GeometryPreparations.CanPrepare(cut));
    }
}
