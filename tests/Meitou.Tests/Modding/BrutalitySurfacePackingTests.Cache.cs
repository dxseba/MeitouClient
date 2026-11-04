using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Brutality;
using Meitou.Brutality.Rendering.Surface;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalitySurfacePackingTests
{
    [Fact]
    public void Span_packing_matches_the_complete_header_and_clears_reused_storage_without_touching_neighbours()
    {
        var rig = Rig();
        var state = State(new(new(0, 1, 0, -12)), new LimbCutState(0, .4f, new(.2f, -.1f)));
        var spheres = Enumerable.Range(0, 64).Select(i => new Vector4(i, 2, 3, 4)).ToArray();
        var cuts = Enumerable.Range(0, 64).Select(i => new Vector4(i, 5, 6, 7)).ToArray();
        var head = Matrix4x4.CreateScale(3);
        var expected = NativeSurfaceBridge.Pack(rig, state, 0, 0xFEDCBA98, spheres, cuts, head);
        var guard = Matrix4x4.CreateTranslation(111, 222, 333);
        var storage = Enumerable.Repeat(guard, 40).ToArray();
        NativeSurfaceBridge.Pack(rig, state, 0, 0xFEDCBA98, spheres, cuts, head, storage.AsSpan(2, 36));
        Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(storage.AsSpan(2, 36)).ToArray());
        Assert.All(storage[..2].Concat(storage[38..]), value => Assert.Equal(guard, value));
        NativeSurfaceBridge.Pack(rig, State(), 2, 0, null, null, default, storage.AsSpan(2, 36));
        Assert.Equal(default, storage[2]);
        Assert.Equal(default, storage[4]);
        Assert.Equal(default, storage[5]);
        Assert.All(storage[6..38], value => Assert.Equal(default, value));
        Assert.Throws<ArgumentException>(() => NativeSurfaceBridge.Pack(rig, state, 0, 0, null, null, default, new Matrix4x4[35]));
    }

    [Fact]
    public void Marks_reuse_only_the_same_quantized_flow_and_race_projection_without_mutating_old_results()
    {
        var wound = new Wound(9, .3f, Vector3.UnitZ, new(1, 0, .2f, .8f, WoundKind.Slash), 12, 1, 3, 2, 100);
        var state = new BodyWoundState([new(-1, wound)]);
        var human = Look();
        var zero = NativeSurfaceBridge.Marks(state, human, 100, 1d / 60);
        var one = NativeSurfaceBridge.Marks(state, human, 101, 1d / 60);
        Assert.Equal(0, zero[0].Flow);
        Assert.Equal(1 / 12f, one[0].Flow);
        Assert.Same(one, NativeSurfaceBridge.Marks(state, human, 101, 1d / 60));
        Assert.Same(one, NativeSurfaceBridge.Marks(state, Look("Shek"), 102, 1d / 60));
        var two = NativeSurfaceBridge.Marks(state, human, 126, 1d / 60);
        Assert.NotSame(one, two);
        Assert.Equal(2 / 12f, two[0].Flow);
        Assert.Equal(1 / 12f, one[0].Flow);
        var hive = NativeSurfaceBridge.Marks(state, Look("Hive prince"), 126, 1d / 60);
        Assert.Equal(two[0].Size * .55f, hive[0].Size);
        Assert.Equal(two[0].Flow, hive[0].Flow);
        Assert.Same(two, NativeSurfaceBridge.Marks(state, human, 126, 1d / 60));
    }

    [Fact]
    public void Detached_heads_and_limbs_do_not_inherit_the_owners_ordinary_wound_marks()
    {
        Assert.True(NativeSurfaceBridge.HasOrdinaryMarks(0));
        Assert.True(NativeSurfaceBridge.HasOrdinaryMarks(1));
        foreach (int piece in new[] { 2, 3, 10, 11, 12, 13 }) Assert.False(NativeSurfaceBridge.HasOrdinaryMarks(piece));
    }

    [Fact]
    public void Stable_crowd_headers_and_mature_marks_have_bounded_allocations_after_warmup()
    {
        var rig = Rig();
        var wound = new Wound(9, .3f, Vector3.UnitZ, new(1, 0, .2f, .8f, WoundKind.Slash), 12, 1, 3, 2, 0);
        var state = State(new(new(0, 1, 0, -12)), new LimbCutState(0, .4f, new(.2f, -.1f))) with { Wounds = new([new(-1, wound)]) };
        var look = Look();
        var prefix = new Matrix4x4[36];
        var spheres = new Vector4[64];
        var cuts = new Vector4[64];
        for (int i = 0; i < 8; i++)
        {
            NativeSurfaceBridge.Pack(rig, state, 0, 1, spheres, cuts, default, prefix);
            _ = NativeSurfaceBridge.Marks(state.Wounds, look, 1000 + i, 1d / 60);
        }
        var marks = NativeSurfaceBridge.Marks(state.Wounds, look, 1008, 1d / 60);
        bool same = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
        {
            NativeSurfaceBridge.Pack(rig, state, i & 1, 1, spheres, cuts, default, prefix);
            same &= ReferenceEquals(marks, NativeSurfaceBridge.Marks(state.Wounds, look, 1010 + i, 1d / 60));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(same);
        Assert.InRange(allocated, 0, 4096);
    }
}
