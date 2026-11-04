using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering;

/// <summary>CPU equivalent of the pinned torso/head/limb halfspaces. Distances are evaluated in the original bind frame.</summary>
internal readonly record struct CutHalfspace(Vector4 Plane, int KeepSide = 1, int Group = -1, bool RejectOtherGroups = false,
    bool LegacyTorso = false, bool MorphedDistance = false)
{
    public float Band(Vector3 position, int group)
    {
        if (LegacyTorso)
        {
            if (group is 1 or 2 or 6 or 7) return KeepSide * 1000;
            if (group is 3 or 4) return -KeepSide * 1000;
        }
        if (Group >= 0 && !(Group == 7 ? group is 6 or 7 : group == Group)) return RejectOtherGroups ? -1000 : 1000;
        return (Vector3.Dot(new(Plane.X, Plane.Y, Plane.Z), position) + Plane.W) * KeepSide;
    }
}

internal sealed record CutMeshResult(OgreMesh Skin, OgreMesh? Wound, Vector3 Min, Vector3 Max, bool HasTriangles);

/// <summary>A convex local wound pocket to subtract, optionally limited to front-facing head/bone surfaces.</summary>
internal sealed record CutVolume(IReadOnlyList<CutHalfspace> Planes, Vector3 FacingNormal, bool FrontOnly = true, bool BoneOnly = false, int Group = 7, Vector4? Bounds = null);

/// <summary>
/// Clips triangles rather than collapsing their vertices. Edge samples keep UVs, tangent handedness, normalized bone weights and sparse Ogre poses.
/// Generated cut surfaces are separate coloured parts; their currently selected face morph is baked exactly once.
/// </summary>
internal static class MeshCut
{
    const float Epsilon = .00001f;
    const int VertexLimit = 1_000_000;
    internal readonly record struct Trace(uint A, uint B, uint C, Vector3 Bary);
    readonly record struct Sample(Vertex Vertex, Vector3 Morph, Trace Trace, uint CutMask = 0);
    sealed class Surface(int source)
    {
        public readonly int Source = source;
        public readonly List<Sample> Vertices = [];
    }
    readonly record struct Edge(Sample A, Sample B);

