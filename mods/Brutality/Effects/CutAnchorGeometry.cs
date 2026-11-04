using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Brutality.Effects;

/// <summary>Small persistent bind-space cut-rim samples, reposed with the exact draw-list transforms every frame.</summary>
internal sealed class CutAnchorGeometry
{
    public const int SampleLimit = 48, CacheLimit = 256;
    readonly Dictionary<(RuntimeCharacterGeometry Geometry, Vector4 Plane, int Side), Vertex[]> cached = [];
    readonly Queue<(RuntimeCharacterGeometry Geometry, Vector4 Plane, int Side)> order = [];
    public int CacheCount => cached.Count;

    public Vertex[] Samples(RuntimeCharacterGeometry geometry, Vector4 plane, int keepSide, SkeletonRig rig, CharacterAppearance appearance)
    {
        var key = (geometry, plane, keepSide);
        if (cached.TryGetValue(key, out var known)) return known;
        var normal = -new Vector3(plane.X, plane.Y, plane.Z) * keepSide;
        var candidates = new List<Vertex>();
        if (normal.LengthSquared() < 1e-10f)
            foreach (var part in geometry.Parts ?? [])
                if (part.Mesh is not null && part.Part.Field == "head-relief")
                    candidates.AddRange(Model.Build(part.Mesh, rig.BoneCount).Parts.SelectMany(p => p.Vertices));
        foreach (var part in geometry.Parts ?? [])
        {
            if (part.Mesh is null || part.Part.Field is not ("wound-section" or "mechanical-section")) continue;
            foreach (var vertex in Model.Build(part.Mesh, rig.BoneCount).Parts.SelectMany(p => p.Vertices))
                if (MathF.Abs(Vector4.Dot(plane, new(vertex.Position, 1))) < .09f && Vector3.Dot(vertex.Normal, normal) > .5f)
                    candidates.Add(vertex);
        }
        if (candidates.Count == 0 && geometry.BodyMesh is { } body)
            foreach (var part in Model.Build(body, rig.BoneCount).Parts)
            {
                var morph = Rendering.MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, appearance.PoseWeights, appearance.Character?.GetBool("shaved") == true);
                for (int i = 0; i < part.Vertices.Length; i++)
                {
                    var vertex = part.Vertices[i] with { Position = part.Vertices[i].Position + morph[i], Normal = normal };
                    if (MathF.Abs(Vector4.Dot(plane, new(vertex.Position, 1))) < .025f) candidates.Add(vertex);
                }
            }
        var seen = new HashSet<Vector3>();
        var rim = candidates.Where(p => seen.Add(new(MathF.Round(p.Position.X, 4), MathF.Round(p.Position.Y, 4), MathF.Round(p.Position.Z, 4)))).ToArray();
        if (rim.Length > SampleLimit) rim = Enumerable.Range(0, SampleLimit).Select(i => rim[i * rim.Length / SampleLimit]).ToArray();
        if (cached.Count >= CacheLimit) cached.Remove(order.Dequeue());
        order.Enqueue(key);
        return cached[key] = rim;
    }

    public static CutFluidContact? Pose(ReadOnlySpan<Vertex> rim, ReadOnlySpan<Matrix4x4> bones, Matrix4x4 world)
    {
        var centre = Vector3.Zero; var normal = Vector3.Zero;
        int count = Math.Min(rim.Length, SampleLimit), valid = 0;
        for (int i = 0; i < count; i++)
        {
            var vertex = rim[i]; var point = Vector3.Zero; var direction = Vector3.Zero; float sum = 0;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(vertex.Bones >> (k * 8) & 255); float weight = vertex.Weights[k];
                if ((uint)bone >= (uint)bones.Length || weight <= 0 || !float.IsFinite(weight)) continue;
                point += Vector3.Transform(vertex.Position, bones[bone]) * weight;
                direction += Vector3.TransformNormal(vertex.Normal, bones[bone]) * weight; sum += weight;
            }
            if (sum <= 0) continue;
            centre += Vector3.Transform(point / sum, world); normal += Vector3.TransformNormal(direction / sum, world); valid++;
        }
        if (valid == 0 || normal.LengthSquared() < 1e-10f) return null;
        centre /= valid; normal = Vector3.Normalize(normal);
        return float.IsFinite(centre.LengthSquared()) && float.IsFinite(normal.LengthSquared()) ? new(centre, normal) : null;
    }
}
