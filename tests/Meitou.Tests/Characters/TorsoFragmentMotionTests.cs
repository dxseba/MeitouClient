using System.Numerics;
using Meitou.Game;

namespace Meitou.Tests.Characters;

public class TorsoFragmentMotionTests
{
    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(1f, 0.24f, 0.2f)]
    [InlineData(1f, -0.2f, -0.15f)]
    public void Long_fragments_show_both_exposed_faces_upward_and_lie_along_the_same_ground_tangent(float x, float y, float z)
    {
        var normal = Vector3.Normalize(new Vector3(x, y, z));
        var up = Vector3.Normalize(Vector3.UnitY - normal * normal.Y);
        Vector3? forward = null;
        foreach (int side in new[] { -1, 1 })
        {
            var turn = TorsoFragmentMotion.OutwardTurn(Vector3.UnitY, normal, side);
            var cutFace = Vector3.Transform(-side * normal, turn);
            Assert.InRange(Vector3.Distance(Vector3.UnitY, cutFace), 0, 0.00001f);
            var along = Vector3.Transform(up, turn);
            Assert.InRange(MathF.Abs(along.Y), 0, 0.00001f);
            if (forward is { } other) Assert.InRange(Vector3.Distance(other, along), 0, 0.00001f);
            forward = along;
        }
    }

    [Fact]
    public void A_fragment_without_a_foot_pivots_on_its_cut_leg_end_instead_of_the_other_half()
    {
        Vector3[] retained = [new(1, 6, 0), new(1.5f, 6.2f, 1), new(1, 12, 0), new(0, 19, 0)];
        var pivot = TorsoFragmentMotion.LowestPivot(retained, Vector3.Zero);
        Assert.Equal(6, pivot.Y);
        Assert.InRange(pivot.X, 1, 1.5f);
        Assert.Equal(Vector3.UnitY, TorsoFragmentMotion.LowestPivot([], Vector3.UnitY));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Surface_bounds_keep_both_turned_fragments_above_the_floor(int side)
    {
        var points = new List<Vector3>();
        foreach (float x in new[] { side > 0 ? 0f : -2f, side > 0 ? 2f : 0f })
            foreach (float y in new[] { 0f, 20f })
                foreach (float z in new[] { -1f, 1f }) points.Add(new(x, y, z));
        var pivot = TorsoFragmentMotion.LowestPivot(points.ToArray(), default);
        (Vector3 Point, float Radius)[] bounds = [.. points.Select(p => (p - pivot, 0.15f))];
        var turn = TorsoFragmentMotion.OutwardTurn(Vector3.UnitY, Vector3.UnitX, side);
        float floorLift = -TorsoFragmentMotion.Bottom(bounds, turn) + 0.1f;
        foreach (var (point, radius) in bounds)
            Assert.True(Vector3.Transform(point, turn).Y + floorLift - radius >= 0.09999f);
    }

    [Fact]
    public void Degenerate_up_vectors_and_axis_aligned_planes_still_produce_finite_rotations()
    {
        foreach (var normal in new[] { Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ })
            foreach (var up in new[] { Vector3.Zero, normal, new Vector3(float.NaN, 0, 0) })
            {
                var turn = TorsoFragmentMotion.OutwardTurn(up, normal, 1);
                Assert.True(float.IsFinite(turn.LengthSquared()));
                Assert.InRange(turn.LengthSquared(), 0.99999f, 1.00001f);
            }
    }
}
