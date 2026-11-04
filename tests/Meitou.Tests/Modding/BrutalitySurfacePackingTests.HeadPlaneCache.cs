using System.Numerics;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Ogre;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalitySurfacePackingTests
{
    [Fact]
    public void Both_head_halves_and_later_frames_resolve_one_immutable_head_plane_once()
    {
        int projections = 0;
        var cache = new ResolvedHeadPlaneCache((body, rig, look, plane) => { projections++; return plane * 3; });
        var body = new OgreMesh(); var rig = Rig(); var look = Look(); var plane = new Vector4(1, .2f, 0, -.1f);
        var expected = cache.For(body, rig, look, plane);
        for (int frame = 0; frame < 1000; frame++)
        {
            Assert.Equal(expected, cache.For(body, rig, look, plane));
            Assert.Equal(expected, cache.For(body, rig, look, plane));
        }
        Assert.Equal(plane * 3, expected);
        Assert.Equal(1, projections);
        _ = cache.For(new OgreMesh(), rig, look, plane);
        _ = cache.For(body, Rig(), look, plane);
        _ = cache.For(body, rig, Look(), plane);
        _ = cache.For(body, rig, look, new(0, 1, 0, -.2f));
        Assert.Equal(5, projections);
    }

    [Fact]
    public void Changed_planes_and_cache_eviction_keep_the_same_projection_result()
    {
        int projections = 0;
        var cache = new ResolvedHeadPlaneCache((body, rig, look, plane) => { projections++; return new(plane.X, plane.Y, plane.Z, plane.W * 7); });
        var body = new OgreMesh(); var rig = Rig(); var look = Look();
        var first = new Vector4(1, 0, 0, -.25f);
        var expected = cache.For(body, rig, look, first);
        for (int i = 0; i < ResolvedHeadPlaneCache.PlanesPerAppearance + 2; i++)
        {
            var plane = new Vector4(0, 1, 0, i + 1);
            Assert.Equal(new Vector4(0, 1, 0, (i + 1) * 7), cache.For(body, rig, look, plane));
        }
        Assert.Equal(expected, cache.For(body, rig, look, first));
        int before = projections;
        cache.Clear();
        Assert.Equal(expected, cache.For(body, rig, look, first));
        Assert.Equal(before + 1, projections);
    }
}
