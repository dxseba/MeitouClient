using System.Numerics;

namespace Meitou.Game;

/// <summary>Placement of long cut fragments: a low pivot, parallel falls and upward-facing exposed planes.</summary>
static class TorsoFragmentMotion
{
    public static Vector3 LowestPivot(ReadOnlySpan<Vector3> points, Vector3 fallback)
    {
        if (points.IsEmpty) return fallback;
        float bottom = float.MaxValue;
        foreach (var point in points) if (float.IsFinite(point.LengthSquared())) bottom = MathF.Min(bottom, point.Y);
        if (bottom == float.MaxValue) return fallback;
        Vector3 sum = default;
        int count = 0;
        foreach (var point in points)
            if (float.IsFinite(point.LengthSquared()) && point.Y <= bottom + 0.75f) { sum += point; count++; }
        var pivot = sum / count;
        pivot.Y = bottom;
        return pivot;
    }

    public static Vector3 Across(Vector3 normal)
    {
        var ground = new Vector3(normal.X, 0, normal.Z);
        return ground.LengthSquared() > 1e-8f ? Vector3.Normalize(ground) : Vector3.UnitX;
    }

    /// <summary>Both fragments lie along the same ground tangent, with their outward cut normals pointing up.</summary>
    public static Quaternion OutwardTurn(Vector3 bodyUp, Vector3 planeNormal, int side)
    {
        var normal = Unit(planeNormal, Vector3.UnitX);
        bodyUp = Unit(bodyUp, Vector3.UnitY);
        var up = bodyUp - normal * Vector3.Dot(bodyUp, normal);
        if (up.LengthSquared() < 1e-8f)
        {
            var axis = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            up = axis - normal * Vector3.Dot(axis, normal);
        }
        up = Unit(up, Vector3.UnitZ);
        var forward = Vector3.Cross(normal, up);
        var targetUp = Unit(Vector3.Cross(Across(normal), Vector3.UnitY), Vector3.UnitZ);
        var targetNormal = side > 0 ? -Vector3.UnitY : Vector3.UnitY;
        var source = Frame(normal, up, forward);
        var target = Frame(targetNormal, targetUp, Vector3.Cross(targetNormal, targetUp));
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Matrix4x4.Transpose(source) * target));
    }

    public static float Bottom(ReadOnlySpan<(Vector3 Point, float Radius)> bounds, Quaternion rotation)
    {
        float bottom = 0;
        foreach (var (point, radius) in bounds)
            bottom = MathF.Min(bottom, Vector3.Transform(point, rotation).Y - radius);
        return bottom;
    }

    static Vector3 Unit(Vector3 vector, Vector3 fallback) => float.IsFinite(vector.LengthSquared()) && vector.LengthSquared() > 1e-8f ? Vector3.Normalize(vector) : fallback;
    static Matrix4x4 Frame(Vector3 x, Vector3 y, Vector3 z) => new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
}