    public static CutMeshResult Clip(OgreMesh source, SkeletonRig rig, IReadOnlyList<CutHalfspace> cuts,
        IReadOnlyDictionary<string, float>? poseWeights = null, bool cutHorns = false, Vector4? capColour = null,
        IReadOnlyList<CutVolume>? removeVolumes = null)
    {
        if (cuts.Count > 31) throw new ArgumentOutOfRangeException(nameof(cuts));
        var model = Model.Build(source, rig.BoneCount, gameBinormals: true);
        var surfaces = Enumerable.Range(0, source.SubMeshes.Count).Select(i => new Surface(i)).ToArray();
        var edges = cuts.Select(_ => new List<Edge>()).ToArray();
        var groups = BoneGroups(rig);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        int total = 0;
        foreach (var part in model.Parts)
        {
            var morphs = MorphDeltas(source, part.SubMeshIndex, part.Vertices.Length, poseWeights, cutHorns);
            var target = surfaces[part.SubMeshIndex];
            for (int triangle = 0; triangle + 2 < part.Indices.Length; triangle += 3)
            {
                uint ia = part.Indices[triangle], ib = part.Indices[triangle + 1], ic = part.Indices[triangle + 2];
                if (ia >= part.Vertices.Length || ib >= part.Vertices.Length || ic >= part.Vertices.Length) continue;
                var a = part.Vertices[ia]; var b = part.Vertices[ib]; var c = part.Vertices[ic];
                int group = TriangleGroup(a, b, c, groups);
                var polygon = new List<Sample>(6)
                {
                    new(a, morphs[ia], new(ia, ib, ic, Vector3.UnitX)),
                    new(b, morphs[ib], new(ia, ib, ic, Vector3.UnitY)),
                    new(c, morphs[ic], new(ia, ib, ic, Vector3.UnitZ)),
                };
                for (int cutIndex = 0; cutIndex < cuts.Count && polygon.Count > 0; cutIndex++)
                    polygon = ClipPolygon(polygon, cuts[cutIndex], group, 1u << cutIndex);
                if (polygon.Count < 3) continue;
                var polygons = new List<List<Sample>> { polygon };
                if (removeVolumes is not null)
                {
                    var minimum = new Vector3(float.MaxValue); var maximum = new Vector3(float.MinValue);
                    foreach (var point in polygon) { minimum = Vector3.Min(minimum, point.Vertex.Position + point.Morph); maximum = Vector3.Max(maximum, point.Vertex.Position + point.Morph); }
                    foreach (var volume in removeVolumes)
                    {
                        if (polygons.Count == 0) break;
                        if (volume.Group >= 0 && !(volume.Group == 7 ? group is 6 or 7 : group == volume.Group)) continue;
                        if (volume.Bounds is { } sphere && Vector3.DistanceSquared(Vector3.Clamp(new(sphere.X, sphere.Y, sphere.Z), minimum, maximum), new(sphere.X, sphere.Y, sphere.Z)) > sphere.W * sphere.W) continue;
                        polygons = polygons.SelectMany(poly => Subtract(poly, volume, group)).ToList();
                    }
                }
                foreach (var keptPolygon in polygons)
                {
                polygon = keptPolygon;
                for (int i = 0; i < polygon.Count; i++)
                {
                    var p = polygon[i]; var q = polygon[(i + 1) % polygon.Count];
                    uint mask = p.CutMask & q.CutMask;
                    for (int cutIndex = 0; cutIndex < cuts.Count; cutIndex++)
                        if ((mask & (1u << cutIndex)) != 0 && Vector3.DistanceSquared(p.Vertex.Position + p.Morph, q.Vertex.Position + q.Morph) > Epsilon * Epsilon)
                            edges[cutIndex].Add(new(p, q));
                }
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    if (Vector3.Cross(polygon[i].Vertex.Position - polygon[0].Vertex.Position,
                        polygon[i + 1].Vertex.Position - polygon[0].Vertex.Position).LengthSquared() < 1e-14f) continue;
                    foreach (var vertex in new[] { polygon[0], polygon[i], polygon[i + 1] })
                    {
                        target.Vertices.Add(vertex);
                        min = Vector3.Min(min, vertex.Vertex.Position + vertex.Morph);
                        max = Vector3.Max(max, vertex.Vertex.Position + vertex.Morph);
                    }
                    total += 3;
                    if (total > VertexLimit) throw new InvalidDataException("Generated character geometry exceeds the vertex budget.");
                }
                }
            }
        }
        var skin = Encode(source, surfaces);
        var wound = new List<Vertex>();
        for (int i = 0; i < cuts.Count; i++) AppendCaps(edges[i], cuts[i], wound, capColour);
        return new(skin, wound.Count == 0 ? null : FromTriangles(wound, source.SkeletonName),
            total == 0 ? Vector3.Zero : min, total == 0 ? Vector3.Zero : max, total != 0);
    }

    static IEnumerable<List<Sample>> Subtract(List<Sample> polygon, CutVolume volume, int group)
    {
        if (volume.Group >= 0 && !(volume.Group == 7 ? group is 6 or 7 : group == volume.Group)) { yield return polygon; yield break; }
        var normal = polygon.Aggregate(Vector3.Zero, (sum, p) => sum + p.Vertex.Normal);
        if (volume.FrontOnly && Vector3.Dot(normal, volume.FacingNormal) <= .05f) { yield return polygon; yield break; }
        if (volume.BoneOnly)
        {
            var colour = polygon.Aggregate(Vector4.Zero, (sum, p) => sum + p.Vertex.Colour) / polygon.Count;
            if (!(colour.X > .45f && colour.Y > .35f && colour.Z < .38f)) { yield return polygon; yield break; }
        }
        // Entirely outside any one plane: this triangle cannot touch the pocket. This also preserves the exact posterior vertices.
        if (volume.Planes.Any(plane => polygon.All(p => plane.Band(p.Vertex.Position + (plane.MorphedDistance ? p.Morph : default), group) < -Epsilon)))
        { yield return polygon; yield break; }
        var remainder = polygon;
        foreach (var plane in volume.Planes)
        {
            var outside = ClipPolygon(remainder, plane with { KeepSide = -plane.KeepSide }, group, 0);
            if (outside.Count >= 3) yield return outside;
            remainder = ClipPolygon(remainder, plane, group, 0);
            if (remainder.Count < 3) yield break;
        }
        // The last remainder is inside the closed pocket and is discarded, without moving any survivor towards the origin.
    }

    static List<Sample> ClipPolygon(List<Sample> polygon, in CutHalfspace cut, int group, uint bit)
    {
        var result = new List<Sample>(polygon.Count + 1);
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            float da = cut.Band(a.Vertex.Position + (cut.MorphedDistance ? a.Morph : default), group);
            float db = cut.Band(b.Vertex.Position + (cut.MorphedDistance ? b.Morph : default), group);
            if (MathF.Abs(da) <= Epsilon) a = a with { CutMask = a.CutMask | bit };
            if (da >= -Epsilon) result.Add(a);
            if ((da < -Epsilon) == (db < -Epsilon)) continue;
            float t = Math.Clamp(da / (da - db), 0, 1);
            result.Add(Interpolate(a, b, t) with { CutMask = (a.CutMask & b.CutMask) | bit });
        }
        return result;
    }

    static Sample Interpolate(in Sample a, in Sample b, float t) => new(Interpolate(a.Vertex, b.Vertex, t),
        Vector3.Lerp(a.Morph, b.Morph, t), a.Trace with { Bary = Vector3.Lerp(a.Trace.Bary, b.Trace.Bary, t) });

    internal static Vertex Interpolate(in Vertex a, in Vertex b, float t)
    {
        Span<float> weights = stackalloc float[256];
        weights.Clear();
        for (int k = 0; k < 4; k++)
        {
            weights[(int)(a.Bones >> (k * 8) & 255)] += a.Weights[k] * (1 - t);
            weights[(int)(b.Bones >> (k * 8) & 255)] += b.Weights[k] * t;
        }
        uint bones = 0;
        var values = Vector4.Zero;
        for (int k = 0; k < 4; k++)
        {
            int best = 0;
            for (int bone = 1; bone < weights.Length; bone++) if (weights[bone] > weights[best]) best = bone;
            bones |= (uint)best << (k * 8);
            values[k] = weights[best];
            weights[best] = 0;
        }
        float sum = values.X + values.Y + values.Z + values.W;
        if (sum > Epsilon) values /= sum;
        var tangent = Vector4.Lerp(a.Tangent, b.Tangent, t);
        tangent.W = t < .5f ? a.Tangent.W : b.Tangent.W;
        return new Vertex { Position = Vector3.Lerp(a.Position, b.Position, t), Normal = Unit(Vector3.Lerp(a.Normal, b.Normal, t), a.Normal),
            Uv = Vector2.Lerp(a.Uv, b.Uv, t), Tangent = tangent, Colour = Vector4.Lerp(a.Colour, b.Colour, t), Bones = bones, Weights = values };
    }

    internal static int[] BoneGroups(SkeletonRig rig)
    {
        var groups = new int[rig.BoneCount];
        for (int bone = 0; bone < groups.Length; bone++)
            for (int parent = bone; parent >= 0 && parent < groups.Length; parent = rig.Parent[parent])
            {
                string name = rig.ByHandle[parent]?.Name ?? "";
                if (name == "Bip01 Jaw") { groups[bone] = 6; break; }
                if (name == "Bip01 Head") { groups[bone] = 7; break; }
                bool left = name.Contains(" L ", StringComparison.Ordinal), right = name.Contains(" R ", StringComparison.Ordinal);
                if (!(left || right) || name.Contains("Clavicle", StringComparison.Ordinal)) continue;
                bool leg = name.Contains("Thigh", StringComparison.Ordinal) || name.Contains("Calf", StringComparison.Ordinal)
                    || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal);
                groups[bone] = (leg ? 3 : 1) + (right ? 1 : 0);
                break;
            }
        return groups;
    }

    internal static int Group(in Vertex vertex, int[] groups)
    {
        int best = 0;
        for (int k = 1; k < 4; k++) if (vertex.Weights[k] > vertex.Weights[best]) best = k;
        int bone = (int)(vertex.Bones >> (best * 8) & 255);
        return bone < groups.Length ? groups[bone] : 0;
    }

    static int TriangleGroup(in Vertex a, in Vertex b, in Vertex c, int[] groups)
    {
        Span<float> votes = stackalloc float[8];
        votes.Clear();
        foreach (var vertex in new[] { a, b, c })
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(vertex.Bones >> (k * 8) & 255);
                votes[bone < groups.Length ? groups[bone] : 0] += vertex.Weights[k];
            }
        int best = 0;
        for (int i = 1; i < votes.Length; i++) if (votes[i] > votes[best]) best = i;
        return best;
    }

    internal static Vector3[] MorphDeltas(OgreMesh source, int subMesh, int count, IReadOnlyDictionary<string, float>? weights, bool cutHorns)
    {
        var result = new Vector3[count];
        if (weights is null) return result;
        bool shared = source.SubMeshes[subMesh].UseSharedVertices;
        foreach (var pose in source.Poses)
        {
            if (pose.Target != (shared ? 0 : subMesh + 1) || !weights.TryGetValue(pose.Name, out float stored)
                || CharacterShape.BakeWeight(pose.Name, stored, cutHorns) is not { } weight) continue;
            foreach (var vertex in pose.Vertices) if (vertex.Index < result.Length) result[vertex.Index] += vertex.Offset * weight;
        }
        return result;
    }

    static OgreMesh Encode(OgreMesh source, IReadOnlyList<Surface> surfaces)
    {
        var mesh = new OgreMesh { Version = source.Version, SkeletonName = source.SkeletonName, SkeletallyAnimated = source.SkeletallyAnimated };
        foreach (var surface in surfaces)
        {
            var original = source.SubMeshes[surface.Source];
            var sub = EncodePart(surface.Vertices.Select(v => v.Vertex).ToArray(), original.MaterialName);
            foreach (var (key, value) in original.TextureAliases) sub.TextureAliases.Add(key, value);
            mesh.SubMeshes.Add(sub);
            foreach (var pose in source.Poses.Where(p => p.Target == (original.UseSharedVertices ? 0 : surface.Source + 1)))
            {
                var sparse = pose.Vertices.GroupBy(p => p.Index).ToDictionary(g => g.Key,
                    g => new OgrePoseVertex(g.Key, g.Aggregate(Vector3.Zero, (sum, p) => sum + p.Offset), g.Aggregate(Vector3.Zero, (sum, p) => sum + p.Normal)));
                var output = new OgrePose { Name = pose.Name, Target = checked((ushort)mesh.SubMeshes.Count), IncludesNormals = pose.IncludesNormals };
                for (int i = 0; i < surface.Vertices.Count; i++)
                {
                    var trace = surface.Vertices[i].Trace;
                    var a = sparse.GetValueOrDefault(trace.A); var b = sparse.GetValueOrDefault(trace.B); var c = sparse.GetValueOrDefault(trace.C);
                    var offset = a.Offset * trace.Bary.X + b.Offset * trace.Bary.Y + c.Offset * trace.Bary.Z;
                    var normal = a.Normal * trace.Bary.X + b.Normal * trace.Bary.Y + c.Normal * trace.Bary.Z;
                    if (offset != Vector3.Zero || normal != Vector3.Zero) output.Vertices.Add(new((uint)i, offset, normal));
                }
                mesh.Poses.Add(output);
            }
        }
        SetBounds(mesh);
        return mesh;
    }

    internal static OgreMesh FromTriangles(IReadOnlyList<Vertex> vertices, string? skeleton = null)
    {
        var mesh = new OgreMesh { Version = "[MeshSerializer_v1.100]", SkeletonName = skeleton, SkeletallyAnimated = true };
        mesh.SubMeshes.Add(EncodePart(vertices, "brutality-generated"));
        SetBounds(mesh);
        return mesh;
    }

    static OgreSubMesh EncodePart(IReadOnlyList<Vertex> vertices, string material)
    {
        const ushort stride = 52;
        var bytes = new byte[vertices.Count * stride];
        var data = new OgreVertexData { VertexCount = checked((uint)vertices.Count) };
        data.Elements.AddRange([new(0, OgreVertexElementType.Float3, OgreVertexSemantic.Position, 0, 0),
            new(0, OgreVertexElementType.Float3, OgreVertexSemantic.Normal, 12, 0), new(0, OgreVertexElementType.Float2, OgreVertexSemantic.TextureCoordinates, 24, 0),
            new(0, OgreVertexElementType.Float4, OgreVertexSemantic.Tangent, 32, 0), new(0, OgreVertexElementType.ColourAbgr, OgreVertexSemantic.Diffuse, 48, 0)]);
        var sub = new OgreSubMesh { MaterialName = material, VertexData = data, Indices = new(true, Enumerable.Range(0, vertices.Count).Select(i => (uint)i).ToArray()) };
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(i * stride, 48));
            floats[0] = v.Position.X; floats[1] = v.Position.Y; floats[2] = v.Position.Z;
            floats[3] = v.Normal.X; floats[4] = v.Normal.Y; floats[5] = v.Normal.Z;
            floats[6] = v.Uv.X; floats[7] = v.Uv.Y;
            floats[8] = v.Tangent.X; floats[9] = v.Tangent.Y; floats[10] = v.Tangent.Z; floats[11] = v.Tangent.W;
            for (int k = 0; k < 4; k++)
            {
                bytes[i * stride + 48 + k] = (byte)Math.Clamp((int)MathF.Round(v.Colour[k] * 255), 0, 255);
                if (v.Weights[k] > 0) sub.BoneAssignments.Add(new((uint)i, (ushort)(v.Bones >> (k * 8) & 255), v.Weights[k]));
            }
        }
        data.Buffers.Add(0, new(stride, bytes));
        return sub;
    }

    static void SetBounds(OgreMesh mesh)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        bool any = false;
        foreach (var sub in mesh.SubMeshes)
            foreach (var p in sub.VertexData?.ReadPositions() ?? []) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true; }
        mesh.Bounds = any ? new(min, max, Math.Max(Vector3.Distance(min, max) * .5f, .001f)) : new(Vector3.Zero, Vector3.Zero, .001f);
    }

    static void AppendCaps(List<Edge> edges, in CutHalfspace cut, List<Vertex> output, Vector4? colour)
    {
        if (edges.Count == 0) return;
        var points = new List<Sample>();
        var byPoint = new Dictionary<(int X, int Y, int Z), int>();
        var links = new Dictionary<int, HashSet<int>>();
        int Add(Sample sample)
        {
            var p = sample.Vertex.Position + sample.Morph;
            var key = ((int)MathF.Round(p.X * 10000), (int)MathF.Round(p.Y * 10000), (int)MathF.Round(p.Z * 10000));
            if (byPoint.TryGetValue(key, out int found)) return found;
            int index = points.Count; points.Add(sample); byPoint.Add(key, index); links.Add(index, []); return index;
        }
        foreach (var edge in edges)
        {
            int a = Add(edge.A), b = Add(edge.B);
            if (a != b) { links[a].Add(b); links[b].Add(a); }
        }
        var seen = new HashSet<(int, int)>();
        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
        var kept = Unit(new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z), Vector3.UnitY) * cut.KeepSide;
        var u = Unit(Vector3.Cross(MathF.Abs(kept.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, kept), Vector3.UnitX);
        var v = Vector3.Cross(kept, u);
        foreach (var (start, neighbors) in links)
            foreach (int next in neighbors)
            {
                if (!seen.Add(Key(start, next))) continue;
                var loop = new List<int> { start };
                int previous = start, current = next;
                while (current != start && loop.Count <= points.Count)
                {
                    loop.Add(current);
                    int following = links[current].Where(x => x != previous && !seen.Contains(Key(current, x))).DefaultIfEmpty(-1).Min();
                    if (following < 0) break;
                    seen.Add(Key(current, following));
                    (previous, current) = (current, following);
                }
                if (current != start || loop.Count < 3) continue;
                var ring = loop.Select(index => points[index].Vertex with { Position = points[index].Vertex.Position + points[index].Morph }).ToArray();
                AppendRing(ring, kept, u, v, colour, output);
            }
    }

    static void AppendRing(Vertex[] ring, Vector3 kept, Vector3 u, Vector3 v, Vector4? colour, List<Vertex> output)
    {
        var centre = ring.Aggregate(Vector3.Zero, (sum, p) => sum + p.Position) / ring.Length;
        float radius = ring.Max(p => Vector3.Distance(p.Position, centre));
        float depth = Math.Clamp(radius * .30f, .12f, .75f);
        var inner = new Vertex[ring.Length];
        var floor = new Vertex[ring.Length];
        var rimColour = colour ?? ring.Aggregate(Vector4.Zero, (sum, p) => sum + p.Colour) / ring.Length;
        rimColour.W = 1;
        for (int i = 0; i < ring.Length; i++)
        {
            var p = ring[i];
            var q = p.Position;
            float ragged = .018f * MathF.Sin(q.X * 13 + q.Y * 7 + q.Z * 11) + .012f * MathF.Sin(q.X * 29 + q.Z * 17);
            ring[i] = p with { Position = q + kept * (.015f + ragged), Normal = -kept, Colour = rimColour, Uv = Vector2.Zero, Tangent = new(u, 1) };
            inner[i] = ring[i] with { Position = Vector3.Lerp(centre, q, .91f) + kept * .12f, Colour = rimColour * new Vector4(.65f, .65f, .65f, 1) };
            floor[i] = inner[i] with { Position = inner[i].Position + kept * depth, Colour = rimColour * new Vector4(.32f, .32f, .32f, 1) };
        }
        void Triangle(Vertex a, Vertex b, Vertex c)
        {
            if (Vector3.Dot(Vector3.Cross(b.Position - a.Position, c.Position - a.Position), -kept) < 0) (b, c) = (c, b);
            output.Add(a); output.Add(b); output.Add(c);
        }
        for (int i = 0; i < ring.Length; i++)
        {
            int j = (i + 1) % ring.Length;
            Triangle(ring[i], inner[i], ring[j]); Triangle(ring[j], inner[i], inner[j]);
            Triangle(inner[i], floor[i], inner[j]); Triangle(inner[j], floor[i], floor[j]);
        }
        foreach (var (a, b, c) in Triangulate(floor, u, v)) Triangle(floor[a], floor[b], floor[c]);
    }

    // Ear clipping retains concave section contours. Disconnected limbs are separate rings, never joined by a fan.
    static IEnumerable<(int A, int B, int C)> Triangulate(Vertex[] ring, Vector3 u, Vector3 v)
    {
        var p = ring.Select(x => new Vector2(Vector3.Dot(x.Position, u), Vector3.Dot(x.Position, v))).ToArray();
        float area = 0;
        for (int i = 0; i < p.Length; i++) area += Cross(p[i], p[(i + 1) % p.Length]);
        float sign = area >= 0 ? 1 : -1;
        var ids = Enumerable.Range(0, ring.Length).ToList();
        int attempts = 0;
        while (ids.Count > 3 && attempts++ < ring.Length * ring.Length)
        {
            bool removed = false;
            for (int i = 0; i < ids.Count; i++)
            {
                int a = ids[(i + ids.Count - 1) % ids.Count], b = ids[i], c = ids[(i + 1) % ids.Count];
                float turn = Cross(p[b] - p[a], p[c] - p[b]) * sign;
                if (MathF.Abs(turn) < Epsilon) { ids.RemoveAt(i); removed = true; break; }
                if (turn < 0) continue;
                bool inside = ids.Any(k => k != a && k != b && k != c && Cross(p[b] - p[a], p[k] - p[a]) * sign > Epsilon
                    && Cross(p[c] - p[b], p[k] - p[b]) * sign > Epsilon && Cross(p[a] - p[c], p[k] - p[c]) * sign > Epsilon);
                if (inside) continue;
                yield return (a, b, c); ids.RemoveAt(i); removed = true; break;
            }
            if (!removed) yield break;
        }
        if (ids.Count == 3) yield return (ids[0], ids[1], ids[2]);
    }

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    static Vector3 Unit(Vector3 value, Vector3 fallback) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;
}
