using System.Numerics;
using Meitou.Game;

namespace Meitou.Tests.Characters;

public class TorsoFragmentMotionTests
{
    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(1f, 0.24f, 0.2f)]
    [InlineData(1f, -0.2f, -0.15f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(0f, 0f, -1f)]
    [InlineData(-1f, 0f, 0f)]
    [InlineData(0.6f, 0.7f, 0.8f)]
    [InlineData(-0.8f, -0.7f, 0.6f)]
    public void Long_fragments_show_both_exposed_faces_upward_and_lie_along_the_same_ground_tangent(float x, float y, float z)
    {
        var normal = Vector3.Normalize(new Vector3(x, y, z));
        var up = Vector3.Normalize(Vector3.UnitY - normal * normal.Y);
        Vector3? forward = null;
        foreach (int side in new[] { -1, 1 })
        {
            var turn = TorsoFragmentMotion.OutwardTurn(Vector3.UnitY, normal, side);
            Assert.True(float.IsFinite(turn.LengthSquared()));
            var cutFace = Vector3.Transform(-side * normal, turn);
            Assert.InRange(Vector3.Distance(Vector3.UnitY, cutFace), 0, 0.00001f);
            var along = Vector3.Transform(up, turn);
            Assert.InRange(MathF.Abs(along.Y), 0, 0.00001f);
            if (forward is { } other) Assert.InRange(Vector3.Distance(other, along), 0, 0.00001f);
            forward = along;
        }
    }

    [Theory]
    [InlineData(40f, 1f)]
    [InlineData(40f, -1f)]
    [InlineData(60f, 1f)]
    [InlineData(60f, -1f)]
    public void Oblique_fragments_lie_flat_with_upward_exposure_even_from_a_leaning_pose(float degrees, float slopeSign)
    {
        float angle = degrees * MathF.PI / 180;
        var normal = new Vector3(0.6f * MathF.Cos(angle), slopeSign * MathF.Sin(angle), 0.8f * MathF.Cos(angle));
        var groundTangent = Vector3.Normalize(Vector3.Cross(TorsoFragmentMotion.Across(normal), Vector3.UnitY));
        foreach (var bodyUp in new[] { Vector3.UnitY, Vector3.Normalize(new Vector3(0.2f, 1, 0.1f)) })
            foreach (int side in new[] { -1, 1 })
            {
                var turn = TorsoFragmentMotion.LyingTurn(bodyUp, normal, side);
                Assert.True(float.IsFinite(turn.LengthSquared()));
                Assert.InRange(Vector3.Distance(groundTangent, Vector3.Transform(bodyUp, turn)), 0, 0.00001f);
                var cutFace = Vector3.Transform(-side * normal, turn);
                float dot = Vector3.Dot(normal, bodyUp);
                float exposure = MathF.Sqrt(1 - dot * dot);
                Assert.True(cutFace.Y > 0.1f);
                Assert.InRange(MathF.Abs(cutFace.Y - exposure), 0, 0.00001f);
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
    [InlineData(-1, 1f, 0f, 0f, 0f, false)]
    [InlineData(1, 1f, 0f, 0f, 0f, false)]
    [InlineData(-1, 0f, 0f, 1f, 0.8f, false)]
    [InlineData(1, 0f, 0f, -1f, -0.8f, false)]
    [InlineData(-1, 0.6f, 0.7f, 0.8f, -0.8f, false)]
    [InlineData(1, -0.8f, -0.7f, 0.6f, 0.8f, false)]
    [InlineData(-1, 0f, 0.8390996f, 1f, 0.8f, true)]
    [InlineData(1, 1f, -0.8390996f, 0f, -0.8f, true)]
    [InlineData(-1, 0.6f, 1.7320508f, 0.8f, -0.8f, true)]
    [InlineData(1, -0.8f, -1.7320508f, 0.6f, 0.8f, true)]
    public void Surface_bounds_keep_turned_offset_fragments_above_the_floor(int side, float x, float y, float z, float offset, bool lying)
    {
        var normal = Vector3.Normalize(new Vector3(x, y, z));
        var anchor = new Vector3(0, 10, 0) + TorsoFragmentMotion.Across(normal) * offset;
        var corners = new List<Vector3>();
        foreach (float cx in new[] { -2f, 2f })
            foreach (float cy in new[] { 0f, 20f })
                foreach (float cz in new[] { -1f, 1f }) corners.Add(new(cx, cy, cz));
        float Distance(Vector3 point) => Vector3.Dot(normal, point - anchor);
        var points = corners.Where(p => side * Distance(p) >= 0).ToList();
        // Each corner has three box edges. Include their plane intersections as the cut fragment's support points.
        for (int i = 0; i < corners.Count; i++)
            foreach (int bit in new[] { 1, 2, 4 })
            {
                int j = i ^ bit;
                if (j <= i) continue;
                float a = Distance(corners[i]), b = Distance(corners[j]);
                if (a * b < 0) points.Add(Vector3.Lerp(corners[i], corners[j], a / (a - b)));
            }
        Assert.NotEmpty(points);
        var pivot = TorsoFragmentMotion.LowestPivot(points.ToArray(), default);
        Assert.True(float.IsFinite(pivot.LengthSquared()));
        (Vector3 Point, float Radius)[] bounds = [.. points.Select(p => (p - pivot, 0.15f))];
        var finalTurn = lying ? TorsoFragmentMotion.LyingTurn(Vector3.UnitY, normal, side)
            : TorsoFragmentMotion.OutwardTurn(Vector3.UnitY, normal, side);
        foreach (float progress in new[] { 0f, 0.5f, 1f })
        {
            var turn = Quaternion.Slerp(Quaternion.Identity, finalTurn, progress);
            float floorLift = -TorsoFragmentMotion.Bottom(bounds, turn) + 0.1f;
            Assert.True(float.IsFinite(floorLift));
            foreach (var (point, radius) in bounds)
                Assert.True(Vector3.Transform(point, turn).Y + floorLift - radius >= 0.09999f);
        }
    }

    [Fact]
    public void Degenerate_up_vectors_and_axis_aligned_planes_still_produce_finite_rotations()
    {
        foreach (var normal in new[] { Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ })
            foreach (var up in new[] { Vector3.Zero, normal, new Vector3(float.NaN, 0, 0) })
            {
                foreach (var turn in new[] { TorsoFragmentMotion.OutwardTurn(up, normal, 1), TorsoFragmentMotion.LyingTurn(up, normal, 1) })
                {
                    Assert.True(float.IsFinite(turn.LengthSquared()));
                    Assert.InRange(turn.LengthSquared(), 0.99999f, 1.00001f);
                }
            }
    }
}
