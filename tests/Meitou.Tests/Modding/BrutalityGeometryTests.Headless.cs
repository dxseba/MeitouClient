using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Theory]
    [InlineData(SevereHeadWoundKind.Scalp)]
    [InlineData(SevereHeadWoundKind.BrokenSkull)]
    public void A_stale_forehead_wound_cannot_leave_relief_or_cached_brain_on_a_later_headless_body(SevereHeadWoundKind kind)
    {
        using var scene = new Scene(withAnatomy: true);
        var state = State() with { SevereHead = new(4, kind, 3, new(new(0, 18.65f, 0), Vector3.UnitZ, Vector3.UnitX)) };
        var before = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state);
        Assert.Contains(before.Retained.Parts!, part => part.Part.Field == "head-relief");
        Assert.Contains(before.Retained.Parts!.Where(part => part.Part.Field == "anatomy").SelectMany(part => NativeVertices(part.Mesh!)),
            vertex => vertex.Tangent.W == 6);

        var headless = scene.Appearance.WithParts(scene.Appearance.Parts);
        SetVisibility(headless, 0, true, false);
        var after = scene.Adapter.Build(headless, Rig, scene.Body, _ => null, state);
        Assert.DoesNotContain(after.Retained.Parts!, part => part.Part.Field == "head-relief");
        var info = Assert.IsType<NativeGeometryInfo>(CharacterGeometryAdapter.SurfaceFor(after.Retained));
        Assert.Null(info.HeadFrame); Assert.False(info.HeadReliefReady);
        Assert.False(after.FidelityGaps.HasFlag(GeometryFidelityGap.SevereHeadRelief));
        var inside = after.Retained.Parts!.Where(part => part.Part.Field == "anatomy").SelectMany(part => NativeVertices(part.Mesh!)).ToArray();
        Assert.DoesNotContain(inside, vertex => vertex.Tangent.W is 5 or 6 || vertex.Bones == 3 || vertex.Bones == 4);
        Assert.NotSame(before.Retained, after.Retained);
    }
}
