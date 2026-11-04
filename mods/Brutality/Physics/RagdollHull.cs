using System.Numerics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using BepuHull = BepuPhysics.Collidables.ConvexHull;
using ManagedHull = Meitou.Data.Physics.ConvexHull;

namespace Meitou.Brutality.Physics;

/// <summary>Builds bounded, checked hull topology before handing any face indices to Bepu's native buffers.</summary>
internal static class RagdollHull
{
    const float Epsilon = 1e-6f, PlaneTolerance = 4e-6f;

    readonly record struct Prepared(Vector3[] Points, int[] Triangles, Vector3 Origin, float Scale);

    /// <summary>The outward triangles refer to the original input array. No unmanaged hull computation is involved.</summary>
    public static bool TryTriangles(Vector3[]? points, out int[] triangles)
    {
        triangles = [];
        if (!Prepare(points, out var prepared)) return false;
        triangles = prepared.Triangles;
        return true;
    }

    /// <summary>On success the caller owns the returned hull's buffers in <paramref name="pool"/>.</summary>
    public static bool TryCreate(Vector3[]? points, BufferPool pool, out BepuHull hull, out Vector3 centre)
    {
        hull = default;
        centre = default;
        if (pool is null || !Prepare(points, out var prepared)) return false;
        HullData data = default;
        BepuHull candidate = default;
        bool transferred = false;
        try
        {
            FillData(prepared, pool, ref data);
            // This overload processes checked face lists; it never calls Bepu 2.4's unchecked ComputeHull walker.
            ConvexHullHelper.CreateShape(prepared.Points, data, pool, out var normalizedCentre, out candidate);
            if (!Finite(normalizedCentre) || !Inside(prepared.Points, prepared.Triangles, normalizedCentre)
                || !ScaleBack(ref candidate, prepared.Scale)) return false;
            var at = prepared.Origin + normalizedCentre * prepared.Scale;
            if (!Finite(at)) return false;
            hull = candidate;
            centre = at;
            transferred = true;
            return true;
        }
        finally
        {
            Return(pool, ref data.OriginalVertexMapping);
            Return(pool, ref data.FaceVertexIndices);
            Return(pool, ref data.FaceStartIndices);
            if (!transferred)
            {
                Return(pool, ref candidate.Points);
                Return(pool, ref candidate.BoundingPlanes);
                Return(pool, ref candidate.FaceVertexIndices);
                Return(pool, ref candidate.FaceToVertexIndicesStart);
            }
        }
    }

