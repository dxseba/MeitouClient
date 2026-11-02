using System.Numerics;
using BepuPhysics;
using BepuUtilities.Memory;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Tests.Physics;

public class RagdollHullTests
{
    [Fact]
    public void A_translated_box_keeps_its_volume_centroid_and_analytic_inertia()
    {
        var translation = new Vector3(123.5f, -42, 7.25f);
        var points = Corners(new(2, 4, 6)).Select(point => point + translation).ToArray();
        var original = points.ToArray();
        var pool = new BufferPool();
        try
        {
            Assert.True(RagdollHull.TryCreate(points, pool, out var hull, out var centre));
            try
            {
                Assert.InRange(Vector3.Distance(translation, centre), 0, 0.0001f);
                var inertia = hull.ComputeInertia(3);
                AssertPositive(inertia);
                var tensor = inertia.InverseInertiaTensor;
                Assert.InRange(MathF.Abs(tensor.XX - 1f / 13), 0, 0.00001f);
                Assert.InRange(MathF.Abs(tensor.YY - 0.1f), 0, 0.00001f);
                Assert.InRange(MathF.Abs(tensor.ZZ - 0.2f), 0, 0.00001f);
                Assert.InRange(MathF.Abs(tensor.YX) + MathF.Abs(tensor.ZX) + MathF.Abs(tensor.ZY), 0, 0.00001f);
                Assert.Equal(original, points);
            }
            finally { hull.Dispose(pool); }
        }
        finally { pool.Clear(); }
    }

    [Fact]
    public void Nearly_coplanar_face_points_and_duplicates_still_form_a_finite_closed_collider()
    {
        Vector3[] points =
        [
            .. Corners(new(2, 2, 2)), new(1, 0, 0), new(1, 0.5f, 0), new(1.000001f, 0, 0.5f),
            new(0, 1, 0), new(0, 0.999999f, 0.5f), new(0.5f, 1, 0), new(1, 0, 0), Vector3.Zero,
        ];
        var pool = new BufferPool();
        try
        {
            Assert.True(RagdollHull.TryCreate(points, pool, out var hull, out var centre));
            try
            {
                Assert.True(Finite(centre));
                Assert.InRange(centre.Length(), 0, 0.0001f);
                Assert.True(hull.FaceToVertexIndicesStart.Length >= 4);
                for (int face = 0; face < hull.FaceToVertexIndicesStart.Length; face++)
                {
                    hull.GetVertexIndicesForFace(face, out var vertices);
                    Assert.True(vertices.Length >= 3);
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        hull.GetPoint(vertices[vertex], out var point);
                        Assert.True(Finite(point));
                    }
                }
                AssertPositive(hull.ComputeInertia(1));
            }
            finally { hull.Dispose(pool); }
        }
        finally { pool.Clear(); }
    }

    [Fact]
    public void Invalid_or_flat_clouds_are_rejected_without_allocating_native_blocks()
    {
        Vector3[]?[] invalid =
        [
            null, [], [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
            [Vector3.One, Vector3.One, Vector3.One, Vector3.One],
            [new(-1, 0, -1), new(-1, 0, 1), new(1, 0, -1), new(1, 0, 1)],
            [.. Corners(new(2, 2, 2)), new(float.NaN, 0, 0)],
            [.. Corners(new(2, 2, 2)), new(0, float.PositiveInfinity, 0)],
        ];
        var pool = new BufferPool();
        try
        {
            foreach (var points in invalid)
            {
                Assert.False(RagdollHull.TryCreate(points, pool, out var hull, out var centre));
                Assert.False(hull.Points.Allocated);
                Assert.False(hull.BoundingPlanes.Allocated);
                Assert.False(hull.FaceVertexIndices.Allocated);
                Assert.False(hull.FaceToVertexIndicesStart.Allocated);
                Assert.Equal(Vector3.Zero, centre);
                for (int power = 0; power <= 30; power++) Assert.Equal(0, pool.GetCapacityForPower(power));
            }
        }
        finally { pool.Clear(); }
    }

    internal static void AssertPositive(BodyInertia inertia)
    {
        var tensor = inertia.InverseInertiaTensor;
        float[] values = [inertia.InverseMass, tensor.XX, tensor.YX, tensor.YY, tensor.ZX, tensor.ZY, tensor.ZZ];
        Assert.All(values, value => Assert.True(float.IsFinite(value)));
        double xx = tensor.XX, yx = tensor.YX, yy = tensor.YY, zx = tensor.ZX, zy = tensor.ZY, zz = tensor.ZZ;
        double minor = xx * yy - yx * yx;
        double determinant = xx * (yy * zz - zy * zy) - yx * (yx * zz - zx * zy) + zx * (yx * zy - zx * yy);
        Assert.True(inertia.InverseMass > 0 && xx > 0 && minor > 0 && determinant > 0);
    }

    static Vector3[] Corners(Vector3 size) =>
        [.. from x in new[] { -1, 1 } from y in new[] { -1, 1 } from z in new[] { -1, 1 }
            select new Vector3(x * size.X / 2, y * size.Y / 2, z * size.Z / 2)];

    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
