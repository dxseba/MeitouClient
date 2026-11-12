using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

/// <summary>Samples the morphed head skin so fitted anatomy follows a tapering cranium.</summary>
internal sealed class StockCranialEnvelope(Vector3 minimum, Vector3 size, StockCranialEnvelope.Slice?[] slices)
{
    const int SliceCount = 20, Sectors = 32;
    const float Tau = 2 * MathF.PI;
    internal sealed record Slice(Vector2 Centre, float[] Radii);

    internal static StockCranialEnvelope Build(OgreMesh mesh, SkeletonRig rig, CharacterAppearance look,
        Vector3 minimum, Vector3 maximum)
    {
        var groups = MeshCut.BoneGroups(rig);
        var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        bool shaved = look.Character?.GetBool("shaved") == true;
        var size = maximum - minimum;
        foreach (var part in MeshCut.SourceModel(mesh, rig.BoneCount).Parts)
        {
            var morph = MeshCut.MorphDeltas(mesh, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, shaved);
            for (int i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                uint a = part.Indices[i], b = part.Indices[i + 1], c = part.Indices[i + 2];
                if (InHead(a, out var x) && InHead(b, out var y) && InHead(c, out var z)) triangles.Add((x, y, z));
            }

            bool InHead(uint index, out Vector3 point)
            {
                point = default;
                if (index >= part.Vertices.Length || MeshCut.Group(part.Vertices[index], groups) is not (6 or 7)) return false;
                point = part.Vertices[index].Position + morph[index];
                return Vector3.DistanceSquared(Vector3.Clamp(point, minimum, maximum), point) < .001f;
            }
        }

        var slices = new Slice?[SliceCount];
        var segments = new List<(Vector2 A, Vector2 B)>();
        for (int layer = 0; layer < slices.Length; layer++)
        {
            float height = minimum.Y + (layer + .5f) * size.Y / SliceCount;
            segments.Clear();
            foreach (var (a, b, c) in triangles)
            {
                if ((a.Y > height) == (b.Y > height))
                {
                    if ((b.Y > height) != (c.Y > height)) segments.Add((Crossing(a, c, height), Crossing(b, c, height)));
                }
                else
                    segments.Add((Crossing(a, b, height), (a.Y > height) == (c.Y > height) ? Crossing(b, c, height) : Crossing(a, c, height)));
            }
            if (segments.Count < 3) continue;
            var low = new Vector2(float.MaxValue); var high = -low;
            foreach (var (a, b) in segments)
            {
                low = Vector2.Min(low, Vector2.Min(a, b));
                high = Vector2.Max(high, Vector2.Max(a, b));
            }
            var centre = (low + high) * .5f;
            var radii = new float[Sectors];
            for (int sector = 0; sector < radii.Length; sector++)
            {
                var angle = Tau * sector / Sectors;
                var ray = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                foreach (var (a, b) in segments)
                {
                    var line = b - a;
                    float cross = Cross(ray, line);
                    if (MathF.Abs(cross) < 1e-6f) continue;
                    var start = a - centre;
                    float along = Cross(start, ray) / cross;
                    if (along is >= 0 and <= 1) radii[sector] = Math.Max(radii[sector], Cross(start, line) / cross);
                }
            }
            slices[layer] = new(centre, radii);
        }
        return new(minimum, size, slices);
    }

    internal Vector3 Contain(Vector3 point, float extra = 0)
    {
        var headCentre = minimum + size * .5f;
        var delta = new Vector2(point.X - headCentre.X, point.Z - headCentre.Z);
        float angle = MathF.Atan2(delta.Y, delta.X) * (Sectors / Tau);
        if (angle < 0) angle += Sectors;
        int sector = (int)angle;
        float height = Math.Clamp((point.Y - minimum.Y) / size.Y * SliceCount - .5f, 0, SliceCount - 1);
        int layer = (int)height;
        var first = slices[layer];
        var second = slices[Math.Min(layer + 1, SliceCount - 1)];
        if (first is null && second is null) return Inset(point);
        first ??= second!;
        second ??= first;
        float radial = delta.Length();
        var centre = Vector2.Lerp(first.Centre, second.Centre, height - layer);
        float radiusA = Radius(first), radiusB = Radius(second);
        if (radiusA <= .001f && radiusB <= .001f) return Inset(point);
        if (radiusA <= .001f) radiusA = radiusB;
        if (radiusB <= .001f) radiusB = radiusA;
        float radius = float.Lerp(radiusA, radiusB, height - layer);
        float clearance = Math.Min(.12f + extra, radius * .29f);
        if (radial > .0001f) delta *= Math.Clamp((radius - clearance) / radial, 0, 1);
        return new(centre.X + delta.X, point.Y, centre.Y + delta.Y);

        float Radius(Slice slice) => float.Lerp(slice.Radii[sector % Sectors], slice.Radii[(sector + 1) % Sectors], angle - sector);
        Vector3 Inset(Vector3 p) => new(headCentre.X + delta.X * .78f, p.Y, headCentre.Z + delta.Y * .78f);
    }

    static Vector2 Crossing(Vector3 a, Vector3 b, float y)
    {
        var point = Vector3.Lerp(a, b, (y - a.Y) / (b.Y - a.Y));
        return new(point.X, point.Z);
    }

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