    static bool Prepare(Vector3[]? source, out Prepared prepared)
    {
        prepared = default;
        if (source is not { Length: >= 4 } || source.Any(p => !Finite(p))) return false;
        var min = source.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = source.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        double dx = (double)max.X - min.X, dy = (double)max.Y - min.Y, dz = (double)max.Z - min.Z;
        double size = Math.Max(dx, Math.Max(dy, dz)) * 0.5;
        if (!double.IsFinite(size) || size <= 0 || size > float.MaxValue || (float)size <= 0) return false;
        var origin = new Vector3((float)(min.X + dx * 0.5), (float)(min.Y + dy * 0.5), (float)(min.Z + dz * 0.5));
        var normalized = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var p = source[i];
            normalized[i] = new((float)(((double)p.X - origin.X) / size),
                (float)(((double)p.Y - origin.Y) / size), (float)(((double)p.Z - origin.Z) / size));
            if (!Finite(normalized[i])) return false;
        }
        var triangles = ManagedHull.Triangles(normalized, Epsilon);
        if (!Validate(normalized, triangles)) return false;
        prepared = new(normalized, triangles, origin, (float)size);
        return true;
    }

    static bool Validate(Vector3[] points, int[] triangles)
    {
        if (triangles.Length < 12 || triangles.Length % 3 != 0 || triangles.Length > (2L * points.Length - 4) * 3) return false;
        var inside = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Length;
        var edges = new Dictionary<(int, int), (int Count, int Balance)>();
        var used = new bool[points.Length];
        double volume = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            if ((uint)a >= (uint)points.Length || (uint)b >= (uint)points.Length || (uint)c >= (uint)points.Length
                || a == b || a == c || b == c) return false;
            var normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            float length = normal.Length();
            if (!float.IsFinite(length) || length <= 1e-9f) return false;
            normal /= length;
            if (Vector3.Dot(normal, inside - points[a]) > 0)
            {
                (b, c) = (c, b);
                (triangles[i + 1], triangles[i + 2]) = (b, c);
                normal = -normal;
            }
            foreach (var p in points)
                if (Vector3.Dot(normal, p - points[a]) > PlaneTolerance) return false;
            if (!Edge(a, b, edges) || !Edge(b, c, edges) || !Edge(c, a, edges)) return false;
            used[a] = used[b] = used[c] = true;
            volume += Volume(points[a] - inside, points[b] - inside, points[c] - inside);
        }
        return double.IsFinite(volume) && volume > 1e-10
            && used.Count(x => x) - edges.Count + triangles.Length / 3 == 2
            && edges.Values.All(e => e.Count == 2 && e.Balance == 0);
    }

    static bool Edge(int a, int b, Dictionary<(int, int), (int Count, int Balance)> edges)
    {
        var key = a < b ? (a, b) : (b, a);
        edges.TryGetValue(key, out var old);
        int count = old.Count + 1;
        if (count > 2) return false;
        edges[key] = (count, old.Balance + (a < b ? 1 : -1));
        return true;
    }

    static double Volume(Vector3 a, Vector3 b, Vector3 c) =>
        a.X * ((double)b.Y * c.Z - (double)b.Z * c.Y)
        + a.Y * ((double)b.Z * c.X - (double)b.X * c.Z)
        + a.Z * ((double)b.X * c.Y - (double)b.Y * c.X);

    static bool Inside(Vector3[] points, int[] triangles, Vector3 candidate)
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = points[triangles[i]];
            var normal = Vector3.Cross(points[triangles[i + 1]] - a, points[triangles[i + 2]] - a);
            if (Vector3.Dot(normal, candidate - a) > PlaneTolerance * normal.Length()) return false;
        }
        return true;
    }

    static void FillData(Prepared prepared, BufferPool pool, ref HullData data)
    {
        var vertices = prepared.Triangles.Distinct().Order().ToArray();
        var remap = new int[prepared.Points.Length];
        pool.Take(vertices.Length, out data.OriginalVertexMapping);
        for (int i = 0; i < vertices.Length; i++)
        {
            remap[vertices[i]] = i;
            data.OriginalVertexMapping[i] = vertices[i];
        }
        pool.Take(prepared.Triangles.Length, out data.FaceVertexIndices);
        for (int i = 0; i < prepared.Triangles.Length; i++) data.FaceVertexIndices[i] = remap[prepared.Triangles[i]];
        pool.Take(prepared.Triangles.Length / 3, out data.FaceStartIndices);
        for (int i = 0; i < data.FaceStartIndices.Length; i++) data.FaceStartIndices[i] = i * 3;
    }

    static bool ScaleBack(ref BepuHull hull, float scale)
    {
        if (!hull.Points.Allocated || !hull.BoundingPlanes.Allocated || !hull.FaceVertexIndices.Allocated
            || !hull.FaceToVertexIndicesStart.Allocated) return false;
        var factor = new Vector<float>(scale);
        for (int i = 0; i < hull.Points.Length; i++)
        {
            ref var p = ref hull.Points[i];
            p.X *= factor;
            p.Y *= factor;
            p.Z *= factor;
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z)) return false;
        }
        var unused = new Vector<float>(float.MinValue);
        for (int i = 0; i < hull.BoundingPlanes.Length; i++)
        {
            ref var plane = ref hull.BoundingPlanes[i];
            plane.Offset = Vector.ConditionalSelect(Vector.Equals(plane.Offset, unused), plane.Offset, plane.Offset * factor);
            if (!Finite(plane.Normal.X) || !Finite(plane.Normal.Y) || !Finite(plane.Normal.Z) || !Finite(plane.Offset)) return false;
        }
        return true;
    }

    static void Return<T>(BufferPool pool, ref Buffer<T> buffer) where T : unmanaged
    {
        if (buffer.Allocated) pool.Return(ref buffer);
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    static bool Finite(Vector<float> vector)
    {
        for (int i = 0; i < Vector<float>.Count; i++) if (!float.IsFinite(vector[i])) return false;
        return true;
    }
}